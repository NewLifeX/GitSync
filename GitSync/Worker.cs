using System.Diagnostics;
using GitSync.Models;
using NewLife.Serialization;

namespace GitSync;

/// <summary>
/// 后台任务。支持构造函数注入服务
/// </summary>
public class Worker(IHost host, ITracer tracer) : BackgroundService
{
    private readonly IHost _host = host;
    private readonly ITracer _tracer = tracer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var set = SyncSetting.Current;
        //XTrace.WriteLine("同步配置：{0}", set.ToJson(true));

        // 支持命令 AddAll {path} ，扫描指定目录并添加所有仓库
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "AddAll");
        if (idx > 0 && args.Length > idx + 1)
        {
            AddAll(args[idx + 1], set);
            return;
        }

        var ms = set.Repos;
        if (ms != null && ms.Length > 0)
        {
            foreach (var item in ms)
            {
                if (item.Enable) ProcessRepo(set.BaseDirectory, item);
            }
        }

        await Task.Delay(2_000, stoppingToken);

        _host.Close("同步完成");
        _host.TryDispose();

        //return Task.CompletedTask;
    }

    Boolean ProcessRepo(String basePath, Repo repo)
    {
        // 基础目录
        var path = !repo.Path.IsNullOrEmpty() ? repo.Path : basePath.CombinePath(repo.Name);
        if (path.IsNullOrEmpty()) return false;

        using var span = _tracer?.NewSpan(nameof(ProcessRepo), repo);
        XTrace.WriteLine("同步：{0}", path);

        // 本地所有分支
        String currentBranch = null;
        var branchs = repo.Branchs.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (branchs == null || branchs.Length == 0 || branchs.Length == 1 && branchs[0] == "*")
        {
            // 执行git branch命令，获得本地所有分支
            var rs = Execute("git", "branch", path);
            if (rs.IsNullOrEmpty()) return false;

            var ss = rs.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToArray();
            var list = new List<String>();
            foreach (var item in ss)
            {
                if (item.StartsWith('*'))
                {
                    currentBranch = item[1..].Trim();
                    list.Add(item[1..].Trim());
                }
                else
                {
                    list.Add(item);
                }
            }
            branchs = list.Distinct().ToArray();
        }
        XTrace.WriteLine("分支：{0}", branchs.ToJson());

        // 本地所有远程库
        var remotes = repo.Remotes.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (remotes == null || remotes.Length == 0 || remotes[0] == "*")
        {
            // 执行git branch命令，获得本地所有分支
            var rs = Execute("git", "branch -r", path);
            if (rs.IsNullOrEmpty()) return false;

            var ss = rs.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            var list = new List<String>();
            foreach (var item in ss)
            {
                var p = item.IndexOf('/');
                if (p > 0) list.Add(item[..p].Trim());
            }
            remotes = list.Distinct().ToArray();
        }
        XTrace.WriteLine("远程：{0}", remotes.ToJson());

        if (branchs == null || branchs.Length == 0)
        {
            ProcessRemotes(repo, path, null, remotes);
        }
        else
        {
            // 记住当前分支，最后要切回来
            currentBranch ??= branchs[0];
            foreach (var item in branchs)
            {
                // 切换分支
                ShellExecute("git", $"checkout {item}", path);

                ProcessRemotes(repo, path, item, remotes);
            }

            ShellExecute("git", $"checkout {currentBranch}", path);
        }

        return true;
    }

    void ProcessRemotes(Repo repo, String path, String branch, String[] remotes)
    {
        using var span = _tracer?.NewSpan(nameof(ProcessRepo), remotes);

        // git拉取所有远端
        foreach (var item in remotes)
        {
            // 拉取远程库
            ShellExecute("git", $"pull -v {item} {branch}", path);
        }

        // git推送所有远端
        foreach (var item in remotes)
        {
            // 推送远程库
            ShellExecute("git", $"push -v {item} {branch}", path);
        }
    }

    void AddAll(String basePath, SyncSetting set)
    {
        //XTrace.WriteLine("basePath: {0}", basePath);
        var di = basePath.AsDirectory();
        if (!di.Exists) return;

        // 扫描目录下所有仓库
        var list = set.Repos?.ToList() ?? [];
        foreach (var item in di.GetDirectories())
        {
            var path = item.FullName.CombinePath(".git");
            if (!Directory.Exists(path)) continue;

            var repo = new Repo
            {
                Name = item.Name,
                Path = item.FullName,
                Enable = true,
            };
            if (item.FullName.EqualIgnoreCase(set.BaseDirectory.CombinePath(repo.Name))) repo.Path = null;

            if (!list.Any(e => e.Name == repo.Name)) list.Add(repo);
        }

        //XTrace.WriteLine(list.ToJson(true));
        set.Repos = list.ToArray();
        set.Save();
    }

    private static String? Execute(String cmd, String? arguments = null, String? worker = null)
    {
        try
        {
            XTrace.WriteLine("{0} {1}", cmd, arguments);

            var psi = new ProcessStartInfo(cmd, arguments ?? String.Empty)
            {
                // UseShellExecute 必须 false，以便于后续重定向输出流
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                //RedirectStandardError = true,
                WorkingDirectory = worker,
            };
            var process = Process.Start(psi);
            if (process == null) return null;

            if (!process.WaitForExit(3_000))
            {
                process.Kill();
                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch { return null; }
    }

    private static Int32 ShellExecute(String cmd, String? arguments = null, String? worker = null)
    {
        try
        {
            XTrace.WriteLine("{0} {1}", cmd, arguments);

            var psi = new ProcessStartInfo(cmd, arguments ?? String.Empty)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                //WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = worker,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => XTrace.WriteLine(e.Data);
            process.ErrorDataReceived += (s, e) => XTrace.WriteLine(e.Data);
            process.Start();
            //if (process == null) return -1;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                return process.ExitCode;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            XTrace.Log.Error(ex.Message);
            return -2;
        }
    }
}
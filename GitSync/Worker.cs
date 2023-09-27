using System.Diagnostics;
using GitSync.Models;
using NewLife.Serialization;

namespace GitSync;

/// <summary>
/// 后台任务。支持构造函数注入服务
/// </summary>
public class Worker : BackgroundService
{
    private readonly IHost _host;

    public Worker(IHost host) => _host = host;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var set = SyncSetting.Current;
        XTrace.WriteLine("同步配置：{0}", set.ToJson(true));

        var ms = set.Repos;
        if (ms != null && ms.Length > 0)
        {
            foreach (var item in ms)
            {
                ProcessRepo(set.BaseDirectory, item);
            }
        }

        await Task.Delay(2_000);

        _host.TryDispose();

        //return Task.CompletedTask;
    }

    Boolean ProcessRepo(String basePath, Repo repo)
    {
        // 基础目录
        var path = !repo.Path.IsNullOrEmpty() ? repo.Path : basePath.CombinePath(repo.Name);
        if (path.IsNullOrEmpty()) return false;

        // 本地所有分支
        var branchs = repo.Branchs.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (branchs == null || branchs.Length == 0 || branchs[0] == "*")
        {
            // 执行git branch命令，获得本地所有分支
            var rs = Execute("git", "branch", path);
            if (rs.IsNullOrEmpty()) return false;

            branchs = rs.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim('*').Trim()).ToArray();
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

        return true;
    }

    private static String? Execute(String cmd, String? arguments = null, String? worker = null)
    {
        try
        {
#if DEBUG
            if (XTrace.Log.Level <= LogLevel.Debug) XTrace.WriteLine("Execute({0} {1})", cmd, arguments);
#endif

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
}
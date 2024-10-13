using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using GitSync.Models;
using NewLife.Remoting.Clients;
using NewLife.Serialization;
using Stardust.Registry;

namespace GitSync;

/// <summary>
/// 后台任务。支持构造函数注入服务
/// </summary>
public class Worker //: BackgroundService
{
    private readonly IHost _host;
    private IEventProvider _eventProvider;
    private readonly ITracer _tracer;

    /// <summary>
    /// 后台任务。支持构造函数注入服务
    /// </summary>
    public Worker(IHost host, IServiceProvider serviceProvider, ITracer tracer)
    {
        _host = host;
        _eventProvider = serviceProvider.GetService<IRegistry>() as IEventProvider;
        _tracer = tracer;
    }

    static Worker()
    {
        Environment.SetEnvironmentVariable("GIT_TEST_DEBUG_UNSAFE_DIRECTORIES", "true");

        Process.Start("git", "config --global --add safe.directory *");
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
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

        var ms = set.Repos?.Where(e => e.Enable).ToArray();
        if (ms != null && ms.Length > 0)
        {
            // 等待网络
            for (var i = 0; i < 300; i++)
            {
                if (NetworkInterface.GetIsNetworkAvailable()) break;

                Thread.Sleep(1000);
            }

            foreach (var item in ms)
            {
                if (item.Enable) ProcessRepo(set.BaseDirectory, item, set);
            }
            //Parallel.ForEach(ms, item =>
            //{
            //    lock (item.Name)
            //    {
            //        ProcessRepo(set.BaseDirectory, item, set);
            //    }
            //});
        }

        await Task.Delay(2_000, stoppingToken);

        _host?.Close("同步完成");
        _host?.TryDispose();

        //return Task.CompletedTask;
    }

    public Boolean ProcessRepo(String basePath, Repo repo, SyncSetting set)
    {
        // 基础目录
        var path = !repo.Path.IsNullOrEmpty() ? repo.Path : basePath.CombinePath(repo.Name);
        if (path.IsNullOrEmpty()) return false;

        using var span = _tracer?.NewSpan($"ProcessRepo-{repo.Name}", repo);
        WriteLog("同步：{0}", path);

        // 如果有旧的.git/index.lock锁定文件，删除之
        var file = path.CombinePath(".git/index.lock");
        if (File.Exists(file)) File.Delete(file);

        var gr = new GitRepo { Name = repo.Name, Path = path, Tracer = _tracer };
        gr.GetBranchs();

        // 如果本地有未提交文件，则跳过处理
        var changes = gr.GetChanges();
        if (changes.Count > 0) return false;

        // 本地所有分支
        var branchs = repo.Branchs.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (branchs == null || branchs.Length == 0 || branchs.Length == 1 && branchs[0] == "*")
            branchs = gr.Branchs;
        else
            gr.Branchs = branchs;

        WriteLog("所有分支：{0}", branchs.ToJson());

        // 本地所有远程库
        var remotes = repo.Remotes.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (remotes == null || remotes.Length == 0 || remotes[0] == "*")
            remotes = gr.GetRemotes();
        else
            gr.Remotes = remotes;

        WriteLog("所有远程：{0}", remotes.ToJson());

        if (branchs == null || branchs.Length == 0)
        {
            gr.PullAll(null);

            if (repo.UpdateMode > 0) Update(repo, gr, path, set);

            gr.PushAll(null);
        }
        else
        {
            // 记住当前分支，最后要切回来
            var currentBranch = gr.CurrentBranch ?? branchs[0];
            foreach (var item in branchs)
            {
                using var span2 = _tracer?.NewSpan($"ProcessBranch-{item}", repo);
                WriteLog("分支：{0}", path);

                // 切换分支
                gr.Checkout(item);
                gr.PullAll(item);

                if (repo.UpdateMode > 0 && item == currentBranch)
                    Update(repo, gr, path, set);

                gr.PushAll(item);
            }

            gr.Checkout(currentBranch);
        }

        return true;
    }

    void AddAll(String basePath, SyncSetting set)
    {
        using var span = _tracer?.NewSpan("AddAll", basePath);

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

    private static Boolean _check;
    void Update(Repo repo, GitRepo gr, String path, SyncSetting set)
    {
        using var span = _tracer?.NewSpan("NugetUpgrade", path);

        if (!_check)
        {
            CheckTool();
            _check = true;
        }

        // 如果有多个解决方案，选择最大的那个
        var sln = "";
        var slns = ".".AsDirectory().GetFiles("*.sln").OrderByDescending(e => e.Length).ToList();
        if (slns.Count > 1) sln = slns[0].Name;

        // 更新Nuget包
        var timeout = 300_000;
        //"dotnet-outdated".Run("-u", 30_000, null, null, path);
        switch (repo.UpdateMode)
        {
            case UpdateModes.None:
                return;
            case UpdateModes.Exclude:
                // 需要排除指定项，因此需要先查询所有可升级包
                var pkgs = new List<String>();
                var excludes = (set.Excludes + "").Split(",", StringSplitOptions.RemoveEmptyEntries);
                if (excludes != null && excludes.Length > 0)
                {
                    var result = "dotnet-outdated".Run($"-pre Never {sln}", timeout, null, null, path);
                    var lines = (result + "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in lines)
                    {
                        if (!item.Contains("->")) continue;

                        var line = item.Trim();
                        var p = line.IndexOf(' ');
                        if (p < 0) continue;

                        var name = line[..p].Trim();
                        if (!name.IsNullOrEmpty() && excludes.Any(e => e.IsMatch(name, StringComparison.OrdinalIgnoreCase)))
                            pkgs.Add(name);
                    }
                }
                if (pkgs.Count > 0)
                    "dotnet-outdated".Run($"-u -pre Never {sln} " + pkgs.Join(" ", e => $"-exc {e}"), timeout, null, null, path);
                else
                    "dotnet-outdated".Run($"-u -pre Never {sln}", timeout, null, null, path);
                break;
            case UpdateModes.Default:
                "dotnet-outdated".Run($"-u -pre Never {sln}", timeout, null, null, path);
                break;
            case UpdateModes.Full:
                "dotnet-outdated".Run($"-u {sln}", timeout, null, null, path);
                break;
            default:
                break;
        }

        // 是否有文件更新
        var changes = gr.GetChanges();
        if (changes.Count == 0) return;

        // 编译
        var exitCode = "dotnet".Run("build", timeout, null, null, path);

        // Git提交。编译成功才提交，否则回滚
        if (exitCode == 0)
        {
            WriteLog("{0}编译成功，提交", repo.Name);
            "git".Run("commit -a -m \"Upgrade Nuget\"", 15_000, null, null, path);
        }
        else
        {
            WriteLog("{0}编译失败（ExitCode={1}），回滚", repo.Name, exitCode);
            "git".Run("reset --hard", 15_000, null, null, path);
        }
    }

    void CheckTool()
    {
        var rs = "dotnet".Execute("tool list -g", 30_000, true, Encoding.UTF8);

        var ss = rs?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var line = ss?.FirstOrDefault(e => e.StartsWith("dotnet-outdated-tool"));
        if (line.IsNullOrEmpty())
        {
            rs = "dotnet".Execute("tool install dotnet-outdated-tool -g", 30_000, true, Encoding.UTF8);
            WriteLog(rs);
        }
    }

    private void WriteLog(String format, params Object[] args)
    {
        XTrace.WriteLine(format, args);
        _eventProvider?.WriteInfoEvent("Worker", String.Format(format, args));
    }
}
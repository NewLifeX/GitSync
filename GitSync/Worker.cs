using System.Diagnostics;
using System.Net.NetworkInformation;
using GitSync.Models;
using GitSync.Services;
using NewLife.Remoting.Clients;
using NewLife.Serialization;
using NewLife.Threading;
using Stardust;
using Stardust.Registry;

namespace GitSync;

/// <summary>
/// 后台任务。支持构造函数注入服务
/// </summary>
public class Worker : IHostedService
{
    private readonly IHost _host;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventProvider _eventProvider;
    private readonly ITracer _tracer;

    /// <summary>
    /// 后台任务。支持构造函数注入服务
    /// </summary>
    public Worker(IHost host, IServiceProvider serviceProvider, ITracer tracer)
    {
        _host = host;
        _serviceProvider = serviceProvider;
        _eventProvider = serviceProvider.GetService<IRegistry>() as IEventProvider;
        _tracer = tracer;
    }

    static Worker()
    {
        Environment.SetEnvironmentVariable("GIT_TEST_DEBUG_UNSAFE_DIRECTORIES", "true");

        Process.Start("git", "config --global --add safe.directory *");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        CheckTimer();

        // 配置改变时重新加载
        SyncSetting.Provider.Changed += Provider_Changed;

        // 考虑到执行时间到达时计算机可能未启动，下次启动时，如果错过了某次执行，则立马执行。
        var set = SyncSetting.Current;
        if (set.LastSync.Year > 2000 && _timer?.Crons != null)
        {
            foreach (var cron in _timer.Crons)
            {
                var next = cron.GetNext(set.LastSync);
                if (next < DateTime.Now)
                {
                    WriteLog("错过了[{0}]的执行时间{1}，立即执行", cron, set.LastSync);
                    _timer.SetNext(-1);
                    break;
                }
            }
        }

        var factory = _serviceProvider.GetService<StarFactory>();
        if (factory != null && factory.App != null)
        {
            factory.App.RegisterCommand("test", Test);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SyncSetting.Provider.Changed -= Provider_Changed;

        _timer.TryDispose();
        _lastCrons = null;

        return Task.CompletedTask;
    }

    private void Provider_Changed(Object sender, EventArgs e) => CheckTimer();

    private String Test(String arg)
    {
        if (arg.IsNullOrEmpty())
            _timer?.SetNext(-1);
        else
        {
            var set = SyncSetting.Current;
            var repo = set.Repos.FirstOrDefault(e => e.Name.EqualIgnoreCase(arg));
            if (repo == null) return "未找到仓库" + arg;

            //var tracer = ServiceProvider.GetService<ITracer>();
            //var worker = new Worker(null, ServiceProvider, tracer);
            //var worker = ServiceProvider.CreateInstance(typeof(Worker)) as Worker;

            using var scope = _serviceProvider.CreateScope();

            ProcessRepo(set.BaseDirectory, repo, set, scope.ServiceProvider);
        }

        return "OK";
    }

    String _lastCrons;
    void CheckTimer()
    {
        // 如果配置未变化，则不处理。首次_lastCrons为空
        var set = SyncSetting.Current;
        var crons = set.Crons + "";
        if (crons == _lastCrons) return;
        _lastCrons = crons;

        // 配置变化，重新加载定时器
        _timer.TryDispose();

        WriteLog("创建定时器：{0}", crons);

        var next = DateTime.MaxValue;
        if (!crons.IsNullOrEmpty())
        {
            _timer = new TimerX(DoWork, null, crons) { Async = true };

        }
        else
        {
            _timer = new TimerX(DoWork, null, 1000, 3600_000) { Async = true };
        }

        XTrace.WriteLine("下次执行时间：{0}", _timer.NextTime);
    }

    private TimerX _timer;
    private void DoWork(Object state)
    {
        //XTrace.WriteLine("DoWork");

        //var tracer = ServiceProvider.GetService<ITracer>();
        //var worker = new Worker(null, ServiceProvider, tracer);
        //var worker = ServiceProvider.CreateInstance(typeof(Worker)) as Worker;
        //await ExecuteAsync(default);

        using var scope = _serviceProvider.CreateScope();

        SyncRepos(scope.ServiceProvider);

        var set = SyncSetting.Current;
        set.LastSync = DateTime.Now;
        set.Save();

        WriteLog("同步完成！");

        CheckTimer();

        //// 如果是Windows系统，设置睡眠自动唤醒任务
        //if (Runtime.Windows && _timer != null && _timer.Crons != null)
        //{
        //    var now = DateTime.Now;
        //    var nextTime = _timer.Crons.Min(e => e.GetNext(now));
        //    if (nextTime.Year > 2000) CreateWakeUpTask(nextTime);
        //}
    }

    protected void SyncRepos(IServiceProvider serviceProvider)
    {
        var set = SyncSetting.Current;
        //XTrace.WriteLine("同步配置：{0}", set.ToJson(true));

        // 阻止系统进入睡眠状态
        SystemSleep.Prevent(false);
        try
        {
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
                    if (item.Enable) ProcessRepo(set.BaseDirectory, item, set, serviceProvider);
                }
                //Parallel.ForEach(ms, item =>
                //{
                //    lock (item.Name)
                //    {
                //        ProcessRepo(set.BaseDirectory, item, set);
                //    }
                //});
            }
        }
        finally
        {
            // 恢复系统睡眠状态
            SystemSleep.Restore();
        }
    }

    public Boolean ProcessRepo(String basePath, Repo repo, SyncSetting set, IServiceProvider serviceProvider)
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

        //// 如果本地有未提交文件，则跳过处理
        //var changes = gr.GetChanges();
        //if (changes.Count > 0) return false;

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

        var nuget = serviceProvider.GetRequiredService<NugetService>();
        if (branchs == null || branchs.Length == 0)
        {
            gr.PullAll(null);

            if (repo.UpdateMode > 0) nuget.Update(repo, gr, path, set);

            gr.PushAll(null);
        }
        else
        {
            // 记住当前分支，最后要切回来
            var currentBranch = gr.CurrentBranch ?? branchs[0];
            // 当前分支必须在第一位，避免有些修改被切到其它分支上
            if (!currentBranch.IsNullOrEmpty() && branchs.Length > 0 && currentBranch != branchs[0])
            {
                var bs = branchs.ToList();
                bs.Remove(currentBranch);
                bs.Insert(0, currentBranch);
            }
            foreach (var item in branchs)
            {
                using var span2 = _tracer?.NewSpan($"ProcessBranch-{item}", repo);
                WriteLog("分支：{0}", path);

                // 切换分支
                gr.Checkout(item);
                gr.PullAll(item);

                if (repo.UpdateMode > 0 && item == currentBranch)
                {
                    nuget.Update(repo, gr, path, set);
                }

                gr.PushAll(item);

                // 如果本地有未提交文件，则跳过处理
                var changes = gr.GetChanges();
                if (changes.Count > 0) break;
            }

            gr.Checkout(currentBranch);
        }

        return true;
    }

    public void AddAll(String basePath, SyncSetting set)
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

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        _eventProvider?.WriteInfoEvent("Worker", String.Format(format, args));
    }
}
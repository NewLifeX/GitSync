﻿using NewLife.Agent;
using NewLife.Model;
using NewLife.Remoting.Clients;
using NewLife.Threading;
using Stardust;

namespace GitSync;

internal class MyService : ServiceBase
{
    #region 属性
    public IServiceProvider ServiceProvider { get; set; }
    #endregion

    #region 构造

    #endregion

    #region 方法
    public override void StartWork(String reason)
    {
        CheckTimer();

        // 配置改变时重新加载
        SyncSetting.Provider.Changed += Provider_Changed;

        base.StartWork(reason);

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

        var factory = ServiceProvider.GetService<StarFactory>();
        if (factory != null && factory.App != null)
        {
            factory.App.RegisterCommand("test", Test);
        }
    }

    private void Provider_Changed(Object sender, EventArgs e) => CheckTimer();

    public override void StopWork(String reason)
    {
        SyncSetting.Provider.Changed -= Provider_Changed;

        _timer.TryDispose();
        _lastCrons = null;

        base.StopWork(reason);
    }

    private String Test(String arg)
    {
        if (arg.IsNullOrEmpty())
            _timer?.SetNext(-1);
        else
        {
            var set = SyncSetting.Current;
            var repo = set.Repos.FirstOrDefault(e => e.Name.EqualIgnoreCase(arg));
            if (repo == null) return "未找到仓库" + arg;

            var tracer = ServiceProvider.GetService<ITracer>();
            var worker = new Worker(null, ServiceProvider, tracer);
            //var worker = ServiceProvider.CreateInstance(typeof(Worker)) as Worker;
            worker.ProcessRepo(set.BaseDirectory, repo, set);
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
    private async Task DoWork(Object state)
    {
        //XTrace.WriteLine("DoWork");

        var tracer = ServiceProvider.GetService<ITracer>();
        var worker = new Worker(null, ServiceProvider, tracer);
        //var worker = ServiceProvider.CreateInstance(typeof(Worker)) as Worker;
        await worker.ExecuteAsync(default);

        var set = SyncSetting.Current;
        set.LastSync = DateTime.Now;
        set.Save();

        WriteLog("同步完成！");

        CheckTimer();
    }
    #endregion
}

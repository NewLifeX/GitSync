using NewLife.Agent;
using NewLife.Threading;

namespace GitSync;

internal class MyService : ServiceBase
{
    #region 属性
    public IServiceProvider ServiceProvider { get; set; }
    #endregion

    #region 构造

    #endregion

    #region 方法
    protected override void StartWork(String reason)
    {
        CheckTimer();

        // 配置改变时重新加载
        SyncSetting.Provider.Changed += (s, e) => CheckTimer();

        base.StartWork(reason);

        // 考虑到执行时间到达时计算机可能未启动，下次启动时，如果错过了某次执行，则立马执行。
        var set = SyncSetting.Current;
        foreach (var cron in _timer.Crons)
        {
            if (set.LastSync.Year > 2000)
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
    }

    protected override void StopWork(String reason)
    {
        _timer.TryDispose();

        base.StopWork(reason);
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
        var worker = new Worker(null, tracer);
        await worker.ExecuteAsync(default);

        var set = SyncSetting.Current;
        set.LastSync = DateTime.Now;
        set.Save();

        WriteLog("同步完成！");

        CheckTimer();
    }
    #endregion
}

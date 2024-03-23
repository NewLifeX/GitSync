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
    }

    protected override void StopWork(String reason)
    {
        _timers.TryDispose();
        _timers.Clear();

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
        _timers.TryDispose();
        _timers.Clear();

        WriteLog("创建定时器：{0}", crons);

        var next = DateTime.MaxValue;
        if (!crons.IsNullOrEmpty())
        {
            // 多个Cron表达式，创建多个定时器
            foreach (var item in crons.Split(";"))
            {
                var timer = new TimerX(DoWork, null, item) { Async = true };
                _timers.Add(timer);

                if (next > timer.NextTime) next = timer.NextTime;
            }
        }
        else
        {
            var timer = new TimerX(DoWork, null, 1000, 3600_000) { Async = true };
            _timers.Add(timer);

            next = timer.NextTime;
        }

        XTrace.WriteLine("下次执行时间：{0}", next);
    }

    private IList<TimerX> _timers = [];
    private async Task DoWork(Object state)
    {
        //XTrace.WriteLine("DoWork");

        var tracer = ServiceProvider.GetService<ITracer>();
        var worker = new Worker(null, tracer);
        await worker.ExecuteAsync(default);

        WriteLog("同步完成！");

        CheckTimer();
    }
    #endregion
}

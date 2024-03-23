using NewLife.Agent;
using NewLife.Threading;

namespace GitSync;

internal class MyService : ServiceBase
{
    #region 属性

    #endregion

    #region 构造

    #endregion

    #region 方法
    protected override void StartWork(String reason)
    {
        CheckTimer();

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
        if (set.Crons + "" == _lastCrons) return;

        // 配置变化，重新加载定时器
        _timers.TryDispose();
        _timers.Clear();

        if (!set.Crons.IsNullOrEmpty())
        {
            // 多个Cron表达式，创建多个定时器
            foreach (var item in set.Crons.Split(";"))
            {
                var timer = new TimerX(DoWork, null, item) { Async = true };
                _timers.Add(timer);
            }
        }
        else
        {
            var timer = new TimerX(DoWork, null, 1000, 3600_000) { Async = true };
            _timers.Add(timer);
        }
    }

    private IList<TimerX> _timers = [];
    private void DoWork(Object state)
    {
        XTrace.WriteLine("DoWork");

        CheckTimer();
    }
    #endregion
}

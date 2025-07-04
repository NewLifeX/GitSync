﻿using System.Diagnostics;
using GitSync.Services;
using NewLife.Remoting.Clients;
using NewLife.Threading;
using Stardust;
using Stardust.Registry;

namespace GitSync;

/// <summary>
/// 后台任务。支持构造函数注入服务
/// </summary>
/// <remarks>
/// 后台任务。支持构造函数注入服务
/// </remarks>
public class Worker(IServiceProvider serviceProvider) : IHostedService
{
    private readonly IEventProvider _eventProvider = serviceProvider.GetService<IRegistry>() as IEventProvider;

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

        // 注册命令。星尘平台应用在线页面，可以给该应用发送test命令
        var factory = serviceProvider.GetService<StarFactory>();
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

            using var scope = serviceProvider.CreateScope();
            var provider = scope.ServiceProvider;
            var gitService = provider.GetService<GitService>();

            gitService.ProcessRepo(set.BaseDirectory, repo, set, provider);
        }

        return "OK";
    }

    String _lastCrons;
    void CheckTimer()
    {
        // 如果配置未变化，则不处理。首次_lastCrons为空
        var set = SyncSetting.Current;
        var crons = set.Crons + "";
        if (crons == _lastCrons)
        {
            var time = _timer.Crons.Min(e => e.GetNext(DateTime.Now));
            XTrace.WriteLine("下次执行时间：{0}", time);
            return;
        }

        _lastCrons = crons;

        // 配置变化，重新加载定时器
        _timer.TryDispose();

        WriteLog("创建定时器：{0}", crons);

        var next = DateTime.MaxValue;
        if (!crons.IsNullOrEmpty())
            _timer = new TimerX(DoWork, null, crons) { Async = true };
        else
            _timer = new TimerX(DoWork, null, 1000, 3600_000) { Async = true };

        XTrace.WriteLine("下次执行时间：{0}", _timer.NextTime);
    }

    private TimerX _timer;
    private void DoWork(Object state)
    {
        WriteLog("开始同步！");

        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider;
        var gitService = provider.GetService<GitService>();

        gitService.SyncRepos(provider);

        var set = SyncSetting.Current;
        set.LastSync = DateTime.Now;
        set.Save();

        WriteLog("同步完成！");

        CheckTimer();
    }

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        if (_eventProvider != null && !format.IsNullOrEmpty())
        {
            if (format.Contains("错误") || format.Contains("异常"))
                _eventProvider.WriteErrorEvent(GetType().Name, String.Format(format, args));
            else
                _eventProvider.WriteInfoEvent(GetType().Name, String.Format(format, args));
        }
    }
}
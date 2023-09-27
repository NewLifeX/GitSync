using NewLife.Remoting;
using NewLife.Serialization;
using Stardust;

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

        await Task.Delay(2_000);

        _host.TryDispose();

        //return Task.CompletedTask;
    }
}
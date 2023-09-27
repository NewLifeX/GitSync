using NewLife.Remoting;
using NewLife.Serialization;
using Stardust;

namespace GitSync;

/// <summary>
/// 后台任务。支持构造函数注入服务
/// </summary>
public class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}
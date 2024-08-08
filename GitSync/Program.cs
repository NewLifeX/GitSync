using GitSync;
using Stardust;

//!!! 轻量级控制台项目模板

internal class Program
{
    private static void Main(string[] args)
    {
        //Runtime.CreateConfigOnMissing = false;

        // 启用控制台日志，拦截所有异常
        XTrace.UseConsole();

        // 初始化对象容器，提供注入能力
        var services = ObjectContainer.Current;
        services.AddStardust();

        var set = SyncSetting.Current;
        if (set.IsNew)
        {
            set.Crons = "0 0 * * * ?";
            set.Save();
        }

        new MyService { ServiceProvider = services.BuildServiceProvider() }.Main(args);

        //// 注册后台任务 IHostedService
        //services.AddHostedService<Worker>();

        //// 异步阻塞，友好退出
        //var host = services.BuildHost();
        //await host.RunAsync();
    }
}

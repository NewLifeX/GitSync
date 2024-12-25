using GitSync;
using Stardust;

//!!! 轻量级控制台项目模板

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

services.AddSingleton<Worker>();
var provider = services.BuildServiceProvider();
var host = services.BuildHost();

// 支持命令 AddAll {path} ，扫描指定目录并添加所有仓库
//var args = Environment.GetCommandLineArgs();
var idx = Array.IndexOf(args, "AddAll");
if (idx >= 0 && args.Length > idx + 1)
{
    var wrk = provider.GetService<Worker>();
    wrk.AddAll(args[idx + 1], set);
    return;
}

new MyService { ServiceProvider = provider }.Main(args);

//// 注册后台任务 IHostedService
//services.AddHostedService<Worker>();

//// 异步阻塞，友好退出
//var host = services.BuildHost();
//await host.RunAsync();

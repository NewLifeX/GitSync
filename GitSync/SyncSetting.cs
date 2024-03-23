using System.ComponentModel;
using GitSync.Models;
using NewLife.Configuration;

namespace GitSync;

/// <summary>设置</summary>
[Config("Sync")]
public class SyncSetting : Config<SyncSetting>
{
    /// <summary>基础目录</summary>
    [Description("基础目录")]
    public String BaseDirectory { get; set; }

    /// <summary>Cron表达式。控制定时执行，多个分号隔开</summary>
    [Description("Cron表达式。控制定时执行，多个分号隔开")]
    public String Crons { get; set; }

    /// <summary>最后同步。记录最后一次同步时间。考虑到执行时间到达时计算机可能未启动，下次启动时，如果错过了某次执行，则立马执行。</summary>
    [Description("最后同步。记录最后一次同步时间。考虑到执行时间到达时计算机可能未启动，下次启动时，如果错过了某次执行，则立马执行。")]
    public DateTime LastSync { get; set; }

    /// <summary>集合</summary>
    [Description("集合")]
    public Repo[] Repos { get; set; }

    protected override void OnLoaded()
    {
        var ms = Repos;
        if (ms == null || ms.Length == 0)
        {
            Repos =
            [
                new Repo { Name = "test", Branchs = "dev,master", Remotes = "origin,github" },
                new Repo { Name = "test2", Branchs = "dev,master", Remotes = "origin,github" },
            ];
        }

        base.OnLoaded();
    }
}

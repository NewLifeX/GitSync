﻿using System.ComponentModel;
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

    /// <summary>集合</summary>
    [Description("集合")]
    public Repo[] Repos { get; set; }

    protected override void OnLoaded()
    {
        var ms = Repos;
        if (ms == null || ms.Length == 0)
        {
            Repos = new[] {
                new Repo {
                    Name = "test",
                    Branchs = "dev,master",
                    Remotes = "origin,github"
                },
                new Repo {
                    Name = "test2",
                    Branchs = "dev,master",
                    Remotes = "origin,github"
                },
            };
        }

        base.OnLoaded();
    }
}

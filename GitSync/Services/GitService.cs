﻿using System.Net.NetworkInformation;
using GitSync.Models;
using NewLife.Remoting.Clients;
using NewLife.Serialization;

namespace GitSync.Services;

internal class GitService
{
    private static Boolean _check;
    private readonly IEventProvider _eventProvider;
    private readonly ITracer _tracer;

    public GitService(IEventProvider eventProvider, ITracer tracer)
    {
        _eventProvider = eventProvider;
        _tracer = tracer;
    }

    public void SyncRepos(IServiceProvider serviceProvider)
    {
        var set = SyncSetting.Current;
        //XTrace.WriteLine("同步配置：{0}", set.ToJson(true));

        // 阻止系统进入睡眠状态
        SystemSleep.Prevent(false);
        try
        {
            var ms = set.Repos?.Where(e => e.Enable).ToArray();
            if (ms != null && ms.Length > 0)
            {
                // 等待网络
                for (var i = 0; i < 300; i++)
                {
                    if (NetworkInterface.GetIsNetworkAvailable())
                    {
                        var ping = new Ping();
                        var rs = ping.Send("www.baidu.com");
                        if (rs.Status == IPStatus.Success) break;
                    }

                    Thread.Sleep(1000);
                }

                foreach (var item in ms)
                {
                    if (item.Enable) ProcessRepo(set.BaseDirectory, item, set, serviceProvider);
                }
                //Parallel.ForEach(ms, item =>
                //{
                //    lock (item.Name)
                //    {
                //        ProcessRepo(set.BaseDirectory, item, set);
                //    }
                //});
            }
        }
        finally
        {
            // 恢复系统睡眠状态
            SystemSleep.Restore();
        }
    }

    public Boolean ProcessRepo(String basePath, Repo repo, SyncSetting set, IServiceProvider serviceProvider)
    {
        // 基础目录
        var path = !repo.Path.IsNullOrEmpty() ? repo.Path : basePath.CombinePath(repo.Name);
        if (path.IsNullOrEmpty()) return false;

        using var span = _tracer?.NewSpan($"ProcessRepo-{repo.Name}", repo);
        WriteLog("同步：{0}", path);

        // 如果有旧的.git/index.lock锁定文件，删除之
        var file = path.CombinePath(".git/index.lock");
        if (File.Exists(file)) File.Delete(file);

        var gr = new GitRepo { Name = repo.Name, Path = path, Tracer = _tracer };
        gr.GetBranchs();

        //// 如果本地有未提交文件，则跳过处理
        //var changes = gr.GetChanges();
        //if (changes.Count > 0) return false;

        // 本地所有分支
        var branchs = repo.Branchs.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (branchs == null || branchs.Length == 0 || branchs.Length == 1 && branchs[0] == "*")
            branchs = gr.Branchs;
        else
            gr.Branchs = branchs;

        WriteLog("所有分支：{0}", branchs.ToJson());

        // 本地所有远程库
        var remotes = repo.Remotes.Split(",", StringSplitOptions.RemoveEmptyEntries);
        if (remotes == null || remotes.Length == 0 || remotes[0] == "*")
            remotes = gr.GetRemotes();
        else
            gr.Remotes = remotes;

        WriteLog("所有远程：{0}", remotes.ToJson());

        var nuget = serviceProvider.GetRequiredService<NugetService>();
        var project = serviceProvider.GetRequiredService<ProjectService>();
        if (branchs == null || branchs.Length == 0)
        {
            gr.PullAll(null);

            project.UpdateReadme(repo, gr, path, set);
            project.UpdateVersion(repo, gr, path, set);
            if (repo.UpdateMode > 0) nuget.Update(repo, gr, path, set);

            gr.PushAll(null);
        }
        else
        {
            // 记住当前分支，最后要切回来
            var currentBranch = gr.CurrentBranch ?? branchs[0];
            // 当前分支必须在第一位，避免有些修改被切到其它分支上
            if (!currentBranch.IsNullOrEmpty() && branchs.Length > 0 && currentBranch != branchs[0])
            {
                var bs = branchs.ToList();
                bs.Remove(currentBranch);
                bs.Insert(0, currentBranch);
            }
            foreach (var item in branchs)
            {
                using var span2 = _tracer?.NewSpan($"ProcessBranch-{item}", repo);
                WriteLog("分支：{0}", path);

                // 切换分支
                gr.Checkout(item);
                gr.PullAll(item);

                if (item == currentBranch)
                {
                    project.UpdateReadme(repo, gr, path, set);
                    project.UpdateVersion(repo, gr, path, set);
                    if (repo.UpdateMode > 0) nuget.Update(repo, gr, path, set);
                }

                gr.PushAll(item);

                // 如果本地有未提交文件，则跳过处理
                var changes = gr.GetChanges();
                if (changes.Count > 0) break;
            }

            gr.Checkout(currentBranch);
        }

        return true;
    }

    public void AddAll(String basePath, SyncSetting set)
    {
        using var span = _tracer?.NewSpan("AddAll", basePath);

        //XTrace.WriteLine("basePath: {0}", basePath);
        var di = basePath.AsDirectory();
        if (!di.Exists) return;

        // 扫描目录下所有仓库
        var list = set.Repos?.ToList() ?? [];
        foreach (var item in di.GetDirectories())
        {
            var path = item.FullName.CombinePath(".git");
            if (!Directory.Exists(path)) continue;

            var repo = new Repo
            {
                Name = item.Name,
                Path = item.FullName,
                Enable = true,
            };
            if (item.FullName.EqualIgnoreCase(set.BaseDirectory.CombinePath(repo.Name))) repo.Path = null;

            if (!list.Any(e => e.Name == repo.Name)) list.Add(repo);
        }

        //XTrace.WriteLine(list.ToJson(true));
        set.Repos = list.ToArray();
        set.Save();
    }

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        _eventProvider?.WriteInfoEvent("Worker", String.Format(format, args));
    }
}
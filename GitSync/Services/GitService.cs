using System.Net.NetworkInformation;
using GitSync.Models;
using NewLife.Remoting.Clients;
using NewLife.Serialization;

namespace GitSync.Services;

internal class GitService(IServiceProvider serviceProvider, ITracer tracer)
{
    private readonly IEventProvider _eventProvider = serviceProvider.GetService<IEventProvider>();

    public void SyncRepos(IServiceProvider serviceProvider)
    {
        var set = SyncSetting.Current;
        //XTrace.WriteLine("同步配置：{0}", set.ToJson(true));

        using var span = tracer?.NewSpan(nameof(SyncRepos));

        // 阻止系统进入睡眠状态
        WriteLog("阻止系统进入睡眠状态");
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
                        span?.AppendTag(rs.Status + "");
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
        catch (Exception ex)
        {
            span?.SetError(ex, null);
        }
        finally
        {
            // 恢复系统睡眠状态
            WriteLog("恢复系统睡眠状态");
            SystemSleep.Restore();
        }
    }

    public Boolean ProcessRepo(String basePath, Repo repo, SyncSetting set, IServiceProvider serviceProvider)
    {
        // 基础目录
        var path = !repo.Path.IsNullOrEmpty() ? repo.Path : basePath.CombinePath(repo.Name);
        if (path.IsNullOrEmpty()) return false;

        using var span = tracer?.NewSpan($"ProcessRepo-{repo.Name}", repo);
        WriteLog("同步：{0}", path);

        // 如果有旧的.git/index.lock锁定文件，删除之
        var file = path.CombinePath(".git/index.lock");
        if (File.Exists(file)) File.Delete(file);

        var gr = new GitRepo { Name = repo.Name, Path = path, Tracer = tracer };
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
            project.UpdateWorkflow(repo, path);
            if (repo.UpdateMode > 0) nuget.Update(repo, gr, path, set);

            gr.PushAll(null);
        }
        else
        {
            // 记住当前分支，最后要切回来
            var currentBranch = gr.CurrentBranch ?? branchs[0];
            //// 当前分支必须在第一位，避免有些修改被切到其它分支上
            //if (!currentBranch.IsNullOrEmpty() && branchs.Length > 0 && currentBranch != branchs[0])
            //{
            //    var bs = branchs.ToList();
            //    bs.Remove(currentBranch);
            //    bs.Insert(0, currentBranch);
            //}
            // 只同步一个分支
            branchs = [currentBranch];
            foreach (var item in branchs)
            {
                using var span2 = tracer?.NewSpan($"ProcessBranch-{item}", repo);
                WriteLog("分支：{0}", item);

                // 切换分支
                gr.Checkout(item);
                gr.PullAll(item);

                if (item == currentBranch)
                {
                    project.UpdateReadme(repo, gr, path, set);
                    project.UpdateVersion(repo, gr, path, set);
                    project.UpdateWorkflow(repo, path);
                    if (repo.UpdateMode > 0) nuget.Update(repo, gr, path, set);

                    // 如果本地有未提交文件，则直接提交
                    var changes = gr.GetChanges();
                    if (changes.Count > 0)
                    {
                        WriteLog("分支 {0} 有未提交文件，直接提交", item);
                        gr.CommitChanges($"[{repo.Name}] {item} 自动提交");
                    }
                }

                gr.PushAll(item);

                {
                    // 如果本地有未提交文件，则跳过处理
                    var changes = gr.GetChanges();
                    if (changes.Count > 0) break;
                }
            }

            gr.Checkout(currentBranch);
        }

        return true;
    }

    public void AddAll(String basePath, SyncSetting set)
    {
        using var span = tracer?.NewSpan("AddAll", basePath);

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
        if (_eventProvider != null && !format.IsNullOrEmpty())
        {
            if (format.Contains("错误") || format.Contains("异常"))
                _eventProvider.WriteErrorEvent(GetType().Name, String.Format(format, args));
            else
                _eventProvider.WriteInfoEvent(GetType().Name, String.Format(format, args));
        }
    }
}

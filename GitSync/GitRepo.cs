namespace GitSync;

/// <summary>Git库操作类</summary>
public class GitRepo
{
    #region 属性
    /// <summary>仓库名称</summary>
    public String Name { get; set; }

    /// <summary>本地仓库路径</summary>
    public String Path { get; set; }

    /// <summary>本地所有分支列表</summary>
    public String[] Branchs { get; set; }

    /// <summary>配置的远程库名称列表</summary>
    public String[] Remotes { get; set; }

    /// <summary>当前所在分支</summary>
    public String CurrentBranch { get; set; }

    /// <summary>链路追踪器</summary>
    public ITracer Tracer { get; set; }
    #endregion

    #region 方法
    /// <summary>获取本地所有分支，并更新 <see cref="Branchs"/> 和 <see cref="CurrentBranch"/></summary>
    /// <returns>分支名称数组</returns>
    public String[] GetBranchs()
    {
        // 执行git branch命令，获得本地所有分支
        var rs = "git".Execute("branch", Path);
        if (rs.IsNullOrEmpty()) return [];

        var ss = rs.Split("\n", StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToArray();
        var list = new List<String>();
        foreach (var item in ss)
        {
            if (item.StartsWith('*'))
            {
                CurrentBranch = item[1..].Trim();
                list.Add(item[1..].Trim());
            }
            else
            {
                list.Add(item);
            }
        }

        return Branchs = list.Distinct().ToArray();
    }

    /// <summary>获取本地所有远程库名称，并更新 <see cref="Remotes"/></summary>
    /// <returns>远程库名称数组</returns>
    public String[] GetRemotes()
    {
        // 执行git branch -r命令，获得所有远程追踪分支
        var rs = "git".Execute("branch -r", Path);
        if (rs.IsNullOrEmpty()) return [];

        var ss = rs.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        var list = new List<String>();
        foreach (var item in ss)
        {
            var p = item.IndexOf('/');
            if (p > 0) list.Add(item[..p].Trim());
        }

        return Remotes = list.Distinct().ToArray();
    }

    /// <summary>切换到指定分支</summary>
    /// <param name="branch">目标分支名</param>
    public void Checkout(String branch)
    {
        using var span = Tracer?.NewSpan(nameof(Checkout), branch);

        // 切换分支
        "git".ShellExecute($"checkout {branch}", Path, 60_000);
    }

    /// <summary>从指定远程库拉取指定分支</summary>
    /// <param name="remote">远程库名称</param>
    /// <param name="branch">分支名称，为空时由 git 决定</param>
    public void Pull(String remote, String branch)
    {
        using var span = Tracer?.NewSpan(nameof(Pull), new { remote, branch });

        // 拉取远程库
        "git".ShellExecute($"pull -v {remote} {branch}", Path, 60_000);
    }

    /// <summary>从所有远程库拉取指定分支</summary>
    /// <param name="branch">分支名称，为空时由 git 决定</param>
    public void PullAll(String branch)
    {
        // 拉取远程库
        //"git".ShellExecute($"pull -v --all", Path);

        var rs = Remotes ?? GetRemotes();
        foreach (var remote in rs)
        {
            Pull(remote, branch);
        }
    }

    /// <summary>将指定分支推送到指定远程库</summary>
    /// <param name="remote">远程库名称</param>
    /// <param name="branch">分支名称，为空时由 git 决定</param>
    public void Push(String remote, String branch)
    {
        using var span = Tracer?.NewSpan(nameof(Push), new { remote, branch });

        // 推送远程库
        "git".ShellExecute($"push -v {remote} {branch}", Path, 60_000);
    }

    /// <summary>将指定分支推送到所有远程库</summary>
    /// <param name="branch">分支名称，为空时由 git 决定</param>
    public void PushAll(String branch)
    {
        // 推送远程库
        //"git".ShellExecute($"push -v --all", Path);

        var rs = Remotes ?? GetRemotes();
        foreach (var remote in rs)
        {
            Push(remote, branch);
        }
    }

    /// <summary>获取工作区变动文件列表（git status -s）</summary>
    /// <returns>Key 为文件路径，Value 为变更类型（M/A/D/??等）</returns>
    public IDictionary<String, String> GetChanges()
    {
        var dic = new Dictionary<String, String>();

        var rs = "git".Execute("status -s", Path);
        if (rs.IsNullOrEmpty()) return dic;

        var ss = rs.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in ss)
        {
            var line = item.Trim();
            if (line.IsNullOrEmpty()) continue;

            var p = line.IndexOf(' ');
            if (p > 0)
            {
                var act = line[..p].Trim();
                var file = line[(p + 1)..].Trim('\"');
                dic[file] = act;
            }
        }

        return dic;
    }

    /// <summary>提交所有已变更文件</summary>
    /// <param name="comment">提交说明</param>
    public void CommitChanges(String comment)
    {
        "git".Run($"commit -a -m \"{comment}\"", Path);
    }
    #endregion

    #region 辅助
    #endregion
}

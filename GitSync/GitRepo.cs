using System.Diagnostics;
using System.IO;
using System.Threading;
using GitSync.Models;

namespace GitSync;

/// <summary>Git库操作类</summary>
public class GitRepo
{
    #region 属性
    public String Name { get; set; }

    public String Path { get; set; }

    public String[] Branchs { get; set; }

    public String[] Remotes { get; set; }

    public String CurrentBranch { get; set; }

    public ITracer Tracer { get; set; }
    #endregion

    #region 方法
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

    public String[] GetRemotes()
    {
        // 执行git branch命令，获得本地所有分支
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

    public void Checkout(String branch)
    {
        using var span = Tracer?.NewSpan(nameof(Checkout), branch);

        // 切换分支
        "git".ShellExecute($"checkout {branch}", Path, 60_000);
    }

    public void Pull(String remote, String branch)
    {
        using var span = Tracer?.NewSpan(nameof(Pull), new { remote, branch });

        // 拉取远程库
        "git".ShellExecute($"pull -v {remote} {branch}", Path, 60_000);
    }

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

    public void Push(String remote, String branch)
    {
        using var span = Tracer?.NewSpan(nameof(Push), new { remote, branch });

        // 推送远程库
        "git".ShellExecute($"push -v {remote} {branch}", Path, 60_000);
    }

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

    public void CommitChanges(String comment)
    {
        "git".Run($"commit -a -m \"{comment}\"", Path);
    }
    #endregion

    #region 辅助
    #endregion
}

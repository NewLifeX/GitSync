using System.Diagnostics;

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
        var rs = Execute("git", "branch", Path);
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
        var rs = Execute("git", "branch -r", Path);
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
        // 切换分支
        ShellExecute("git", $"checkout {branch}", Path);
    }

    public void Pull(String remote, String branch)
    {
        // 拉取远程库
        ShellExecute("git", $"pull -v {remote} {branch}", Path);
    }

    public void PullAll(String branch)
    {
        // 拉取远程库
        //ShellExecute("git", $"pull -v --all", Path);

        var rs = Remotes ?? GetRemotes();
        foreach (var remote in rs)
        {
            Pull(remote, branch);
        }
    }

    public void Push(String remote, String branch)
    {
        // 推送远程库
        ShellExecute("git", $"push -v {remote} {branch}", Path);
    }

    public void PushAll(String branch)
    {
        // 推送远程库
        //ShellExecute("git", $"push -v --all", Path);

        var rs = Remotes ?? GetRemotes();
        foreach (var remote in rs)
        {
            Push(remote, branch);
        }
    }
    #endregion

    #region 辅助
    private String? Execute(String cmd, String? arguments = null, String? worker = null)
    {
        using var span = Tracer?.NewSpan("Execute", $"{cmd} {arguments} worker={worker}");
        try
        {
            XTrace.WriteLine("{0} {1}", cmd, arguments);

            var psi = new ProcessStartInfo(cmd, arguments ?? String.Empty)
            {
                // UseShellExecute 必须 false，以便于后续重定向输出流
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                //RedirectStandardError = true,
                WorkingDirectory = worker,
            };
            var process = Process.Start(psi);
            if (process == null) return null;

            if (!process.WaitForExit(3_000))
            {
                process.Kill();
                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            return null;
        }
    }

    private Int32 ShellExecute(String cmd, String? arguments = null, String? worker = null)
    {
        using var span = Tracer?.NewSpan("ShellExecute", $"{cmd} {arguments} worker={worker}");
        try
        {
            XTrace.WriteLine("{0} {1}", cmd, arguments);

            var psi = new ProcessStartInfo(cmd, arguments ?? String.Empty)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                //WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = worker,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => XTrace.WriteLine(e.Data);
            process.ErrorDataReceived += (s, e) => XTrace.WriteLine(e.Data);
            process.Start();
            //if (process == null) return -1;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                return process.ExitCode;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            XTrace.Log.Error(ex.Message);
            return -2;
        }
    }
    #endregion
}

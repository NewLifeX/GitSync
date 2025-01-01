using System.Text;
using GitSync;
using GitSync.Models;
using NewLife.Remoting.Clients;

namespace GitSync.Services;

internal class NugetService
{
    private static Boolean _check;
    private readonly IEventProvider _eventProvider;
    private readonly ITracer _tracer;

    public NugetService(IEventProvider eventProvider, ITracer tracer)
    {
        _eventProvider = eventProvider;
        _tracer = tracer;
    }

    public void Update(Repo repo, GitRepo gr, String path, SyncSetting set)
    {
        using var span = _tracer?.NewSpan("NugetUpgrade", path);

        if (!_check)
        {
            CheckTool();
            _check = true;
        }

        // 如果有多个解决方案，选择最大的那个
        var sln = "";
        var slns = ".".AsDirectory().GetFiles("*.sln").OrderByDescending(e => e.Length).ToList();
        if (slns.Count > 1) sln = slns[0].Name;

        // 更新Nuget包
        var timeout = 30_000;
        //"dotnet-outdated".Run("-u", 30_000, null, null, path);
        switch (repo.UpdateMode)
        {
            case UpdateModes.None:
                return;
            case UpdateModes.Exclude:
                // 需要排除指定项，因此需要先查询所有可升级包
                var pkgs = new List<String>();
                var excludes = (set.Excludes + "").Split(",", StringSplitOptions.RemoveEmptyEntries);
                if (excludes != null && excludes.Length > 0)
                {
                    var result = "dotnet-outdated".Run($"-pre Never {sln}", path, timeout);
                    var lines = (result + "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in lines)
                    {
                        if (!item.Contains("->")) continue;

                        var line = item.Trim();
                        var p = line.IndexOf(' ');
                        if (p < 0) continue;

                        var name = line[..p].Trim();
                        if (!name.IsNullOrEmpty() && excludes.Any(e => e.IsMatch(name, StringComparison.OrdinalIgnoreCase)))
                            pkgs.Add(name);
                    }
                }
                if (pkgs.Count > 0)
                    "dotnet-outdated".Run($"-u -pre Never {sln} " + pkgs.Join(" ", e => $"-exc {e}"), path, timeout);
                else
                    "dotnet-outdated".Run($"-u -pre Never {sln}", path, timeout);
                break;
            case UpdateModes.Default:
                "dotnet-outdated".Run($"-u -pre Never {sln}", path, timeout);
                break;
            case UpdateModes.Full:
                "dotnet-outdated".Run($"-u {sln}", path, timeout);
                break;
            default:
                break;
        }

        //// 是否有文件更新
        //var changes = gr.GetChanges();
        //if (changes.Count == 0) return;

        // 编译
        var exitCode = "dotnet".Run("build", path, timeout);

        // Git提交。编译成功才提交，否则回滚
        timeout = 15_000;
        if (exitCode == 0)
        {
            WriteLog("{0}编译成功，提交", repo.Name);
            "git".Run("commit -a -m \"Upgrade Nuget\"", path, timeout);
        }
        else
        {
            WriteLog("{0}编译失败（ExitCode={1}），回滚", repo.Name, exitCode);
            "git".Run("reset --hard", path, timeout);
        }
    }

    void CheckTool()
    {
        var rs = "dotnet".Execute("tool list -g", 30_000, true, Encoding.UTF8);

        var ss = rs?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var line = ss?.FirstOrDefault(e => e.StartsWith("dotnet-outdated-tool"));
        if (line.IsNullOrEmpty())
        {
            rs = "dotnet".Execute("tool install dotnet-outdated-tool -g", 30_000, true, Encoding.UTF8);
            WriteLog(rs);
        }
    }

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        _eventProvider?.WriteInfoEvent("Worker", String.Format(format, args));
    }
}

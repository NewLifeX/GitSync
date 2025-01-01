using System.Text.RegularExpressions;
using GitSync.Models;
using NewLife.Remoting.Clients;

namespace GitSync.Services;

/// <summary>更新项目</summary>
internal class ProjectService
{
    private readonly IEventProvider _eventProvider;
    private readonly ITracer _tracer;

    public ProjectService(IEventProvider eventProvider, ITracer tracer)
    {
        _eventProvider = eventProvider;
        _tracer = tracer;
    }

    public void UpdateReadme(Repo repo, GitRepo gr, String path, SyncSetting set)
    {
        var prjs = path.AsDirectory().GetFiles("Readme.md", SearchOption.AllDirectories);
        if (prjs.Length == 0) return;

        using var span = _tracer?.NewSpan(nameof(UpdateReadme), path, prjs.Length);

    }

    public void UpdateVersion(Repo repo, GitRepo gr, String path, SyncSetting set)
    {
        var prjs = path.AsDirectory().GetFiles("*.csproj", SearchOption.AllDirectories);
        if (prjs.Length == 0) return;

        using var span = _tracer?.NewSpan(nameof(UpdateVersion), path, prjs.Length);

        // 读取每一个项目文件，识别其中版权信息，然后更新文件
        foreach (var item in prjs)
        {
            var txt = File.ReadAllText(item.FullName);
            if (txt.IsNullOrEmpty()) continue;

            var p1 = txt.IndexOf("<Copyright>");
            if (p1 < 0) continue;
            p1 += "<Copyright>".Length;
            var p2 = txt.IndexOf("</Copyright>", p1);
            if (p2 < 0) continue;

            var copyRight = txt.Substring(p1, p2 - p1);

            // 把版权中的年份取出来，分为有没有横杠两种情况，使用正则表达式匹配最后一次出现的年份
            var reg = new Regex(@"20\d{2}");
            var match = reg.Matches(copyRight).LastOrDefault();
            if (match == null) continue;

            var copyRight2 = copyRight.Replace(match.Value, DateTime.Now.Year + "");

            WriteLog("[{0}] {1} => {2}", item.Name, copyRight, copyRight2);

            txt = txt.Replace(copyRight, copyRight2);
            File.WriteAllText(item.FullName, txt);
        }
    }

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        _eventProvider?.WriteInfoEvent("Worker", String.Format(format, args));
    }
}

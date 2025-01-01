using System.Text;
using System.Text.RegularExpressions;
using GitSync.Models;
using NewLife.Remoting.Clients;
using static System.Net.Mime.MediaTypeNames;

namespace GitSync.Services;

/// <summary>更新项目</summary>
internal class ProjectService
{
    private readonly IEventProvider _eventProvider;
    private readonly ITracer _tracer;
    private String _projects;
    private String _teamInfo;

    public ProjectService(IEventProvider eventProvider, ITracer tracer)
    {
        _eventProvider = eventProvider;
        _tracer = tracer;
    }

    public void UpdateReadme(Repo repo, GitRepo gr, String path, SyncSetting set)
    {
        var mds = path.AsDirectory().GetFiles("Readme.md", SearchOption.AllDirectories);
        if (mds.Length == 0) return;

        // 以NewLife.Core作为模板，更新所有Readme.md
        if (repo.Name == "NewLife.Core")
        {
            var ts = ParseSegments(File.ReadAllText(mds[0].FullName));
            _projects = ts[0]?.Text;
            _teamInfo = ts[1]?.Text;
        }

        if (_projects.IsNullOrEmpty() && _teamInfo.IsNullOrEmpty()) return;

        using var span = _tracer?.NewSpan(nameof(UpdateReadme), path, mds.Length);
        foreach (var item in mds)
        {
            var txt = File.ReadAllText(item.FullName);
            if (txt.IsNullOrEmpty()) continue;

            var txt2 = txt;
            var ts = ParseSegments(txt);
            if (ts[0] != null && !_projects.IsNullOrEmpty())
            {
                txt2 = txt2.Replace(ts[0].Text, _projects);
            }
            if (ts[1] != null && !_teamInfo.IsNullOrEmpty())
            {
                txt2 = txt2.Replace(ts[1].Text, _teamInfo);
            }

            if (txt != txt2)
            {
                WriteLog("[{0}] 更新", item.Name);
                File.WriteAllText(item.FullName, txt2);
            }
        }
    }

    TextSegment[] ParseSegments(String txt)
    {
        var list = new TextSegment[2];

        //var md = path.AsDirectory().GetFiles("Readme.md").FirstOrDefault();
        //if (md == null) return list;

        //var txt = File.ReadAllText(md.FullName);
        //if (txt.IsNullOrEmpty()) return list;

        {
            var p1 = txt.IndexOf("## 新生命项目矩阵");
            if (p1 >= 0)
            {
                p1 += "## 新生命项目矩阵".Length;
                var p2 = txt.IndexOf("##", p1);
                if (p2 >= 0)
                {
                    list[0] = new TextSegment { Text = txt[p1..p2], Start = p1, End = p2 };
                }
            }
        }
        {
            var p1 = txt.IndexOf("## 新生命开发团队");
            if (p1 >= 0)
            {
                p1 += "## 新生命开发团队".Length;
                var p2 = txt.IndexOf("![智能大石头]", p1);
                if (p2 >= 0)
                {
                    list[1] = new TextSegment { Text = txt[p1..p2], Start = p1, End = p2 };
                }
            }
        }

        return list;
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

            var copyRight = txt[p1..p2];

            // 把版权中的年份取出来，分为有没有横杠两种情况，使用正则表达式匹配最后一次出现的年份
            var reg = new Regex(@"20\d{2}");
            var match = reg.Matches(copyRight).LastOrDefault();
            if (match == null) continue;

            var copyRight2 = copyRight.Replace(match.Value, DateTime.Now.Year + "");
            if (copyRight == copyRight2) continue;

            WriteLog("[{0}] {1} => {2}", item.Name, copyRight, copyRight2);

            txt = txt.Replace(copyRight, copyRight2);
            File.WriteAllText(item.FullName, txt, Encoding.UTF8);
        }
    }

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        _eventProvider?.WriteInfoEvent("Worker", String.Format(format, args));
    }
}

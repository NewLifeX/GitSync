using System.Text;
using System.Text.RegularExpressions;
using GitSync.Models;
using NewLife.Remoting.Clients;

namespace GitSync.Services;

/// <summary>更新项目</summary>
public class ProjectService(IEventProvider eventProvider, ITracer tracer)
{
    private String _projects;
    private String _teamInfo;
    private String _dotnetActions;
    private String _copilotInstructions;

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

        using var span = tracer?.NewSpan(nameof(UpdateReadme), path, mds.Length);
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

        using var span = tracer?.NewSpan(nameof(UpdateVersion), path, prjs.Length);

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

    public void UpdateWorkflow(Repo repo, String path)
    {
        var files = path.AsDirectory().GetFiles("*.yml", SearchOption.AllDirectories);
        if (files.Length == 0) return;

        // 以NewLife.Core作为模板
        if (repo.Name == "NewLife.Core")
        {
            var publish = files.FirstOrDefault(e => e.Name == "publish.yml");
            if (publish != null)
            {
                var ts = ParseYml(File.ReadAllText(publish.FullName), 20);
                if (ts != null && !ts.Text.IsNullOrEmpty() && ts.Text.Contains("actions/setup-dotnet"))
                    _dotnetActions = ts?.Text;
            }
        }

        if (_dotnetActions.IsNullOrEmpty()) return;

        using var span = tracer?.NewSpan(nameof(UpdateWorkflow), path, files.Length);

        // 读取每一个项目文件，识别其中版权信息，然后更新文件
        foreach (var item in files)
        {
            var txt = File.ReadAllText(item.FullName);
            if (txt.IsNullOrEmpty()) continue;
            if (!txt.Contains("actions/setup-dotnet")) continue;

            var txt2 = txt;
            var ts = ParseYml(txt, 20);
            if (ts != null)
            {
                txt2 = txt2.Replace(ts.Text, _dotnetActions);
            }

            if (txt != txt2)
            {
                WriteLog("[{0}] 更新", item.Name);
                File.WriteAllText(item.FullName, txt2);
            }
        }
    }

    TextSegment ParseYml(String txt, Int32 minLength)
    {
        //var pStart = 0;
        //var pEnd = 0;
        //var tab = 0;
        //var lines = txt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        //for (var i = 0; i < lines.Length; i++)
        //{
        //    // 找到包含actions/setup-dotnet的行，并且下一行包含with，那么这一行作为pStart
        //    if (lines[i].Contains("actions/setup-dotnet") && i + 1 < lines.Length && lines[i + 1].Trim().StartsWith("with:"))
        //    {
        //        pStart = i;
        //        tab = lines[i + 1].IndexOf("with:");
        //    }
        //    else if (pStart > 0)
        //    {
        //        // 找到下一行不以tab开始的行，那么这一行作为pEnd
        //        if (lines[i].Length < tab || !lines[i].StartsWith(new String(' ', tab)))
        //        {
        //            pEnd = i;
        //            break;
        //        }
        //    }
        //}

        var p1 = txt.IndexOf("actions/checkout");
        if (p1 >= 0)
        {
            p1 += "actions/checkout".Length;
            var p2 = txt.IndexOf("- name:", p1 + minLength);
            if (p2 >= 0)
            {
                return new TextSegment { Text = txt[p1..p2], Start = p1, End = p2 };
            }
        }

        return null;
    }

    private void WriteLog(String format, params Object[] args)
    {
        if (format.IsNullOrEmpty()) return;

        XTrace.WriteLine(format, args);
        if (eventProvider != null && !format.IsNullOrEmpty())
        {
            if (format.Contains("错误") || format.Contains("异常"))
                eventProvider.WriteErrorEvent(GetType().Name, String.Format(format, args));
            else
                eventProvider.WriteInfoEvent(GetType().Name, String.Format(format, args));
        }
    }

    public void UpdateCopilotInstructions(Repo repo, String path)
    {
        var dir = ".github".GetFullPath();
        if (!Directory.Exists(dir)) return;

        var target = dir.CombinePath("copilot-instructions.md").GetFullPath();

        // 以NewLife.Core作为模板
        if (repo.Name == "NewLife.Core")
        {
            if (File.Exists(target))
                _copilotInstructions = target;

            return;
        }

        if (_copilotInstructions.IsNullOrEmpty()) return;

        using var span = tracer?.NewSpan(nameof(UpdateCopilotInstructions), path);

        File.Copy(_copilotInstructions, target, true);

        WriteLog("[{0}] 更新 copilot-instructions.md", repo.Name);
    }
}

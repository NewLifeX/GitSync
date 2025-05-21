using System.ComponentModel;
using System.Xml.Serialization;

namespace GitSync.Models;

public class Repo
{
    /// <summary>名字</summary>
    [XmlAttribute]
    public String Name { get; set; }

    /// <summary>启用</summary>
    [XmlAttribute]
    public Boolean Enable { get; set; }

    /// <summary>路径。为空时取基础路径</summary>
    [XmlAttribute]
    public String Path { get; set; }

    /// <summary>要同步的分支。为空时同步当前分支，*同步所有分支，多个分支逗号隔开</summary>
    [XmlAttribute]
    public String Branchs { get; set; }

    /// <summary>要同步的远程库。为空时同步所有库，多个库逗号隔开</summary>
    [XmlAttribute]
    public String Remotes { get; set; }

    /// <summary>拉取远程</summary>
    [XmlAttribute] 
    public String PullRemote { get; set; }

    /// <summary>推送远程列表</summary>
    [XmlAttribute]
    public String PushRemotes { get; set; }

    ///// <summary>更新Nuget包</summary>
    //[XmlAttribute]
    //public Boolean Upgrade { get; set; }

    /// <summary>更新Nuget包</summary>
    [XmlAttribute]
    public UpdateModes UpdateMode { get; set; }
}

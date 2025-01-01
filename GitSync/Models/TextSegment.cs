namespace GitSync.Models;

/// <summary>文本片段</summary>
internal class TextSegment
{
    /// <summary>文本内容</summary>
    public String Text { get; set; }

    /// <summary>开始位置</summary>
    public Int32 Start { get; set; }

    /// <summary>结束位置</summary>
    public Int32 End { get; set; }
}

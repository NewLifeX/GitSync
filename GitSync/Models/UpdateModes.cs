namespace GitSync.Models;

public enum UpdateModes
{
    /// <summary>不升级</summary>
    None = 0,

    /// <summary>默认升级，排除指定项，不含预览版</summary>
    Default = 1,

    /// <summary>升级所有包，不排除任何项，不含预览版</summary>
    NoExclude = 2,

    /// <summary>全面升级，包括预览版</summary>
    Full = 4,
}

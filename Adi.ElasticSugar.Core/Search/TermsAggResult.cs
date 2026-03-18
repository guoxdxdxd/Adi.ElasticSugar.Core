namespace Adi.ElasticSugar.Core.Search;

/// <summary>
/// Terms 聚合桶结果
/// </summary>
public class TermsAggResult
{
    /// <summary>
    /// 桶 Key（字符串表示）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 文档数量
    /// </summary>
    public long Count { get; set; }
}

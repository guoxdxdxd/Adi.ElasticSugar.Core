namespace Adi.ElasticSugar.Core.Models;

/// <summary>
/// ElasticSearch 文档基类
/// 所有需要存储到 ElasticSearch 的文档类型都应该继承此基类
/// </summary>
public abstract class BaseEsModel
{
    /// <summary>
    /// 文档 ID
    /// </summary>
    public object? Id { get; set; }

    /// <summary>
    /// ElasticSearch 时间字段
    /// 用于索引名称自动生成（基于年月）
    /// 例如：如果 esDateTime 为 2024-01-15，索引名称可能为 "orders-2024-01"
    /// </summary>
    public DateTime EsDateTime { get; set; }
}


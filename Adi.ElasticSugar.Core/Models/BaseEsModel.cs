using System.Reflection;
using Adi.ElasticSugar.Core.Index;

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

    /// <summary>
    /// 根据派生类的特性（EsIndexAttribute）和当前实例的 EsDateTime 生成索引名称
    /// 自动从派生类的 EsIndexAttribute 特性中读取配置，结合 EsDateTime 字段生成索引名称
    /// </summary>
    /// <returns>索引名称</returns>
    public string GetIndexName()
    {
        return IndexNameGenerator.GenerateIndexNameFromAttribute(this);
    }

    /// <summary>
    /// 直接从派生类的特性（EsIndexAttribute）获取索引名称
    /// 此方法不依赖其他类，直接读取特性配置并生成索引名称
    /// 根据 EsIndexAttribute 中的 Format 和 IndexPrefix 配置，结合当前实例的 EsDateTime 字段生成索引名称
    /// </summary>
    /// <returns>索引名称</returns>
    public string GetIndexNameFromAttribute()
    {
        // 获取当前实例的类型
        var type = GetType();
        
        // 从类型特性中获取索引配置
        var attribute = type.GetCustomAttribute<EsIndexAttribute>();
        
        // 获取索引前缀：优先从特性中读取，如果特性中未设置则使用类名的小写形式
        string indexPrefix;
        if (attribute != null && !string.IsNullOrWhiteSpace(attribute.IndexPrefix))
        {
            indexPrefix = attribute.IndexPrefix;
        }
        else
        {
            indexPrefix = type.Name.ToLowerInvariant();
        }
        
        // 获取索引格式：从特性中读取，默认为年月格式
        var format = attribute?.Format ?? IndexFormat.YearMonth;
        
        // 根据格式和时间字段生成索引名称
        string indexName;
        switch (format)
        {
            case IndexFormat.Year:
                // 年格式：{prefix}-{yyyy}
                // 例如：orders-2024
                var year = EsDateTime.ToString("yyyy");
                indexName = $"{indexPrefix}-{year}";
                break;
                
            case IndexFormat.YearMonth:
            default:
                // 年月格式：{prefix}-{yyyy-MM}
                // 例如：orders-2024-01
                var yearMonth = EsDateTime.ToString("yyyy-MM");
                indexName = $"{indexPrefix}-{yearMonth}";
                break;
        }
        
        return indexName;
    }
}


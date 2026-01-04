using Adi.ElasticSugar.Core.Models;

namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// 索引名称生成器
/// 根据文档类型和时间字段自动生成索引名称
/// </summary>
public static class IndexNameGenerator
{
    /// <summary>
    /// 生成索引名称（基于年月）
    /// 格式：{indexPrefix}-{yyyy-MM}
    /// 例如：orders-2024-01
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="indexPrefix">索引前缀（例如："orders"）</param>
    /// <param name="dateTime">时间字段（用于生成年月部分）</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexName<T>(string indexPrefix, DateTime dateTime)
    {
        if (string.IsNullOrWhiteSpace(indexPrefix))
        {
            throw new ArgumentException("索引前缀不能为空", nameof(indexPrefix));
        }

        var yearMonth = dateTime.ToString("yyyy-MM");
        return $"{indexPrefix}-{yearMonth}";
    }

    /// <summary>
    /// 生成索引名称（基于年月）
    /// 从文档实例中提取时间字段
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="indexPrefix">索引前缀</param>
    /// <param name="document">文档实例</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexName<T>(string indexPrefix, T document) where T : BaseEsModel
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return GenerateIndexName<T>(indexPrefix, document.EsDateTime);
    }

    /// <summary>
    /// 生成索引名称（基于当前年月）
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="indexPrefix">索引前缀</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexName<T>(string indexPrefix)
    {
        return GenerateIndexName<T>(indexPrefix, DateTime.Now);
    }

    /// <summary>
    /// 生成索引通配符模式（用于查询多个索引）
    /// 格式：{indexPrefix}-*
    /// 例如：orders-*
    /// </summary>
    /// <param name="indexPrefix">索引前缀</param>
    /// <returns>索引通配符模式</returns>
    public static string GenerateIndexPattern(string indexPrefix)
    {
        if (string.IsNullOrWhiteSpace(indexPrefix))
        {
            throw new ArgumentException("索引前缀不能为空", nameof(indexPrefix));
        }

        return $"{indexPrefix}-*";
    }
}


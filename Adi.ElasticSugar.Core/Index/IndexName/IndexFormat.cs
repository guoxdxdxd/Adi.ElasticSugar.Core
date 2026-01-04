namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// 索引名称格式枚举
/// 用于定义索引名称的生成格式
/// </summary>
public enum IndexFormat
{
    /// <summary>
    /// 基于年月的格式
    /// 格式：{prefix}-{yyyy-MM}
    /// 例如：orders-2024-01
    /// </summary>
    YearMonth = 0,

    /// <summary>
    /// 基于年的格式
    /// 格式：{prefix}-{yyyy}
    /// 例如：orders-2024
    /// </summary>
    Year = 1
}


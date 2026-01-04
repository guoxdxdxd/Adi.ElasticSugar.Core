using Adi.ElasticSugar.Core.Models;

namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// 年格式索引名称生成器
/// 格式：{prefix}-{yyyy}
/// 例如：orders-2024
/// </summary>
/// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
public class YearIndexNameGenerator<T> : IIndexNameGenerator<T> where T : BaseEsModel
{
    private readonly string _prefix;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="prefix">索引前缀</param>
    public YearIndexNameGenerator(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("索引前缀不能为空", nameof(prefix));
        }

        _prefix = prefix;
    }

    /// <summary>
    /// 根据文档实例生成索引名称
    /// </summary>
    /// <param name="document">文档实例</param>
    /// <returns>索引名称</returns>
    public string GenerateIndexName(T document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return GenerateIndexName(document.EsDateTime);
    }

    /// <summary>
    /// 根据时间字段生成索引名称
    /// </summary>
    /// <param name="dateTime">时间字段</param>
    /// <returns>索引名称</returns>
    public string GenerateIndexName(DateTime dateTime)
    {
        var year = dateTime.ToString("yyyy");
        return $"{_prefix}-{year}";
    }

    /// <summary>
    /// 生成索引通配符模式（用于查询多个索引）
    /// </summary>
    /// <returns>索引通配符模式，例如：orders-*</returns>
    public string GenerateIndexPattern()
    {
        return $"{_prefix}-*";
    }
}


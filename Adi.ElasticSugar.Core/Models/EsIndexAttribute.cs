using Adi.ElasticSugar.Core.Index;

namespace Adi.ElasticSugar.Core.Models;

/// <summary>
/// ElasticSearch 索引配置特性
/// 用于标记文档类型的索引配置，包括索引前缀和索引格式
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class EsIndexAttribute : Attribute
{
    /// <summary>
    /// 索引前缀
    /// 如果未设置，则使用类名的小写形式作为前缀
    /// </summary>
    public string? IndexPrefix { get; set; }

    /// <summary>
    /// 索引格式
    /// 用于定义索引名称的生成方式：年或年月
    /// 默认值：YearMonth（年月格式）
    /// </summary>
    public IndexFormat Format { get; set; } = IndexFormat.YearMonth;

    /// <summary>
    /// 自定义索引名称生成器类型
    /// 如果指定了此类型，将优先使用自定义生成器来生成索引名称
    /// 该类型必须实现 IIndexNameGenerator&lt;T&gt; 接口
    /// 例如：typeof(CustomOrderIndexNameGenerator)
    /// </summary>
    public Type? CustomGeneratorType { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="indexPrefix">索引前缀</param>
    /// <param name="format">索引格式，默认为年月格式</param>
    public EsIndexAttribute(string? indexPrefix = null, IndexFormat format = IndexFormat.YearMonth)
    {
        IndexPrefix = indexPrefix;
        Format = format;
    }

    /// <summary>
    /// 构造函数（支持指定自定义生成器类型）
    /// </summary>
    /// <param name="customGeneratorType">自定义索引名称生成器类型，必须实现 IIndexNameGenerator&lt;T&gt; 接口</param>
    public EsIndexAttribute(Type customGeneratorType)
    {
        CustomGeneratorType = customGeneratorType ?? throw new ArgumentNullException(nameof(customGeneratorType));
    }
}


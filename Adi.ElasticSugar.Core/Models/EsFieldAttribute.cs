namespace Adi.ElasticSugar.Core.Models;

/// <summary>
/// ElasticSearch 字段特性
/// 用于标记字段的映射配置，包括字段类型、是否需要 keyword 等
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class EsFieldAttribute : Attribute
{
    /// <summary>
    /// 字段类型
    /// 如果为 null，则根据属性类型自动推断
    /// 可选值：text, keyword, long, integer, short, byte, double, float, date, boolean, object, nested 等
    /// </summary>
    public string? FieldType { get; set; }

    /// <summary>
    /// 是否为嵌套文档
    /// 如果为 true，该字段会被设置为 nested 类型
    /// 如果为 null，则根据属性类型自动判断（引用类型且不是 string 的会被识别为 nested）
    /// </summary>
    public bool? IsNested { get; set; }

    /// <summary>
    /// 是否需要 keyword 子字段
    /// 对于 text 类型字段，如果设置为 true，会自动创建 .keyword 子字段用于精确匹配和排序
    /// 默认值：对于 string 类型字段，如果 FieldType 为 text 或未指定，则默认为 true
    /// </summary>
    public bool NeedKeyword { get; set; } = true;

    /// <summary>
    /// 是否忽略该字段
    /// 如果为 true，该字段不会被索引
    /// </summary>
    public bool Ignore { get; set; } = false;

    /// <summary>
    /// 字段分析器
    /// 用于 text 类型字段，指定分词器
    /// 例如：standard, ik_max_word, ik_smart 等
    /// </summary>
    public string? Analyzer { get; set; }

    /// <summary>
    /// 搜索分析器
    /// 用于 text 类型字段，指定搜索时的分词器
    /// </summary>
    public string? SearchAnalyzer { get; set; }

    /// <summary>
    /// 是否启用索引
    /// 如果为 false，字段不会被索引，但仍可以存储
    /// </summary>
    public bool Index { get; set; } = true;

    /// <summary>
    /// 是否存储字段值
    /// 如果为 false，字段值不会被存储，但仍可以索引
    /// </summary>
    public bool Store { get; set; } = false;

    /// <summary>
    /// 字段在 Elasticsearch 中的字段名称
    /// 如果指定了此值，创建索引和查询时会使用此名称而不是属性名称
    /// 例如：如果属性名为 TextField，但 FieldName 为 "textField"，则 ES 中的字段名为 "textField"
    /// 如果为 null 或空字符串，则使用属性名称（Pascal 命名）
    /// </summary>
    public string? FieldName { get; set; }
}


using Adi.ElasticSugar.Core.Models;
using Adi.ElasticSugar.Core.Index;

namespace Adi.ElasticSugar.Core.Tests.Models;

/// <summary>
/// 测试文档模型 - 包含各种 Elasticsearch 支持的数据类型
/// 用于测试索引创建、文档推送和查询功能
/// </summary>
[EsIndex(IndexPrefix = "test-documents", Format = IndexFormat.YearMonth)]
public class TestDocument : BaseEsModel
{
    // ========== 字符串类型字段 ==========
    
    /// <summary>
    /// 默认 text 类型 + keyword 子字段（用于全文搜索和精确匹配）
    /// </summary>
    public string TextField { get; set; } = string.Empty;

    /// <summary>
    /// 纯 keyword 类型（用于精确匹配和排序，不支持分词）
    /// </summary>
    [EsField(FieldType = "keyword")]
    public string KeywordField { get; set; } = string.Empty;

    /// <summary>
    /// text 类型，不需要 keyword 子字段
    /// </summary>
    [EsField(FieldType = "text", NeedKeyword = false)]
    public string TextOnlyField { get; set; } = string.Empty;

    /// <summary>
    /// 可空字符串类型
    /// </summary>
    public string? NullableStringField { get; set; }

    // ========== 整数类型字段 ==========
    
    /// <summary>
    /// int 类型（自动映射为 integer）
    /// </summary>
    public int IntField { get; set; }

    /// <summary>
    /// 可空 int 类型
    /// </summary>
    public int? NullableIntField { get; set; }

    /// <summary>
    /// long 类型（自动映射为 long）
    /// </summary>
    public long LongField { get; set; }

    /// <summary>
    /// 可空 long 类型
    /// </summary>
    public long? NullableLongField { get; set; }

    /// <summary>
    /// short 类型（自动映射为 short）
    /// </summary>
    public short ShortField { get; set; }

    /// <summary>
    /// byte 类型（自动映射为 byte）
    /// </summary>
    public byte ByteField { get; set; }

    // ========== 浮点数类型字段 ==========
    
    /// <summary>
    /// double 类型（自动映射为 double）
    /// </summary>
    public double DoubleField { get; set; }

    /// <summary>
    /// 可空 double 类型
    /// </summary>
    public double? NullableDoubleField { get; set; }

    /// <summary>
    /// float 类型（自动映射为 float）
    /// </summary>
    public float FloatField { get; set; }

    /// <summary>
    /// decimal 类型（自动映射为 double）
    /// </summary>
    public decimal DecimalField { get; set; }

    // ========== 日期时间类型字段 ==========
    
    /// <summary>
    /// DateTime 类型（自动映射为 date）
    /// </summary>
    public DateTime DateTimeField { get; set; }

    /// <summary>
    /// 可空 DateTime 类型
    /// </summary>
    public DateTime? NullableDateTimeField { get; set; }

    /// <summary>
    /// DateTimeOffset 类型（自动映射为 date）
    /// </summary>
    public DateTimeOffset DateTimeOffsetField { get; set; }

    // ========== 布尔类型字段 ==========
    
    /// <summary>
    /// bool 类型（自动映射为 boolean）
    /// </summary>
    public bool BoolField { get; set; }

    /// <summary>
    /// 可空 bool 类型
    /// </summary>
    public bool? NullableBoolField { get; set; }

    // ========== Guid 类型字段 ==========
    
    /// <summary>
    /// Guid 类型（自动映射为 keyword）
    /// </summary>
    public Guid GuidField { get; set; }

    /// <summary>
    /// 可空 Guid 类型
    /// </summary>
    public Guid? NullableGuidField { get; set; }

    // ========== 集合类型字段 ==========
    
    /// <summary>
    /// 字符串数组
    /// </summary>
    public List<string> StringListField { get; set; } = new();

    /// <summary>
    /// 整数数组
    /// </summary>
    public List<int> IntListField { get; set; } = new();

    // ========== 嵌套文档类型字段 ==========
    
    /// <summary>
    /// 嵌套文档（自动识别为 nested 类型）
    /// </summary>
    public NestedAddress Address { get; set; } = new();

    /// <summary>
    /// 嵌套文档集合（自动识别为 nested 类型）
    /// </summary>
    public List<NestedItem> Items { get; set; } = new();

    // ========== 特殊配置字段 ==========
    
    /// <summary>
    /// 自定义分析器的 text 字段
    /// </summary>
    [EsField(FieldType = "text", Analyzer = "standard", SearchAnalyzer = "standard")]
    public string AnalyzedTextField { get; set; } = string.Empty;

    /// <summary>
    /// 不索引但存储的字段
    /// </summary>
    [EsField(Index = false, Store = true)]
    public string StoredOnlyField { get; set; } = string.Empty;

    /// <summary>
    /// 被忽略的字段（不会出现在索引中）
    /// </summary>
    [EsField(Ignore = true)]
    public string IgnoredField { get; set; } = string.Empty;
}

/// <summary>
/// 嵌套地址文档（用于测试嵌套文档功能）
/// </summary>
public class NestedAddress
{
    /// <summary>
    /// 街道地址
    /// </summary>
    public string Street { get; set; } = string.Empty;

    /// <summary>
    /// 城市
    /// </summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// 邮政编码
    /// </summary>
    [EsField(FieldType = "keyword")]
    public string ZipCode { get; set; } = string.Empty;

    /// <summary>
    /// 国家
    /// </summary>
    public string Country { get; set; } = string.Empty;
}

/// <summary>
/// 嵌套项目文档（用于测试嵌套文档集合功能）
/// </summary>
public class NestedItem
{
    /// <summary>
    /// 产品名称
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 是否可用
    /// </summary>
    public bool IsAvailable { get; set; }
}


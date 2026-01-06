using Adi.ElasticSugar.Core.Models;
using Adi.ElasticSugar.Core.Index;

namespace Adi.ElasticSugar.Core.Tests.Models;

/// <summary>
/// 枚举字段类型配置测试文档
/// 用于测试枚举字段配置为不同 FieldType 时的行为
/// </summary>
[EsIndex(IndexPrefix = "test-enum-field-type", Format = IndexFormat.YearMonth)]
public class EnumFieldTypeTestDocument : BaseEsModel
{
    /// <summary>
    /// 文本字段
    /// </summary>
    public string TextField { get; set; } = string.Empty;

    /// <summary>
    /// 枚举字段 - 配置为 integer 类型（存储为数值）
    /// </summary>
    [EsField(FieldType = "integer", FieldName = "orderStatusAsInt")]
    public OrderStatus OrderStatusAsInt { get; set; }

    /// <summary>
    /// 可空枚举字段 - 配置为 integer 类型（存储为数值）
    /// </summary>
    [EsField(FieldType = "integer", FieldName = "nullableOrderStatusAsInt")]
    public OrderStatus? NullableOrderStatusAsInt { get; set; }

    /// <summary>
    /// 枚举字段 - 配置为 long 类型（存储为数值）
    /// </summary>
    [EsField(FieldType = "long", FieldName = "userRoleAsLong")]
    public UserRole UserRoleAsLong { get; set; }

    /// <summary>
    /// 枚举字段 - 配置为 keyword 类型（存储为名称，默认行为）
    /// </summary>
    [EsField(FieldType = "keyword", FieldName = "orderStatusAsKeyword")]
    public OrderStatus OrderStatusAsKeyword { get; set; }

    /// <summary>
    /// 枚举字段 - 配置为 text 类型（存储为名称，带 keyword 子字段）
    /// </summary>
    [EsField(FieldType = "text", FieldName = "orderStatusAsText")]
    public OrderStatus OrderStatusAsText { get; set; }

    /// <summary>
    /// 枚举字段 - 未配置 FieldType（默认行为，存储为名称）
    /// </summary>
    [EsField(FieldName = "orderStatusDefault")]
    public OrderStatus OrderStatusDefault { get; set; }
}


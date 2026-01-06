using System.Reflection;
using Adi.ElasticSugar.Core.Models;

namespace Adi.ElasticSugar.Core.Utils;

/// <summary>
/// 枚举字段辅助工具类
/// 提供枚举字段类型判断、值转换等公共方法
/// 用于统一处理枚举类型在索引创建、文档推送和查询时的逻辑
/// </summary>
public static class EnumFieldHelper
{
    /// <summary>
    /// 获取枚举字段在 Elasticsearch 中的字段类型
    /// 优先使用 EsFieldAttribute.FieldType 配置，如果未配置则返回默认类型
    /// </summary>
    /// <param name="property">属性信息</param>
    /// <param name="esFieldAttr">字段特性（可选，如果为 null 会自动从 property 获取）</param>
    /// <returns>Elasticsearch 字段类型（如 "integer", "long", "keyword", "text" 等）</returns>
    public static string GetEnumFieldType(PropertyInfo property, EsFieldAttribute? esFieldAttr = null)
    {
        if (property == null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        var propertyType = property.PropertyType;
        // 处理可空类型
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            propertyType = propertyType.GetGenericArguments()[0];
        }

        // 如果不是枚举类型，返回 null 或抛出异常
        if (!propertyType.IsEnum)
        {
            throw new ArgumentException($"属性 {property.Name} 不是枚举类型", nameof(property));
        }

        // 如果未提供特性，尝试从属性获取
        esFieldAttr ??= property.GetCustomAttribute<EsFieldAttribute>();

        // 优先使用配置的 FieldType
        if (!string.IsNullOrEmpty(esFieldAttr?.FieldType))
        {
            return esFieldAttr.FieldType.ToLower();
        }

        // 默认返回 "keyword"（保持向后兼容）
        return "keyword";
    }

    /// <summary>
    /// 判断枚举字段是否存储为数值类型
    /// 如果 EsFieldAttribute.FieldType 配置为数值类型（int/long/short/byte），则返回 true
    /// </summary>
    /// <param name="property">属性信息</param>
    /// <param name="esFieldAttr">字段特性（可选，如果为 null 会自动从 property 获取）</param>
    /// <returns>如果配置为数值类型返回 true，否则返回 false</returns>
    public static bool IsEnumStoredAsNumeric(PropertyInfo property, EsFieldAttribute? esFieldAttr = null)
    {
        if (property == null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        var fieldType = GetEnumFieldType(property, esFieldAttr);
        
        // 判断是否为数值类型
        return fieldType == "int" || fieldType == "integer" ||
               fieldType == "long" ||
               fieldType == "short" ||
               fieldType == "byte";
    }

    /// <summary>
    /// 获取枚举的数值
    /// 将枚举值转换为对应的数值类型（long）
    /// </summary>
    /// <param name="enumValue">枚举值</param>
    /// <param name="enumType">枚举类型（可选，如果为 null 会从 enumValue 获取）</param>
    /// <returns>枚举的数值（long 类型）</returns>
    public static long GetEnumValue(object enumValue, Type? enumType = null)
    {
        if (enumValue == null)
        {
            throw new ArgumentNullException(nameof(enumValue));
        }

        // 如果未提供类型，从值获取
        enumType ??= enumValue.GetType();
        
        // 处理可空类型
        if (enumType.IsGenericType && enumType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            enumType = enumType.GetGenericArguments()[0];
        }

        if (!enumType.IsEnum)
        {
            throw new ArgumentException($"类型 {enumType.Name} 不是枚举类型", nameof(enumType));
        }

        // 将枚举转换为数值
        return Convert.ToInt64(enumValue);
    }

    /// <summary>
    /// 获取枚举的名称
    /// 返回枚举值的名称（字符串）
    /// </summary>
    /// <param name="enumValue">枚举值</param>
    /// <returns>枚举的名称（字符串）</returns>
    public static string GetEnumName(object enumValue)
    {
        if (enumValue == null)
        {
            return string.Empty;
        }

        return enumValue.ToString() ?? string.Empty;
    }

    /// <summary>
    /// 判断字段类型是否为数值类型
    /// </summary>
    /// <param name="fieldType">字段类型字符串（如 "integer", "long", "int" 等）</param>
    /// <returns>如果是数值类型返回 true，否则返回 false</returns>
    public static bool IsNumericFieldType(string fieldType)
    {
        if (string.IsNullOrEmpty(fieldType))
        {
            return false;
        }

        var lowerFieldType = fieldType.ToLower();
        return lowerFieldType == "int" || lowerFieldType == "integer" ||
               lowerFieldType == "long" ||
               lowerFieldType == "short" ||
               lowerFieldType == "byte" ||
               lowerFieldType == "double" ||
               lowerFieldType == "float";
    }

    /// <summary>
    /// 判断字段类型是否为文本类型（text 或 keyword）
    /// </summary>
    /// <param name="fieldType">字段类型字符串</param>
    /// <returns>如果是文本类型返回 true，否则返回 false</returns>
    public static bool IsTextFieldType(string fieldType)
    {
        if (string.IsNullOrEmpty(fieldType))
        {
            return true; // 默认是 text
        }

        var lowerFieldType = fieldType.ToLower();
        return lowerFieldType == "text" || lowerFieldType == "keyword";
    }
}


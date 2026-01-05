using System.Reflection;
using Adi.ElasticSugar.Core.Models;

namespace Adi.ElasticSugar.Core.Utils;

/// <summary>
/// 字段名辅助工具类
/// 提供字段名获取和转换的通用方法
/// 用于统一处理 Elasticsearch 字段名的获取逻辑
/// </summary>
public static class FieldNameHelper
{
    /// <summary>
    /// 获取字段在 Elasticsearch 中的字段名称
    /// 如果字段配置了 EsFieldAttribute.FieldName，则使用配置的名称
    /// 否则将属性名称从 PascalCase 转换为 camelCase
    /// 因为 Elasticsearch 客户端在序列化文档时会自动将 C# 的 PascalCase 属性名转换为 camelCase
    /// </summary>
    /// <param name="property">属性信息</param>
    /// <param name="esFieldAttr">字段特性（可选，如果为 null 会自动从 property 获取）</param>
    /// <returns>字段名称</returns>
    /// <remarks>
    /// 此方法用于统一处理字段名的获取逻辑：
    /// 1. 优先使用 EsFieldAttribute.FieldName 配置的名称
    /// 2. 如果没有配置，则将属性名从 PascalCase 转换为 camelCase
    /// 
    /// 使用场景：
    /// - 索引映射构建时获取字段名
    /// - 查询表达式解析时获取字段名
    /// - 排序表达式解析时获取字段名
    /// </remarks>
    public static string GetIndexFieldName(PropertyInfo property, EsFieldAttribute? esFieldAttr = null)
    {
        if (property == null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        // 如果未提供特性，尝试从属性获取
        esFieldAttr ??= property.GetCustomAttribute<EsFieldAttribute>();

        // 如果配置了 FieldName，优先使用配置的名称
        if (!string.IsNullOrEmpty(esFieldAttr?.FieldName))
        {
            return esFieldAttr.FieldName;
        }

        // 否则将属性名称从 PascalCase 转换为 camelCase
        // 以匹配 Elasticsearch 客户端序列化时的字段命名约定
        return StringHelper.ToCamelCase(property.Name);
    }

    /// <summary>
    /// 获取成员名称在 Elasticsearch 中的字段名称
    /// 用于处理非属性的成员（如字段），直接转换为 camelCase
    /// </summary>
    /// <param name="memberName">成员名称</param>
    /// <returns>字段名称（camelCase 格式）</returns>
    public static string GetIndexFieldName(string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
        {
            throw new ArgumentException("成员名称不能为空", nameof(memberName));
        }

        // 非属性成员（如字段），将 PascalCase 转换为 camelCase
        return StringHelper.ToCamelCase(memberName);
    }
}


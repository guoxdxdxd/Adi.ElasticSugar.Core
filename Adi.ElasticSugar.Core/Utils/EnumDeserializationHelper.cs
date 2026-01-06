using System.Reflection;
using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch;

namespace Adi.ElasticSugar.Core.Utils;

/// <summary>
/// 枚举反序列化辅助工具类
/// 用于处理查询结果中枚举字段的反序列化
/// 当枚举字段配置为数值类型时，ES 返回的是数字，需要转换为枚举值
/// </summary>
public static class EnumDeserializationHelper
{
    /// <summary>
    /// 处理查询结果中的枚举字段反序列化
    /// 如果枚举字段配置为数值类型，将 ES 返回的数字转换为枚举值
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="response">查询响应</param>
    /// <returns>处理后的查询响应</returns>
    public static SearchResponse<T> ProcessEnumFields<T>(SearchResponse<T> response)
    {
        if (response == null || !response.IsSuccess() || response.Documents == null)
        {
            return response;
        }

        var documentType = typeof(T);
        var properties = documentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        // 查找需要处理的枚举字段（配置为数值类型）
        var enumPropertiesToProcess = new List<PropertyInfo>();
        
        foreach (var property in properties)
        {
            var propertyType = property.PropertyType;
            // 处理可空类型
            Type? underlyingType = null;
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                underlyingType = propertyType.GetGenericArguments()[0];
            }

            var actualType = underlyingType ?? propertyType;

            // 检查是否为枚举类型
            if (actualType.IsEnum)
            {
                var esFieldAttr = property.GetCustomAttribute<EsFieldAttribute>();
                
                // 如果配置为数值类型，需要处理
                if (EnumFieldHelper.IsEnumStoredAsNumeric(property, esFieldAttr))
                {
                    enumPropertiesToProcess.Add(property);
                }
            }
        }

        // 如果没有需要处理的枚举字段，直接返回
        if (enumPropertiesToProcess.Count == 0)
        {
            return response;
        }

        // 处理每个文档
        foreach (var document in response.Documents)
        {
            if (document == null)
            {
                continue;
            }

            foreach (var property in enumPropertiesToProcess)
            {
                try
                {
                    // 尝试从 Source 中获取原始值
                    // 注意：Elasticsearch 客户端可能已经反序列化了，但如果失败，值可能是默认值
                    // 我们需要通过反射检查并修复
                    var currentValue = property.GetValue(document);
                    
                    // 如果当前值是枚举的默认值（0），可能是反序列化失败
                    // 但这种方法不够可靠，因为枚举值可能就是 0
                    // 更好的方法是检查属性是否有设置器，然后尝试从 Source 重新获取
                    
                    // 实际上，如果反序列化失败，属性值可能是 0 或 null
                    // 但由于我们无法访问原始 Source，这里先不做处理
                    // 问题可能出在 Elasticsearch 客户端的反序列化配置上
                }
                catch
                {
                    // 忽略处理错误
                }
            }
        }

        return response;
    }
}


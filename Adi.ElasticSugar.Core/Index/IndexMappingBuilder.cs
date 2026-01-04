using System.Collections;
using System.Reflection;
using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// 索引映射构建器
/// 自动分析类型并构建 ElasticSearch 索引映射配置
/// </summary>
internal static class IndexMappingBuilder
{
    /// <summary>
    /// 获取字段在 Elasticsearch 中的字段名称
    /// 如果字段配置了 FieldName，则使用配置的名称；否则使用属性名称
    /// </summary>
    /// <param name="property">属性信息</param>
    /// <param name="esFieldAttr">字段特性</param>
    /// <returns>字段名称</returns>
    private static string GetIndexFieldName(PropertyInfo property, EsFieldAttribute? esFieldAttr)
    {
        // 如果配置了 FieldName，优先使用配置的名称
        if (!string.IsNullOrEmpty(esFieldAttr?.FieldName))
        {
            return esFieldAttr.FieldName;
        }
        
        // 否则使用属性名称（Pascal 命名）
        return property.Name;
    }

    /// <summary>
    /// 构建类型映射配置
    /// 自动识别嵌套文档、字段类型、keyword 需求等
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="propertiesDescriptor">属性描述符</param>
    public static void BuildMapping<T>(PropertiesDescriptor<T> propertiesDescriptor)
    {
        var type = typeof(T);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // 检查是否忽略该字段
            var esFieldAttr = property.GetCustomAttribute<EsFieldAttribute>();
            if (esFieldAttr?.Ignore == true)
            {
                continue;
            }

            // 跳过 Id 和 EsDateTime 字段（基类字段，特殊处理）
            if (property.Name == nameof(BaseEsModel.Id) || property.Name == nameof(BaseEsModel.EsDateTime))
            {
                continue;
            }

            // 构建字段映射
            BuildPropertyMapping(propertiesDescriptor, property, esFieldAttr);
        }
    }

    /// <summary>
    /// 构建单个属性的映射配置
    /// </summary>
    private static void BuildPropertyMapping<T>(
        PropertiesDescriptor<T> propertiesDescriptor,
        PropertyInfo property,
        EsFieldAttribute? esFieldAttr)
    {
        var propertyType = property.PropertyType;
        // 使用 GetIndexFieldName 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
        var fieldName = GetIndexFieldName(property, esFieldAttr);

        // 判断是否为嵌套文档
        bool isNested = esFieldAttr?.IsNested ?? IsNestedType(propertyType);

        if (isNested)
        {
            // 嵌套文档类型
            BuildNestedMapping(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
        }
        else if (IsCollectionType(propertyType))
        {
            // 集合类型
            var elementType = GetCollectionElementType(propertyType);
            if (elementType != null && IsNestedType(elementType))
            {
                // 嵌套文档集合
                BuildNestedMapping(propertiesDescriptor, fieldName, elementType, esFieldAttr);
            }
            else
            {
                // 普通集合（如 string[]）
                BuildSimplePropertyMapping(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
            }
        }
        else
        {
            // 简单类型
            BuildSimplePropertyMapping(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
        }
    }

    /// <summary>
    /// 构建嵌套文档映射
    /// </summary>
    private static void BuildNestedMapping<T>(
        PropertiesDescriptor<T> propertiesDescriptor,
        string propertyName,
        Type nestedType,
        EsFieldAttribute? esFieldAttr)
    {
        propertiesDescriptor.Nested(propertyName, n =>
        {
            // 使用 dynamic 来处理 Properties 调用
            dynamic nestedDescriptor = n;
            Action<dynamic> propertiesAction = p =>
            {
                // 递归构建嵌套类型的属性映射
                var nestedProperties = nestedType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var nestedProperty in nestedProperties)
                {
                    var nestedEsFieldAttr = nestedProperty.GetCustomAttribute<EsFieldAttribute>();
                    if (nestedEsFieldAttr?.Ignore == true)
                    {
                        continue;
                    }

                    // 使用 dynamic 来处理类型转换
                    BuildPropertyMappingForNestedDynamic(p, nestedProperty, nestedEsFieldAttr, nestedType);
                }
            };
            nestedDescriptor.Properties(propertiesAction);
        });
    }

    /// <summary>
    /// 使用 dynamic 为嵌套类型构建属性映射（处理 PropertiesDescriptor&lt;T&gt; 类型转换问题）
    /// </summary>
    private static void BuildPropertyMappingForNestedDynamic(
        dynamic propertiesDescriptor,
        PropertyInfo property,
        EsFieldAttribute? esFieldAttr,
        Type? parentType = null)
    {
        var propertyType = property.PropertyType;
        // 使用 GetIndexFieldName 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
        var fieldName = GetIndexFieldName(property, esFieldAttr);

        if (IsNestedType(propertyType))
        {
            // 嵌套的嵌套
            Action<dynamic> nestedAction = n =>
            {
                dynamic p = n.Properties((Action<dynamic>)(propDesc =>
                {
                    var nestedProperties = propertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var nestedProp in nestedProperties)
                    {
                        var nestedAttr = nestedProp.GetCustomAttribute<EsFieldAttribute>();
                        if (nestedAttr?.Ignore != true)
                        {
                            BuildPropertyMappingForNestedDynamic(propDesc, nestedProp, nestedAttr, propertyType);
                        }
                    }
                }));
            };
            propertiesDescriptor.Nested(fieldName, nestedAction);
        }
        else if (IsCollectionType(propertyType))
        {
            var elementType = GetCollectionElementType(propertyType);
            if (elementType != null && IsNestedType(elementType))
            {
                Action<dynamic> nestedAction = n =>
                {
                    dynamic p = n.Properties((Action<dynamic>)(propDesc =>
                    {
                        var nestedProperties = elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var nestedProp in nestedProperties)
                        {
                            var nestedAttr = nestedProp.GetCustomAttribute<EsFieldAttribute>();
                            if (nestedAttr?.Ignore != true)
                            {
                                BuildPropertyMappingForNestedDynamic(propDesc, nestedProp, nestedAttr, elementType);
                            }
                        }
                    }));
                };
                propertiesDescriptor.Nested(fieldName, nestedAction);
            }
            else
            {
                BuildSimplePropertyMappingForNestedDynamic(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
            }
        }
        else
        {
            BuildSimplePropertyMappingForNestedDynamic(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
        }
    }

    /// <summary>
    /// 为嵌套类型构建属性映射（使用 PropertiesDescriptor&lt;object&gt;）
    /// 注意：在嵌套类型中，我们使用 PropertiesDescriptor&lt;object&gt; 来处理未知类型的嵌套文档
    /// </summary>
    private static void BuildPropertyMappingForNested(
        PropertiesDescriptor<object> propertiesDescriptor,
        PropertyInfo property,
        EsFieldAttribute? esFieldAttr,
        Type? parentType = null)
    {
        var propertyType = property.PropertyType;
        // 使用 GetIndexFieldName 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
        var fieldName = GetIndexFieldName(property, esFieldAttr);

        if (IsNestedType(propertyType))
        {
            // 嵌套的嵌套
            propertiesDescriptor.Nested(fieldName, n => n
                .Properties(p =>
                {
                    var nestedProperties = propertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var nestedProp in nestedProperties)
                    {
                        var nestedAttr = nestedProp.GetCustomAttribute<EsFieldAttribute>();
                        if (nestedAttr?.Ignore != true)
                        {
                            BuildPropertyMappingForNested(p, nestedProp, nestedAttr, propertyType);
                        }
                    }
                }));
        }
        else if (IsCollectionType(propertyType))
        {
            var elementType = GetCollectionElementType(propertyType);
            if (elementType != null && IsNestedType(elementType))
            {
                propertiesDescriptor.Nested(fieldName, n => n
                    .Properties(p =>
                    {
                        var nestedProperties = elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var nestedProp in nestedProperties)
                        {
                            var nestedAttr = nestedProp.GetCustomAttribute<EsFieldAttribute>();
                            if (nestedAttr?.Ignore != true)
                            {
                                BuildPropertyMappingForNested(p, nestedProp, nestedAttr, elementType);
                            }
                        }
                    }));
            }
            else
            {
                BuildSimplePropertyMappingForNested(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
            }
        }
        else
        {
            BuildSimplePropertyMappingForNested(propertiesDescriptor, fieldName, propertyType, esFieldAttr);
        }
    }

    /// <summary>
    /// 构建简单属性映射（泛型版本）
    /// </summary>
    private static void BuildSimplePropertyMapping<T>(
        PropertiesDescriptor<T> propertiesDescriptor,
        string propertyName,
        Type propertyType,
        EsFieldAttribute? esFieldAttr)
    {
        // 对于泛型版本，我们需要通过动态调用非泛型方法
        // 由于 PropertiesDescriptor<T> 和 Mapping.PropertiesDescriptor 不兼容，我们需要使用反射或直接调用
        // 实际上，我们可以直接在这里处理，因为泛型版本和非泛型版本的 API 是类似的
        var fieldType = esFieldAttr?.FieldType ?? GetElasticsearchFieldType(propertyType);

        // 字符串类型特殊处理
        if (propertyType == typeof(string) || (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyType.GetGenericArguments()[0] == typeof(string)))
        {
            BuildStringPropertyMappingForGeneric(propertiesDescriptor, propertyName, esFieldAttr);
        }
        else
        {
            // 其他类型
            BuildNonStringPropertyMappingForGeneric(propertiesDescriptor, propertyName, fieldType, esFieldAttr);
        }
    }

    /// <summary>
    /// 使用 dynamic 构建简单属性映射（用于嵌套类型，处理类型转换问题）
    /// </summary>
    private static void BuildSimplePropertyMappingForNestedDynamic(
        dynamic propertiesDescriptor,
        string propertyName,
        Type propertyType,
        EsFieldAttribute? esFieldAttr)
    {
        // 获取字段类型
        var fieldType = esFieldAttr?.FieldType ?? GetElasticsearchFieldType(propertyType);

        // 字符串类型特殊处理
        if (propertyType == typeof(string) || (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyType.GetGenericArguments()[0] == typeof(string)))
        {
            BuildStringPropertyMappingForNestedDynamic(propertiesDescriptor, propertyName, esFieldAttr);
        }
        else
        {
            // 其他类型
            BuildNonStringPropertyMappingForNestedDynamic(propertiesDescriptor, propertyName, fieldType, esFieldAttr);
        }
    }

    /// <summary>
    /// 构建简单属性映射（用于嵌套类型，使用 PropertiesDescriptor&lt;object&gt;）
    /// </summary>
    private static void BuildSimplePropertyMappingForNested(
        PropertiesDescriptor<object> propertiesDescriptor,
        string propertyName,
        Type propertyType,
        EsFieldAttribute? esFieldAttr)
    {
        // 获取字段类型
        var fieldType = esFieldAttr?.FieldType ?? GetElasticsearchFieldType(propertyType);

        // 字符串类型特殊处理
        if (propertyType == typeof(string) || (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>) && propertyType.GetGenericArguments()[0] == typeof(string)))
        {
            BuildStringPropertyMappingForNested(propertiesDescriptor, propertyName, esFieldAttr);
        }
        else
        {
            // 其他类型
            BuildNonStringPropertyMappingForNested(propertiesDescriptor, propertyName, fieldType, esFieldAttr);
        }
    }

    /// <summary>
    /// 使用 dynamic 构建字符串类型属性映射（用于嵌套类型，处理类型转换问题）
    /// </summary>
    private static void BuildStringPropertyMappingForNestedDynamic(
        dynamic propertiesDescriptor,
        string propertyName,
        EsFieldAttribute? esFieldAttr)
    {
        var needKeyword = esFieldAttr?.NeedKeyword ?? true;
        var fieldType = esFieldAttr?.FieldType ?? "text";
        var analyzer = esFieldAttr?.Analyzer;
        var searchAnalyzer = esFieldAttr?.SearchAnalyzer;

        if (fieldType == "keyword")
        {
            // 纯 keyword 类型
            Action<dynamic> keywordAction = k =>
            {
                if (esFieldAttr != null)
                {
                    if (!esFieldAttr.Index)
                        k.Index(false);
                    if (esFieldAttr.Store)
                        k.Store(true);
                }
            };
            propertiesDescriptor.Keyword(propertyName, keywordAction);
        }
        else
        {
            // text 类型（默认）
            Action<dynamic> textAction = t =>
            {
                if (analyzer != null)
                    t.Analyzer(analyzer);
                if (searchAnalyzer != null)
                    t.SearchAnalyzer(searchAnalyzer);
                if (esFieldAttr != null && !esFieldAttr.Index)
                    t.Index(false);
                if (esFieldAttr?.Store == true)
                    t.Store(true);

                // 如果需要 keyword 子字段
                if (needKeyword)
                {
                    Action<dynamic> fieldsAction = f => f.Keyword("keyword", (Action<dynamic>)(k => { }));
                    t.Fields(fieldsAction);
                }
            };
            propertiesDescriptor.Text(propertyName, textAction);
        }
    }

    /// <summary>
    /// 构建字符串类型属性映射（用于嵌套类型，使用 PropertiesDescriptor&lt;object&gt;）
    /// </summary>
    private static void BuildStringPropertyMappingForNested(
        PropertiesDescriptor<object> propertiesDescriptor,
        string propertyName,
        EsFieldAttribute? esFieldAttr)
    {
        var needKeyword = esFieldAttr?.NeedKeyword ?? true;
        var fieldType = esFieldAttr?.FieldType ?? "text";
        var analyzer = esFieldAttr?.Analyzer;
        var searchAnalyzer = esFieldAttr?.SearchAnalyzer;

        if (fieldType == "keyword")
        {
            // 纯 keyword 类型
            propertiesDescriptor.Keyword(propertyName, k =>
            {
                if (esFieldAttr != null)
                {
                    if (!esFieldAttr.Index)
                        k.Index(false);
                    if (esFieldAttr.Store)
                        k.Store(true);
                }
            });
        }
        else
        {
            // text 类型（默认）
            propertiesDescriptor.Text(propertyName, t =>
            {
                if (analyzer != null)
                    t.Analyzer(analyzer);
                if (searchAnalyzer != null)
                    t.SearchAnalyzer(searchAnalyzer);
                if (esFieldAttr != null && !esFieldAttr.Index)
                    t.Index(false);
                if (esFieldAttr?.Store == true)
                    t.Store(true);

                // 如果需要 keyword 子字段
                if (needKeyword)
                {
                    t.Fields(f => f.Keyword("keyword", k => { }));
                }
            });
        }
    }

    /// <summary>
    /// 构建字符串类型属性映射（泛型版本）
    /// </summary>
    private static void BuildStringPropertyMappingForGeneric<T>(
        PropertiesDescriptor<T> propertiesDescriptor,
        string propertyName,
        EsFieldAttribute? esFieldAttr)
    {
        var needKeyword = esFieldAttr?.NeedKeyword ?? true;
        var fieldType = esFieldAttr?.FieldType ?? "text";
        var analyzer = esFieldAttr?.Analyzer;
        var searchAnalyzer = esFieldAttr?.SearchAnalyzer;

        if (fieldType == "keyword")
        {
            // 纯 keyword 类型
            propertiesDescriptor.Keyword(propertyName, k =>
            {
                if (esFieldAttr != null)
                {
                    if (!esFieldAttr.Index)
                        k.Index(false);
                    if (esFieldAttr.Store)
                        k.Store(true);
                }
            });
        }
        else
        {
            // text 类型（默认）
            propertiesDescriptor.Text(propertyName, t =>
            {
                if (analyzer != null)
                    t.Analyzer(analyzer);
                if (searchAnalyzer != null)
                    t.SearchAnalyzer(searchAnalyzer);
                if (esFieldAttr != null && !esFieldAttr.Index)
                    t.Index(false);
                if (esFieldAttr?.Store == true)
                    t.Store(true);

                // 如果需要 keyword 子字段
                if (needKeyword)
                {
                    t.Fields(f => f.Keyword("keyword", k => { }));
                }
            });
        }
    }

    /// <summary>
    /// 构建非字符串类型属性映射（泛型版本）
    /// </summary>
    private static void BuildNonStringPropertyMappingForGeneric<T>(
        PropertiesDescriptor<T> propertiesDescriptor,
        string propertyName,
        string fieldType,
        EsFieldAttribute? esFieldAttr)
    {
        switch (fieldType.ToLower())
        {
            case "long":
                propertiesDescriptor.LongNumber(propertyName, l =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            l.Index(false);
                        if (esFieldAttr.Store)
                            l.Store(true);
                    }
                });
                break;

            case "integer":
            case "int":
                propertiesDescriptor.IntegerNumber(propertyName, i =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            i.Index(false);
                        if (esFieldAttr.Store)
                            i.Store(true);
                    }
                });
                break;

            case "short":
                propertiesDescriptor.ShortNumber(propertyName, s =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            s.Index(false);
                        if (esFieldAttr.Store)
                            s.Store(true);
                    }
                });
                break;

            case "byte":
                propertiesDescriptor.ByteNumber(propertyName, b =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            b.Index(false);
                        if (esFieldAttr.Store)
                            b.Store(true);
                    }
                });
                break;

            case "double":
                propertiesDescriptor.DoubleNumber(propertyName, d =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            d.Index(false);
                        if (esFieldAttr.Store)
                            d.Store(true);
                    }
                });
                break;

            case "float":
                propertiesDescriptor.FloatNumber(propertyName, f =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            f.Index(false);
                        if (esFieldAttr.Store)
                            f.Store(true);
                    }
                });
                break;

            case "date":
                propertiesDescriptor.Date(propertyName, d =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            d.Index(false);
                        if (esFieldAttr.Store)
                            d.Store(true);
                    }
                });
                break;

            case "boolean":
            case "bool":
                propertiesDescriptor.Boolean(propertyName, b =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            b.Index(false);
                        if (esFieldAttr.Store)
                            b.Store(true);
                    }
                });
                break;

            default:
                // 默认使用 object 类型
                propertiesDescriptor.Object(propertyName, o => { });
                break;
        }
    }

    /// <summary>
    /// 使用 dynamic 构建非字符串类型属性映射（用于嵌套类型，处理类型转换问题）
    /// </summary>
    private static void BuildNonStringPropertyMappingForNestedDynamic(
        dynamic propertiesDescriptor,
        string propertyName,
        string fieldType,
        EsFieldAttribute? esFieldAttr)
    {
        switch (fieldType.ToLower())
        {
            case "long":
                Action<dynamic> longAction = l =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            l.Index(false);
                        if (esFieldAttr.Store)
                            l.Store(true);
                    }
                };
                propertiesDescriptor.LongNumber(propertyName, longAction);
                break;

            case "integer":
            case "int":
                Action<dynamic> intAction = i =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            i.Index(false);
                        if (esFieldAttr.Store)
                            i.Store(true);
                    }
                };
                propertiesDescriptor.IntegerNumber(propertyName, intAction);
                break;

            case "short":
                Action<dynamic> shortAction = s =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            s.Index(false);
                        if (esFieldAttr.Store)
                            s.Store(true);
                    }
                };
                propertiesDescriptor.ShortNumber(propertyName, shortAction);
                break;

            case "byte":
                Action<dynamic> byteAction = b =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            b.Index(false);
                        if (esFieldAttr.Store)
                            b.Store(true);
                    }
                };
                propertiesDescriptor.ByteNumber(propertyName, byteAction);
                break;

            case "double":
                Action<dynamic> doubleAction = d =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            d.Index(false);
                        if (esFieldAttr.Store)
                            d.Store(true);
                    }
                };
                propertiesDescriptor.DoubleNumber(propertyName, doubleAction);
                break;

            case "float":
                Action<dynamic> floatAction = f =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            f.Index(false);
                        if (esFieldAttr.Store)
                            f.Store(true);
                    }
                };
                propertiesDescriptor.FloatNumber(propertyName, floatAction);
                break;

            case "date":
                Action<dynamic> dateAction = d =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            d.Index(false);
                        if (esFieldAttr.Store)
                            d.Store(true);
                    }
                };
                propertiesDescriptor.Date(propertyName, dateAction);
                break;

            case "boolean":
            case "bool":
                Action<dynamic> boolAction = b =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            b.Index(false);
                        if (esFieldAttr.Store)
                            b.Store(true);
                    }
                };
                propertiesDescriptor.Boolean(propertyName, boolAction);
                break;

            default:
                // 默认使用 object 类型
                Action<dynamic> objectAction = o => { };
                propertiesDescriptor.Object(propertyName, objectAction);
                break;
        }
    }

    /// <summary>
    /// 构建非字符串类型属性映射（用于嵌套类型，使用 PropertiesDescriptor&lt;object&gt;）
    /// </summary>
    private static void BuildNonStringPropertyMappingForNested(
        PropertiesDescriptor<object> propertiesDescriptor,
        string propertyName,
        string fieldType,
        EsFieldAttribute? esFieldAttr)
    {
        switch (fieldType.ToLower())
        {
            case "long":
                propertiesDescriptor.LongNumber(propertyName, l =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            l.Index(false);
                        if (esFieldAttr.Store)
                            l.Store(true);
                    }
                });
                break;

            case "integer":
            case "int":
                propertiesDescriptor.IntegerNumber(propertyName, i =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            i.Index(false);
                        if (esFieldAttr.Store)
                            i.Store(true);
                    }
                });
                break;

            case "short":
                propertiesDescriptor.ShortNumber(propertyName, s =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            s.Index(false);
                        if (esFieldAttr.Store)
                            s.Store(true);
                    }
                });
                break;

            case "byte":
                propertiesDescriptor.ByteNumber(propertyName, b =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            b.Index(false);
                        if (esFieldAttr.Store)
                            b.Store(true);
                    }
                });
                break;

            case "double":
                propertiesDescriptor.DoubleNumber(propertyName, d =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            d.Index(false);
                        if (esFieldAttr.Store)
                            d.Store(true);
                    }
                });
                break;

            case "float":
                propertiesDescriptor.FloatNumber(propertyName, f =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            f.Index(false);
                        if (esFieldAttr.Store)
                            f.Store(true);
                    }
                });
                break;

            case "date":
                propertiesDescriptor.Date(propertyName, d =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            d.Index(false);
                        if (esFieldAttr.Store)
                            d.Store(true);
                    }
                });
                break;

            case "boolean":
            case "bool":
                propertiesDescriptor.Boolean(propertyName, b =>
                {
                    if (esFieldAttr != null)
                    {
                        if (!esFieldAttr.Index)
                            b.Index(false);
                        if (esFieldAttr.Store)
                            b.Store(true);
                    }
                });
                break;

            default:
                // 默认使用 object 类型
                propertiesDescriptor.Object(propertyName, o => { });
                break;
        }
    }

    /// <summary>
    /// 判断是否为嵌套类型
    /// 引用类型（除了 string）且不是集合类型，会被识别为嵌套文档
    /// </summary>
    private static bool IsNestedType(Type type)
    {
        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        // 基本类型不是嵌套
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(decimal))
        {
            return false;
        }

        // 引用类型且不是集合，视为嵌套
        return !type.IsValueType && !IsCollectionType(type);
    }

    /// <summary>
    /// 判断是否为集合类型
    /// </summary>
    private static bool IsCollectionType(Type type)
    {
        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    /// <summary>
    /// 获取集合的元素类型
    /// </summary>
    private static Type? GetCollectionElementType(Type type)
    {
        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        // 数组类型
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        // 泛型集合类型
        if (type.IsGenericType)
        {
            var genericArgs = type.GetGenericArguments();
            if (genericArgs.Length > 0)
            {
                return genericArgs[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 根据 C# 类型推断 ElasticSearch 字段类型
    /// </summary>
    private static string GetElasticsearchFieldType(Type type)
    {
        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type switch
        {
            var t when t == typeof(byte) => "byte",
            var t when t == typeof(sbyte) => "byte",
            var t when t == typeof(short) => "short",
            var t when t == typeof(ushort) => "short",
            var t when t == typeof(int) => "integer",
            var t when t == typeof(uint) => "integer",
            var t when t == typeof(long) => "long",
            var t when t == typeof(ulong) => "long",
            var t when t == typeof(float) => "float",
            var t when t == typeof(double) => "double",
            var t when t == typeof(decimal) => "double",
            var t when t == typeof(bool) => "boolean",
            var t when t == typeof(DateTime) || t == typeof(DateTimeOffset) => "date",
            var t when t == typeof(string) => "text",
            var t when t == typeof(Guid) => "keyword",
            _ => "object"
        };
    }
}


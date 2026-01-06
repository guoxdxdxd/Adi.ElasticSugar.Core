using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Adi.ElasticSugar.Core.Models;
using Adi.ElasticSugar.Core.Utils;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Adi.ElasticSugar.Core.Search;

/// <summary>
/// ElasticSearch 搜索查询构建器
/// 支持链式调用，类似 SqlSugar 的使用方式
/// </summary>
/// <typeparam name="T">文档类型</typeparam>
public class EsSearchQueryable<T>
{
    private readonly ElasticsearchClient _client;
    private readonly string _index;
    private readonly List<Expression<Func<T, bool>>> _whereExpressions = new();
    private readonly List<(Expression<Func<T, object>> field, bool descending)> _orderByExpressions = new();
    private int? _skip;
    private int? _take;
    private bool _trackTotalHits = false;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="index">索引名称</param>
    internal EsSearchQueryable(ElasticsearchClient client, string index)
    {
        _client = client;
        _index = index;
    }

    /// <summary>
    /// 添加 Where 条件（AND 逻辑）
    /// </summary>
    /// <param name="predicate">Lambda 表达式条件</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        if (predicate != null)
        {
            _whereExpressions.Add(predicate);
        }
        return this;
    }

    /// <summary>
    /// 条件判断：只有当条件为 true 时才添加 Where 条件
    /// </summary>
    /// <param name="condition">判断条件</param>
    /// <param name="predicate">Lambda 表达式条件</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> WhereIf(bool condition, Expression<Func<T, bool>> predicate)
    {
        if (condition && predicate != null)
        {
            _whereExpressions.Add(predicate);
        }
        return this;
    }

    /// <summary>
    /// 升序排序
    /// </summary>
    /// <param name="field">排序字段</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> OrderBy(Expression<Func<T, object>> field)
    {
        if (field != null)
        {
            _orderByExpressions.Add((field, false));
        }
        return this;
    }

    /// <summary>
    /// 降序排序
    /// </summary>
    /// <param name="field">排序字段</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> OrderByDesc(Expression<Func<T, object>> field)
    {
        if (field != null)
        {
            _orderByExpressions.Add((field, true));
        }
        return this;
    }

    /// <summary>
    /// 跳过指定数量的文档（分页）
    /// </summary>
    /// <param name="count">跳过的数量</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> Skip(int count)
    {
        _skip = count;
        return this;
    }

    /// <summary>
    /// 获取指定数量的文档（分页）
    /// </summary>
    /// <param name="count">获取的数量</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> Take(int count)
    {
        _take = count;
        return this;
    }

    /// <summary>
    /// 启用跟踪总命中数
    /// </summary>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsSearchQueryable<T> TrackTotalHits()
    {
        _trackTotalHits = true;
        return this;
    }

    /// <summary>
    /// 执行查询并返回结果列表
    /// </summary>
    /// <returns>查询结果</returns>
    public async Task<SearchResponse<T>> ToListAsync()
    {
        var descriptor = BuildSearchDescriptor();

        return await _client.SearchAsync<T>(descriptor);
                
        // var response = await _client.SearchAsync<T>(descriptor);
        
        // // 处理枚举字段的反序列化（如果枚举字段配置为数值类型）
        // return ProcessEnumFieldsDeserialization(response);
    }

    /// <summary>
    /// 处理枚举字段的反序列化
    /// 当枚举字段配置为数值类型时，ES 返回的是数字，需要手动转换为枚举值
    /// </summary>
    private SearchResponse<T> ProcessEnumFieldsDeserialization(SearchResponse<T> response)
    {
        if (response == null || !response.IsSuccess() || response.Documents == null)
        {
            return response!;
        }

        var documentType = typeof(T);
        var properties = documentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        // 查找需要处理的枚举字段（配置为数值类型）
        var enumPropertiesToProcess = new Dictionary<PropertyInfo, Type>();
        
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
                    enumPropertiesToProcess[property] = actualType;
                }
            }
        }

        // 如果没有需要处理的枚举字段，直接返回
        if (enumPropertiesToProcess.Count == 0)
        {
            return response;
        }

        // 处理每个文档
        // 注意：我们需要从 Hits 中获取原始 Source 来手动反序列化
        // 但由于 SearchResponse<T> 的 Documents 已经反序列化，如果失败可能已经抛出异常
        // 我们需要尝试从 Source 重新获取值
        
        // 获取字段名映射（camelCase）
        var fieldNameMap = new Dictionary<string, (PropertyInfo property, Type enumType)>();
        foreach (var (property, enumType) in enumPropertiesToProcess)
        {
            var fieldName = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            // 如果配置了 FieldName，使用配置的名称
            var esFieldAttr = property.GetCustomAttribute<EsFieldAttribute>();
            if (!string.IsNullOrEmpty(esFieldAttr?.FieldName))
            {
                fieldName = esFieldAttr.FieldName;
            }
            fieldNameMap[fieldName] = (property, enumType);
        }

        // 尝试从 Source 重新反序列化枚举字段
        if (response.Hits != null)
        {
            var documentsList = response.Documents.ToList();
            var hitsList = response.Hits.ToList();
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            for (int i = 0; i < hitsList.Count && i < documentsList.Count; i++)
            {
                var hit = hitsList[i];
                var document = documentsList[i];
                
                if (hit == null || document == null)
                {
                    continue;
                }

                // 尝试从 Source 获取原始 JSON
                // 注意：Elasticsearch 客户端的 Source 可能已经是反序列化的对象
                // 我们需要通过反射访问 Source 属性
                try
                {
                    // 使用反射获取 Source
                    var sourceProperty = hit.GetType().GetProperty("Source");
                    if (sourceProperty != null)
                    {
                        var source = sourceProperty.GetValue(hit);
                        if (source != null)
                        {
                            // 将 Source 序列化为 JSON，然后重新解析
                            var sourceJson = JsonSerializer.Serialize(source, jsonOptions);
                            var sourceDoc = JsonDocument.Parse(sourceJson);
                            
                            // 处理每个枚举字段
                            foreach (var (fieldName, (property, enumType)) in fieldNameMap)
                            {
                                if (sourceDoc.RootElement.TryGetProperty(fieldName, out var enumElement))
                                {
                                    if (enumElement.ValueKind == JsonValueKind.Number)
                                    {
                                        // 获取数值
                                        var numericValue = enumElement.GetInt64();
                                        
                                        // 转换为枚举值
                                        var enumValue = Enum.ToObject(enumType, numericValue);
                                        
                                        // 设置属性值
                                        property.SetValue(document, enumValue);
                                    }
                                    else if (enumElement.ValueKind == JsonValueKind.Null)
                                    {
                                        // 处理可空类型
                                        if (property.PropertyType.IsGenericType && 
                                            property.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                                        {
                                            property.SetValue(document, null);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 如果无法从 Source 获取，忽略错误
                    // 这可能意味着反序列化已经成功，或者 Source 不可访问
                }
            }
        }

        return response;
    }

    /// <summary>
    /// 执行查询并返回结果列表（带分页信息）
    /// </summary>
    /// <param name="pageIndex">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>查询结果</returns>
    public async Task<SearchResponse<T>> ToPageAsync(int pageIndex, int pageSize)
    {
        _skip = (pageIndex - 1) * pageSize;
        _take = pageSize;
        var result = await ToListAsync();
        return result ?? throw new InvalidOperationException("查询返回了 null 结果");
    }

    /// <summary>
    /// 构建搜索描述符
    /// </summary>
    /// <returns>搜索描述符</returns>
    private SearchRequestDescriptor<T> BuildSearchDescriptor()
    {
        var descriptor = new SearchRequestDescriptor<T>();
        descriptor = descriptor.Index(_index);

        // 构建查询条件
        var queryAction = BuildQuery();
        if (queryAction != null)
        {
            descriptor = descriptor.Query(queryAction);
        }

        // 构建排序
        if (_orderByExpressions.Count > 0)
        {
            descriptor = descriptor.Sort(sort =>
            {
                foreach (var (field, descending) in _orderByExpressions)
                {
                    var (fieldPath, propertyInfo) = ExtractFieldPathWithProperty(field);
                    if (!string.IsNullOrEmpty(fieldPath))
                    {
                        // 排序需要使用精确匹配字段（对于字符串类型的 text 字段，需要使用 .keyword）
                        var finalFieldPath = ExpressionParser.GetFieldPathForExactMatch(fieldPath, propertyInfo);
                        if (descending)
                        {
                            sort.Field(finalFieldPath, fs => fs.Order(SortOrder.Desc));
                        }
                        else
                        {
                            sort.Field(finalFieldPath, fs => fs.Order(SortOrder.Asc));
                        }
                    }
                }
            });
        }

        // 分页
        if (_skip.HasValue)
        {
            descriptor = descriptor.From(_skip.Value);
        }

        if (_take.HasValue)
        {
            descriptor = descriptor.Size(_take.Value);
        }

        // 跟踪总命中数
        if (_trackTotalHits)
        {
            descriptor = descriptor.TrackTotalHits(new Elastic.Clients.Elasticsearch.Core.Search.TrackHits(true));
        }

        return descriptor;
    }

    /// <summary>
    /// 构建查询条件
    /// </summary>
    /// <returns>查询动作</returns>
    private Action<QueryDescriptor<T>>? BuildQuery()
    {
        if (_whereExpressions.Count == 0)
        {
            return null; // 返回 null 表示使用默认查询（MatchAll）
        }

        // 解析所有表达式，组合成 Bool 查询
        var mustActions = new List<Action<QueryDescriptor<T>>>();
        
        foreach (var expression in _whereExpressions)
        {
            var action = ExpressionParser.ParseExpression<T>(expression);
            if (action != null)
            {
                mustActions.Add(action);
            }
        }

        if (mustActions.Count == 0)
        {
            return null;
        }

        if (mustActions.Count == 1)
        {
            return mustActions[0];
        }

        // 多个条件组合成 Bool.Must 查询
        return q => q.Bool(b => b.Must(mustActions.ToArray()));
    }

    /// <summary>
    /// 从 Lambda 表达式中提取字段路径和 PropertyInfo
    /// 字段名会进行转换：如果配置了 EsFieldAttribute.FieldName，则使用配置的名称；
    /// 否则将 PascalCase 转换为 camelCase，以匹配 Elasticsearch 客户端序列化时的字段命名约定
    /// </summary>
    private (string? fieldPath, PropertyInfo? propertyInfo) ExtractFieldPathWithProperty(Expression<Func<T, object>> expression)
    {
        var memberExpression = GetMemberExpression(expression.Body);
        if (memberExpression == null)
        {
            return (null, null);
        }

        var path = new List<string>();
        var properties = new List<PropertyInfo>();
        var current = (Expression?)memberExpression;

        while (current is MemberExpression member)
        {
            // 如果是属性，保存 PropertyInfo 并获取字段名称
            if (member.Member is PropertyInfo propertyInfo)
            {
                properties.Insert(0, propertyInfo);
                
                // 使用 FieldNameHelper 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
                // 如果没有配置 FieldName，会自动将 PascalCase 转换为 camelCase
                var fieldName = FieldNameHelper.GetIndexFieldName(propertyInfo);
                
                path.Insert(0, fieldName);
            }
            else
            {
                // 非属性成员（如字段），将 PascalCase 转换为 camelCase
                path.Insert(0, FieldNameHelper.GetIndexFieldName(member.Member.Name));
            }
            
            current = member.Expression;
        }

        var fieldPath = path.Count > 0 ? string.Join(".", path) : null;
        var lastProperty = properties.Count > 0 ? properties[properties.Count - 1] : null;

        return (fieldPath, lastProperty);
    }


    /// <summary>
    /// 从 Lambda 表达式中提取字段路径
    /// </summary>
    private string? ExtractFieldPath(Expression<Func<T, object>> expression)
    {
        var (fieldPath, _) = ExtractFieldPathWithProperty(expression);
        return fieldPath;
    }

    /// <summary>
    /// 从表达式中提取成员表达式
    /// </summary>
    private MemberExpression? GetMemberExpression(Expression expression)
    {
        return expression switch
        {
            MemberExpression member => member,
            UnaryExpression unary when unary.NodeType == ExpressionType.Convert => GetMemberExpression(unary.Operand),
            _ => null
        };
    }
}


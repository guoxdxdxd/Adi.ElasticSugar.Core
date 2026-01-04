using System.Linq.Expressions;
using System.Reflection;
using Adi.ElasticSugar.Core.Models;
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
        return await ToListAsync();
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
                
                // 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
                // 如果没有配置 FieldName，需要将 PascalCase 转换为 camelCase
                // 因为 Elasticsearch 客户端在序列化文档时会自动将属性名转换为 camelCase
                var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();
                var fieldName = !string.IsNullOrEmpty(esFieldAttr?.FieldName) 
                    ? esFieldAttr.FieldName 
                    : ToCamelCase(propertyInfo.Name);
                
                path.Insert(0, fieldName);
            }
            else
            {
                // 非属性成员（如字段），将 PascalCase 转换为 camelCase
                path.Insert(0, ToCamelCase(member.Member.Name));
            }
            
            current = member.Expression;
        }

        var fieldPath = path.Count > 0 ? string.Join(".", path) : null;
        var lastProperty = properties.Count > 0 ? properties[properties.Count - 1] : null;

        return (fieldPath, lastProperty);
    }

    /// <summary>
    /// 将 PascalCase 转换为 camelCase
    /// 例如：IntField -> intField
    /// 用于匹配 Elasticsearch 客户端序列化时的字段命名约定
    /// Elasticsearch 客户端在序列化文档时会自动将 C# 的 PascalCase 属性名转换为 camelCase
    /// 因此查询和排序时也需要使用 camelCase 字段名才能正确匹配
    /// </summary>
    /// <param name="pascalCase">PascalCase 格式的字符串</param>
    /// <returns>camelCase 格式的字符串</returns>
    private static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
        {
            return pascalCase;
        }

        // 如果第一个字符是小写，直接返回
        if (char.IsLower(pascalCase[0]))
        {
            return pascalCase;
        }

        // 将第一个字符转换为小写
        if (pascalCase.Length == 1)
        {
            return char.ToLowerInvariant(pascalCase[0]).ToString();
        }

        return char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
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


using System.Linq.Expressions;
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
                    var fieldPath = ExtractFieldPath(field);
                    if (!string.IsNullOrEmpty(fieldPath))
                    {
                        var path = fieldPath; // 避免空引用警告
                        if (descending)
                        {
                            sort.Field(path, fs => fs.Order(SortOrder.Desc));
                        }
                        else
                        {
                            sort.Field(path, fs => fs.Order(SortOrder.Asc));
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
    /// 从 Lambda 表达式中提取字段路径
    /// </summary>
    private string? ExtractFieldPath(Expression<Func<T, object>> expression)
    {
        var memberExpression = GetMemberExpression(expression.Body);
        if (memberExpression == null)
        {
            return null;
        }

        var path = new List<string>();
        var current = (Expression?)memberExpression;

        while (current is MemberExpression member)
        {
            path.Insert(0, member.Member.Name);
            current = member.Expression;
        }

        return path.Count > 0 ? string.Join(".", path) : null;
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


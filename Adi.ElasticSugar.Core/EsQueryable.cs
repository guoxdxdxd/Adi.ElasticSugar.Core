using System.Linq.Expressions;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Adi.ElasticSugar.Core;

/// <summary>
/// ElasticSearch 查询构建器，类似数据库 ORM 的 IQueryable
/// 支持 Where 方法和 Lambda 表达式自动构建字段路径
/// </summary>
/// <typeparam name="T">文档类型</typeparam>
public class EsQueryable<T>
{
    private readonly List<Expression<Func<T, bool>>> _whereExpressions = new();

    /// <summary>
    /// 创建新的查询构建器
    /// </summary>
    public EsQueryable()
    {
    }

    /// <summary>
    /// 添加 Where 条件（AND 逻辑）
    /// 支持链式调用，多个 Where 之间是 AND 关系
    /// </summary>
    /// <param name="predicate">Lambda 表达式条件</param>
    /// <returns>查询构建器（支持链式调用）</returns>
    public EsQueryable<T> Where(Expression<Func<T, bool>> predicate)
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
    public EsQueryable<T> WhereIf(bool condition, Expression<Func<T, bool>> predicate)
    {
        if (condition && predicate != null)
        {
            _whereExpressions.Add(predicate);
        }
        return this;
    }

    /// <summary>
    /// 构建 Elasticsearch QueryDescriptor
    /// </summary>
    /// <returns>查询动作</returns>
    public Action<QueryDescriptor<T>> BuildQuery()
    {
        if (_whereExpressions.Count == 0)
        {
            return q => q.MatchAll(new MatchAllQuery());
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
            return q => q.MatchAll(new MatchAllQuery());
        }

        if (mustActions.Count == 1)
        {
            return mustActions[0];
        }

        // 多个条件组合成 Bool.Must 查询
        return q => q.Bool(b => b.Must(mustActions.ToArray()));
    }

    /// <summary>
    /// 获取所有 Where 表达式（用于调试）
    /// </summary>
    internal IReadOnlyList<Expression<Func<T, bool>>> GetExpressions() => _whereExpressions;
}


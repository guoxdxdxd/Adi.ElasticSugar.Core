using System.Linq.Expressions;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Adi.ElasticSugar.Core;

/// <summary>
/// EsQueryable 扩展方法
/// </summary>
public static class EsQueryableExtensions
{
    /// <summary>
    /// 创建新的查询构建器
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <returns>查询构建器</returns>
    public static EsQueryable<T> Query<T>()
    {
        return new EsQueryable<T>();
    }

    /// <summary>
    /// 将查询构建器应用到 SearchRequestDescriptor
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="descriptor">搜索请求描述符</param>
    /// <param name="queryable">查询构建器</param>
    /// <returns>搜索请求描述符（支持链式调用）</returns>
    public static SearchRequestDescriptor<T> Query<T>(
        this SearchRequestDescriptor<T> descriptor, 
        EsQueryable<T> queryable)
    {
        if (queryable == null)
        {
            return descriptor;
        }

        var queryAction = queryable.BuildQuery();
        return descriptor.Query(queryAction);
    }
}


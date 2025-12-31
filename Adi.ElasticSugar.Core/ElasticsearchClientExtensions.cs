using Elastic.Clients.Elasticsearch;

namespace Adi.ElasticSugar.Core;

/// <summary>
/// ElasticsearchClient 扩展方法
/// 提供类似 SqlSugar 的查询方式
/// </summary>
public static class ElasticsearchClientExtensions
{
    /// <summary>
    /// 创建搜索查询构建器
    /// 类似 SqlSugar 的 AsQueryable，但直接指定索引
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="index">索引名称（支持通配符，如 "orders*"）</param>
    /// <returns>搜索查询构建器</returns>
    public static EsSearchQueryable<T> Search<T>(this ElasticsearchClient client, string index)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (string.IsNullOrEmpty(index))
        {
            throw new ArgumentException("索引名称不能为空", nameof(index));
        }

        return new EsSearchQueryable<T>(client, index);
    }
}


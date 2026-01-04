using Adi.ElasticSugar.Core.Index;
using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch;

namespace Adi.ElasticSugar.Core.Search;

/// <summary>
/// ElasticsearchClient 扩展方法
/// 提供类似 SqlSugar 的查询方式
/// </summary>
public static class ElasticsearchClientExtensions
{
    /// <summary>
    /// 创建搜索查询构建器
    /// 根据泛型类型 T 的 EsIndexAttribute 特性自动获取索引名称
    /// 如果类型 T 继承自 BaseEsModel 且包含 EsIndexAttribute 特性，将自动生成索引通配符模式（如 "orders-*"）
    /// 用于查询所有相关索引
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承自 BaseEsModel 且包含 EsIndexAttribute 特性</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <returns>搜索查询构建器</returns>
    /// <exception cref="InvalidOperationException">当类型 T 不继承自 BaseEsModel 时抛出</exception>
    public static EsSearchQueryable<T> Search<T>(this ElasticsearchClient client) where T : BaseEsModel
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        // 从泛型类型 T 的特性中获取索引通配符模式
        // 使用通配符模式可以查询所有相关索引（如 orders-2024-01, orders-2024-02 等）
        var indexPattern = IndexNameGenerator.GenerateIndexPatternFromAttribute<T>();
        return new EsSearchQueryable<T>(client, indexPattern);
    }

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


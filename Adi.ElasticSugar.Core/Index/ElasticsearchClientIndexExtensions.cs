using Adi.ElasticSugar.Core.Index;
using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Adi.ElasticSugar.Core;

/// <summary>
/// ElasticsearchClient 索引管理扩展方法
/// 提供索引创建、管理等功能的扩展方法
/// </summary>
public static class ElasticsearchClientIndexExtensions
{
    /// <summary>
    /// 创建索引管理器
    /// </summary>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <returns>索引管理器实例</returns>
    public static ElasticsearchIndexManager IndexManager(this ElasticsearchClient client)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        return new ElasticsearchIndexManager(client);
    }

    /// <summary>
    /// 创建索引（便捷方法）
    /// 如果索引已存在则不创建
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="indexName">索引名称</param>
    /// <param name="configure">可选的映射配置委托，用于手动配置特定字段</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>如果索引已存在或创建成功返回 true</returns>
    public static async Task<bool> CreateIndexIfNotExistsAsync<T>(
        this ElasticsearchClient client,
        string indexName,
        Action<PropertiesDescriptor<T>>? configure = null,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        var manager = client.IndexManager();
        return await manager.CreateIndexIfNotExistsAsync<T>(indexName, configure, numberOfShards, numberOfReplicas);
    }

    /// <summary>
    /// 根据索引前缀和时间自动创建索引
    /// 索引名称格式：{indexPrefix}-{yyyy-MM}
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="indexPrefix">索引前缀（例如："orders"）</param>
    /// <param name="dateTime">时间字段（用于生成年月部分）</param>
    /// <param name="configure">可选的映射配置委托</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>索引名称</returns>
    public static async Task<string> CreateIndexByDateAsync<T>(
        this ElasticsearchClient client,
        string indexPrefix,
        DateTime dateTime,
        Action<PropertiesDescriptor<T>>? configure = null,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        var indexName = IndexNameGenerator.GenerateIndexName<T>(indexPrefix, dateTime);
        await client.CreateIndexIfNotExistsAsync<T>(indexName, configure, numberOfShards, numberOfReplicas);
        return indexName;
    }

    /// <summary>
    /// 根据索引前缀和文档实例自动创建索引
    /// 从文档的 EsDateTime 字段提取时间信息
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="indexPrefix">索引前缀</param>
    /// <param name="document">文档实例</param>
    /// <param name="configure">可选的映射配置委托</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>索引名称</returns>
    public static async Task<string> CreateIndexByDocumentAsync<T>(
        this ElasticsearchClient client,
        string indexPrefix,
        T document,
        Action<PropertiesDescriptor<T>>? configure = null,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var indexName = IndexNameGenerator.GenerateIndexName<T>(indexPrefix, document);
        await client.CreateIndexIfNotExistsAsync<T>(indexName, configure, numberOfShards, numberOfReplicas);
        return indexName;
    }
}


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
    /// 根据文档对象创建索引（使用文档的 GetIndexNameFromAttribute 方法）
    /// 如果索引已存在则不创建
    /// 索引名称通过文档对象的 GetIndexNameFromAttribute() 方法获取，该方法直接从特性读取配置
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="document">文档实例，使用其 GetIndexNameFromAttribute() 方法生成索引名称</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>创建的索引名称</returns>
    /// <exception cref="ArgumentNullException">当 document 为 null 时抛出</exception>
    /// <exception cref="Exception">当创建索引失败时抛出异常</exception>
    public static async Task<string> CreateIndexForDocumentAsync<T>(
        this ElasticsearchClient client,
        T document,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        // 使用文档对象的 GetIndexNameFromAttribute() 方法获取索引名称（不依赖 IndexNameGenerator）
        var indexName = document.GetIndexNameFromAttribute();
        var manager = client.IndexManager();
        
        // 创建索引，如果失败会抛出异常（由 CreateIndexIfNotExistsAsync 内部处理）
        await manager.CreateIndexIfNotExistsAsync<T>(indexName, numberOfShards, numberOfReplicas);
        
        return indexName;
    }

    /// <summary>
    /// 批量创建索引（基于文档列表）
    /// 如果索引已存在则不创建
    /// 索引名称根据每个文档的 GetIndexNameFromAttribute() 方法自动生成
    /// 会自动去重，相同的索引名称只会创建一次
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="documents">文档实例列表，使用每个文档的 GetIndexNameFromAttribute() 方法生成索引名称</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>索引名称和对应文档列表的字典，键为索引名称，值为该索引对应的文档列表</returns>
    /// <exception cref="ArgumentNullException">当 documents 为 null 时抛出</exception>
    /// <exception cref="Exception">当创建索引失败时抛出异常</exception>
    public static async Task<Dictionary<string, List<T>>> CreateIndexesForDocumentsAsync<T>(
        this ElasticsearchClient client,
        IEnumerable<T> documents,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        if (documents == null)
        {
            throw new ArgumentNullException(nameof(documents));
        }

        // 过滤空文档并按索引名称分组
        var documentList = documents.Where(d => d != null).ToList();
        if (documentList.Count == 0)
        {
            return new Dictionary<string, List<T>>();
        }

        // 按索引名称分组文档
        var documentsByIndex = documentList
            .GroupBy(d => d.GetIndexNameFromAttribute())
            .ToDictionary(g => g.Key, g => g.ToList());

        if (documentsByIndex.Count == 0)
        {
            return new Dictionary<string, List<T>>();
        }

        var manager = client.IndexManager();
        var indexNames = documentsByIndex.Keys.ToList();

        // 并行创建所有索引（提高效率）
        // 如果任何索引创建失败，会抛出异常（由 CreateIndexIfNotExistsAsync 内部处理）
        var tasks = indexNames.Select(async indexName =>
        {
            await manager.CreateIndexIfNotExistsAsync<T>(indexName, numberOfShards, numberOfReplicas);
            return indexName;
        });

        // 等待所有索引创建完成，如果有任何失败会抛出异常
        await Task.WhenAll(tasks);

        // 返回索引名称和对应文档列表的字典
        return documentsByIndex;
    }
}


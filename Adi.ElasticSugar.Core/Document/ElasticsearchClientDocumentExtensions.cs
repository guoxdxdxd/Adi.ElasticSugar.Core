using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Adi.ElasticSugar.Core.Document;

/// <summary>
/// ElasticsearchClient 文档推送扩展方法
/// 提供单个文档和批量文档的推送功能，推送前自动检查并创建索引
/// </summary>
public static class ElasticsearchClientDocumentExtensions
{
    /// <summary>
    /// 推送单个文档到 Elasticsearch
    /// 推送前会自动检查索引是否存在，不存在则自动创建
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="document">要推送的文档</param>
    /// <param name="numberOfShards">分片数量，仅在创建索引时使用，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，仅在创建索引时使用，默认 1</param>
    /// <returns>推送结果，包含文档 ID 和索引名称</returns>
    /// <exception cref="ArgumentNullException">当 document 为 null 时抛出</exception>
    /// <exception cref="Exception">当推送失败时抛出异常</exception>
    public static async Task<IndexResponse> PushDocumentAsync<T>(
        this ElasticsearchClient client,
        T document,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        ArgumentNullException.ThrowIfNull(client);

        ArgumentNullException.ThrowIfNull(document);

        // 使用文档对象创建索引（使用 GetIndexNameFromAttribute 方法）
        // 方法返回创建的索引名称
        var createdIndexName = await client.CreateIndexForDocumentAsync(
            document,
            numberOfShards,
            numberOfReplicas);

        // 确定索引名称：优先使用用户指定的 indexName，否则使用创建索引时返回的索引名称
        var finalIndexName = createdIndexName;

        // 推送文档
        var response = await client.IndexAsync(document, idx =>
        {
            idx.Index(finalIndexName);
            if (document.Id != null)
            {
                idx.Id(document.Id.ToString()!);
            }
        });

        if (!response.IsSuccess())
        {
            throw new Exception($"推送文档失败: {response.DebugInformation}");
        }

        return response;
    }

    /// <summary>
    /// 批量推送文档到 Elasticsearch
    /// 推送前会自动检查索引是否存在，不存在则自动创建
    /// 使用 Bulk API 进行批量操作，提高性能
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="documents">要推送的文档列表</param>
    /// <param name="numberOfShards">分片数量，仅在创建索引时使用，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，仅在创建索引时使用，默认 1</param>
    /// <param name="batchSize">批量操作的大小，默认 1000，超过此数量会分批处理</param>
    /// <returns>批量推送结果</returns>
    /// <exception cref="ArgumentNullException">当 documents 为 null 或空时抛出</exception>
    /// <exception cref="Exception">当推送失败时抛出异常</exception>
    public static async Task<BulkResponse> PushDocumentsAsync<T>(
        this ElasticsearchClient client,
        IEnumerable<T> documents,
        int numberOfShards = 3,
        int numberOfReplicas = 1,
        int batchSize = 1000) where T : BaseEsModel
    {
        ArgumentNullException.ThrowIfNull(client);

        ArgumentNullException.ThrowIfNull(documents);

        var documentList = documents.ToList();
        if (documentList.Count == 0)
        {
            throw new ArgumentException("文档列表不能为空", nameof(documents));
        }

        // 使用新增的批量创建索引方法，自动为每个文档创建对应的索引
        // 该方法会自动去重，相同的索引名称只会创建一次
        // 方法返回索引名称和对应文档列表的字典
        var documentsByIndex = await client.CreateIndexesForDocumentsAsync(
            documentList,
            numberOfShards,
            numberOfReplicas);

        // 按索引分组推送文档，每个索引的文档分别推送
        var allResponses = new List<BulkResponse>();

        foreach (var kvp in documentsByIndex)
        {
            var indexName = kvp.Key;
            var documentsForIndex = kvp.Value;

            // 如果该索引的文档数量小于等于批次大小，直接批量推送
            if (documentsForIndex.Count <= batchSize)
            {
                var response = await PushBatchAsync(client, indexName, documentsForIndex);
                allResponses.Add(response);

                // 检查是否有错误
                if (!response.IsSuccess())
                {
                    throw new Exception($"批量推送文档失败（索引: {indexName}）: {response.DebugInformation}");
                }
            }
            else
            {
                // 如果该索引的文档数量超过批次大小，分批处理
                for (int i = 0; i < documentsForIndex.Count; i += batchSize)
                {
                    var batch = documentsForIndex.Skip(i).Take(batchSize).ToList();
                    var response = await PushBatchAsync(client, indexName, batch);
                    allResponses.Add(response);

                    // 检查是否有错误
                    if (!response.IsSuccess())
                    {
                        throw new Exception($"批量推送文档失败（索引: {indexName}, 批次 {i / batchSize + 1}）: {response.DebugInformation}");
                    }
                }
            }
        }

        // 返回最后一个响应（包含所有批次的信息）
        return allResponses.Last();
    }

    /// <summary>
    /// 执行单个批次的批量推送操作
    /// 所有文档使用同一个索引名称
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="client">Elasticsearch 客户端</param>
    /// <param name="indexName">索引名称</param>
    /// <param name="batch">批次文档列表</param>
    /// <returns>批量推送结果</returns>
    private static async Task<BulkResponse> PushBatchAsync<T>(
        ElasticsearchClient client,
        string indexName,
        List<T> batch) where T : BaseEsModel
    {
        var response = await client.BulkAsync(b => b
            .Index(indexName)
            .IndexMany(batch, (descriptor, document) =>
            {
                if (document.Id != null)
                {
                    descriptor.Id(document.Id.ToString()!);
                }
            }));

        if (!response.IsSuccess())
        {
            throw new Exception($"批量推送文档失败: {response.DebugInformation}");
        }

        // 检查是否有错误项
        if (response.Errors)
        {
            var errorDetails = string.Join("; ", response.Items
                .Where(item => item.Error != null)
                .Select(item => $"ID: {item.Id}, Error: {item.Error?.Reason}"));

            throw new Exception($"批量推送文档时发生错误: {errorDetails}");
        }

        return response;
    }
}


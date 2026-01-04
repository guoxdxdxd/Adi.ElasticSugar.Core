using System.Collections.Concurrent;
using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// ElasticSearch 索引管理器
/// 提供索引创建、存在性检查等功能
/// </summary>
public class ElasticsearchIndexManager
{
    private readonly ElasticsearchClient _client;
    private static readonly ConcurrentDictionary<string, bool> _indexCache = new();
    private readonly object _lockObject = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="client">Elasticsearch 客户端</param>
    public ElasticsearchIndexManager(ElasticsearchClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// 创建索引，如果存在则不创建
    /// </summary>
    /// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
    /// <param name="indexName">索引名称</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>如果索引已存在或创建成功返回 true，否则返回 false</returns>
    /// <exception cref="Exception">当检查索引存在性或创建索引失败时抛出异常</exception>
    public async Task<bool> CreateIndexIfNotExistsAsync<T>(
        string indexName,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new ArgumentException("索引名称不能为空", nameof(indexName));
        }

        // 复用 IndexExistsAsync 方法检查索引是否存在（包含缓存检查和实际索引检查）
        if (await IndexExistsAsync(indexName))
        {
            return true;
        }

        // 如果索引不存在，使用锁保证线程安全地创建索引
        lock (_lockObject)
        {
            // 双重检查：在锁内再次检查索引是否存在（可能其他线程已经创建了）
            // 复用 IndexExistsAsync 的同步检查逻辑（仅检查缓存，因为上面已经检查过实际索引）
            if (_indexCache.TryGetValue(indexName, out var exists) && exists)
            {
                return true;
            }

            // 创建索引
            var createIndexResponse = _client.Indices.Create(indexName, i => i
                .Mappings(m => m.Properties<T>(p =>
                {
                    // 先应用自动映射
                    IndexMappingBuilder.BuildMapping(p);
                }))
                .Settings(s => s
                    .NumberOfShards(numberOfShards)
                    .NumberOfReplicas(numberOfReplicas)
                )
            );

            if (!createIndexResponse.IsSuccess())
            {
                throw new Exception($"创建索引失败: {indexName}, {createIndexResponse.DebugInformation}");
            }

            // 使用 AddOrUpdate 确保无论缓存中是否存在该键，都能更新为 true
            // 如果之前缓存了 false（索引不存在），现在创建成功后需要更新为 true
            _indexCache.AddOrUpdate(indexName, true, (key, oldValue) => true);
            return true;
        }
    }

    /// <summary>
    /// 检查索引是否存在
    /// </summary>
    /// <param name="indexName">索引名称</param>
    /// <returns>如果索引存在返回 true，否则返回 false</returns>
    public async Task<bool> IndexExistsAsync(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            return false;
        }

        // 检查缓存
        if (_indexCache.TryGetValue(indexName, out var exists))
        {
            return exists;
        }

        var response = await _client.Indices.ExistsAsync(indexName);
        if (!response.IsSuccess())
        {
            throw new Exception($"检查索引是否存在失败: {indexName}, {response.DebugInformation}");
        }

        exists = response.Exists;
        // 使用 AddOrUpdate 确保无论缓存中是否存在该键，都能更新为最新的存在状态
        // 如果之前缓存了 false，但索引现在存在了，需要更新为 true
        _indexCache.AddOrUpdate(indexName, exists, (key, oldValue) => exists);
        return exists;
    }

    /// <summary>
    /// 删除索引
    /// </summary>
    /// <param name="indexName">索引名称</param>
    /// <returns>删除成功返回 true</returns>
    public async Task<bool> DeleteIndexAsync(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new ArgumentException("索引名称不能为空", nameof(indexName));
        }

        var response = await _client.Indices.DeleteAsync(indexName);
        if (!response.IsSuccess())
        {
            throw new Exception($"删除索引失败: {indexName}, {response.DebugInformation}");
        }

        _indexCache.TryRemove(indexName, out _);
        return true;
    }

    /// <summary>
    /// 清除索引缓存
    /// 当索引被外部删除或修改时，可以调用此方法清除缓存
    /// </summary>
    public void ClearCache()
    {
        _indexCache.Clear();
    }

    /// <summary>
    /// 清除指定索引的缓存
    /// </summary>
    /// <param name="indexName">索引名称</param>
    public void ClearCache(string indexName)
    {
        _indexCache.TryRemove(indexName, out _);
    }
}


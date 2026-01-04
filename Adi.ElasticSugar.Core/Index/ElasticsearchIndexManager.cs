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
    private readonly ConcurrentDictionary<string, bool> _indexCache = new();
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
    /// <param name="configure">可选的映射配置委托，用于手动配置特定字段</param>
    /// <param name="numberOfShards">分片数量，默认 3</param>
    /// <param name="numberOfReplicas">副本数量，默认 1</param>
    /// <returns>如果索引已存在或创建成功返回 true，否则返回 false</returns>
    /// <exception cref="Exception">当检查索引存在性或创建索引失败时抛出异常</exception>
    public async Task<bool> CreateIndexIfNotExistsAsync<T>(
        string indexName,
        Action<PropertiesDescriptor<T>>? configure = null,
        int numberOfShards = 3,
        int numberOfReplicas = 1) where T : BaseEsModel
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new ArgumentException("索引名称不能为空", nameof(indexName));
        }

        // 检查缓存
        if (_indexCache.TryGetValue(indexName, out var exists) && exists)
        {
            return true;
        }

        lock (_lockObject)
        {
            // 双重检查
            if (_indexCache.TryGetValue(indexName, out exists) && exists)
            {
                return true;
            }

            // 检查索引是否存在
            var existsResponse = _client.Indices.Exists(indexName);
            if (!existsResponse.IsSuccess())
            {
                throw new Exception($"检查索引是否存在失败: {indexName}, {existsResponse.DebugInformation}");
            }

            if (existsResponse.Exists)
            {
                _indexCache.TryAdd(indexName, true);
                return true;
            }

            // 创建索引
            var createIndexResponse = _client.Indices.Create(indexName, i => i
                .Mappings(m => m.Properties<T>(p =>
                {
                    // 先应用自动映射
                    IndexMappingBuilder.BuildMapping(p);

                    // 然后应用手动配置（如果有）
                    configure?.Invoke(p);
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

            _indexCache.TryAdd(indexName, true);
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
        _indexCache.TryAdd(indexName, exists);
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


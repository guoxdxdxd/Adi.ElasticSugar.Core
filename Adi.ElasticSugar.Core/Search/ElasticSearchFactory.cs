using Adi.ElasticSugar.Core.Configuration;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace Adi.ElasticSugar.Core.Search;

/// <summary>
/// ElasticSearch 客户端工厂：按名称维护多个 <see cref="ElasticsearchClient"/> 实例。
/// </summary>
public sealed class ElasticSearchFactory
{
    private readonly Dictionary<string, ElasticsearchClient> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 根据配置注册客户端；同名已存在时忽略。
    /// </summary>
    /// <param name="config">连接配置。</param>
    public void AddClient(EsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new ArgumentException("ElasticSearch client name cannot be empty.", nameof(config));
        }

        if (_clients.ContainsKey(config.Name))
        {
            return;
        }

        if (config.Uris.Count == 0)
        {
            throw new ArgumentException("ElasticSearch Uris cannot be empty.", nameof(config));
        }

        var nodes = config.Uris
            .Where(static uri => !string.IsNullOrWhiteSpace(uri))
            .Select(static uri => new Uri(uri, UriKind.Absolute))
            .ToArray();

        if (nodes.Length == 0)
        {
            throw new ArgumentException("ElasticSearch Uris cannot be empty or whitespace.", nameof(config));
        }

        var settings = new ElasticsearchClientSettings(new StaticNodePool(nodes))
            .DisableAutomaticProxyDetection()
            .DisableDirectStreaming()
            .ServerCertificateValidationCallback(static (_, _, _, _) => true)
            .Authentication(new BasicAuthentication(config.UserName, config.Password));

        _clients.Add(config.Name, new ElasticsearchClient(settings));
    }

    /// <summary>
    /// 按名称获取客户端；未注册时返回 null。
    /// </summary>
    /// <param name="name">客户端名称。</param>
    public ElasticsearchClient? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _clients.GetValueOrDefault(name);
    }

    /// <summary>
    /// 判断指定名称的客户端是否已注册。
    /// </summary>
    /// <param name="name">客户端名称。</param>
    public bool Exists(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _clients.ContainsKey(name);
    }
}

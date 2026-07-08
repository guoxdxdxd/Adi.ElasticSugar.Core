namespace Adi.ElasticSugar.Core.Configuration;

/// <summary>
/// ElasticSearch 具名客户端连接配置。
/// </summary>
public sealed class EsConfig
{
    /// <summary>
    /// 客户端名称，在工厂内唯一且不可为空。
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 节点地址集合（绝对 URI，如 <c>http://es:9200</c>）。
    /// </summary>
    public required List<string> Uris { get; set; }

    /// <summary>
    /// Basic Authentication 用户名。
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// Basic Authentication 密码。
    /// </summary>
    public required string Password { get; set; }
}

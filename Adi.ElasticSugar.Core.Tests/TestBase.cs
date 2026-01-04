using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests;

/// <summary>
/// 测试基类
/// 提供 Elasticsearch 客户端初始化和清理功能
/// 支持从配置文件（appsettings.json）或环境变量读取 Elasticsearch 连接配置
/// </summary>
public abstract class TestBase : IAsyncLifetime
{
    private static IConfiguration? _configuration;

    /// <summary>
    /// Elasticsearch 客户端
    /// </summary>
    protected ElasticsearchClient Client { get; private set; } = null!;

    /// <summary>
    /// 测试索引前缀（用于区分不同测试的索引）
    /// </summary>
    protected virtual string TestIndexPrefix => "test";

    /// <summary>
    /// 获取配置实例（单例模式）
    /// </summary>
    private static IConfiguration GetConfiguration()
    {
        if (_configuration == null)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(); // 环境变量优先级高于配置文件

            _configuration = builder.Build();
        }

        return _configuration;
    }

    /// <summary>
    /// 从配置中获取 Elasticsearch 连接设置
    /// 支持从 appsettings.json 或环境变量读取
    /// 环境变量格式：
    ///   ELASTICSEARCH__URIS__0=https://172.17.12.19:9200
    ///   ELASTICSEARCH__USERNAME=elastic
    ///   ELASTICSEARCH__PASSWORD=your_password
    /// </summary>
    private static ElasticsearchClientSettings GetElasticsearchSettings()
    {
        var config = GetConfiguration();
        var esConfig = config.GetSection("Elasticsearch");

        // 从配置中读取 URI（支持多个 URI，取第一个）
        // 优先读取数组格式 Uris:0，如果没有则读取字符串格式 Uris
        var uriString = esConfig["Uris:0"] ?? esConfig["Uris"] ?? "http://localhost:9200";
        
        // 如果配置的是数组格式字符串，尝试解析
        if (uriString.StartsWith("["))
        {
            // 简单处理：如果是 JSON 数组格式，提取第一个 URI
            uriString = uriString.Trim('[', ']', '"', ' ').Split(',')[0].Trim('"', ' ');
        }

        var uri = new Uri(uriString);

        // 创建客户端设置
        var settings = new ElasticsearchClientSettings(uri)
            .DisableDirectStreaming() // 用于调试，可以看到请求和响应内容
            .EnableDebugMode(); // 启用调试模式

        // 如果配置了用户名和密码，设置认证
        var userName = esConfig["UserName"] ?? esConfig["Username"];
        var password = esConfig["Password"];

        if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password))
        {
            settings = settings.Authentication(new Elastic.Clients.Elasticsearch.Authentication.BasicAuthentication(userName, password));
        }

        // 对于 HTTPS 连接，如果是测试环境，可能需要跳过证书验证
        // 注意：生产环境应该使用有效的证书
        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            // 跳过证书验证（仅用于测试环境）
            // 生产环境应该配置正确的证书
            // 通过配置 HttpClientFactory 来跳过证书验证
            settings = settings.HttpClientFactory(() =>
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                return new HttpClient(handler);
            });
        }

        return settings;
    }

    /// <summary>
    /// 初始化测试环境
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        // 从配置中获取 Elasticsearch 连接设置
        var settings = GetElasticsearchSettings();
        Client = new ElasticsearchClient(settings);

        // 检查 Elasticsearch 是否可用
        try
        {
            var healthResponse = await Client.Cluster.HealthAsync();
            if (!healthResponse.IsSuccess())
            {
                throw new InvalidOperationException(
                    $"无法连接到 Elasticsearch。响应信息: {healthResponse.DebugInformation}");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法连接到 Elasticsearch。请检查配置文件和连接设置。错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 清理测试环境
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        // 清理测试创建的索引
        await CleanupTestIndexesAsync();
    }

    /// <summary>
    /// 清理测试索引
    /// </summary>
    protected virtual async Task CleanupTestIndexesAsync()
    {
        try
        {
            // 获取所有以测试前缀开头的索引
            var indicesResponse = await Client.Indices.GetAsync($"{TestIndexPrefix}*");
            if (indicesResponse.IsSuccess() && indicesResponse.Indices != null)
            {
                foreach (var indexName in indicesResponse.Indices.Keys)
                {
                    try
                    {
                        await Client.Indices.DeleteAsync(indexName);
                    }
                    catch
                    {
                        // 忽略删除失败的情况
                    }
                }
            }
        }
        catch
        {
            // 忽略清理失败的情况
        }
    }

    /// <summary>
    /// 生成测试索引名称
    /// </summary>
    /// <param name="suffix">后缀</param>
    /// <returns>索引名称</returns>
    protected string GetTestIndexName(string suffix = "")
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        return string.IsNullOrEmpty(suffix)
            ? $"{TestIndexPrefix}-{timestamp}"
            : $"{TestIndexPrefix}-{suffix}-{timestamp}";
    }

    /// <summary>
    /// 等待索引刷新（确保文档可搜索）
    /// </summary>
    protected async Task RefreshIndexAsync(string indexName)
    {
        await Client.Indices.RefreshAsync(indexName);
        // 额外等待一小段时间，确保索引完全刷新
        await Task.Delay(100);
    }
}


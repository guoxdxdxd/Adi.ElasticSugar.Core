using Adi.ElasticSugar.Core.Configuration;
using Adi.ElasticSugar.Core.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adi.ElasticSugar.Core.DependencyInjection;

/// <summary>
/// ElasticSearch 依赖注入扩展。
/// </summary>
public static class ElasticSearchServiceCollectionExtensions
{
    /// <summary>
    /// 从配置节 <c>ElasticSearchs</c> 注册单例 <see cref="ElasticSearchFactory"/>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    public static IServiceCollection AddElasticSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var esConfigs = configuration.GetSection("ElasticSearchs").Get<List<EsConfig>>();
        var factory = new ElasticSearchFactory();
        if (esConfigs != null)
        {
            foreach (var esConfig in esConfigs)
            {
                factory.AddClient(esConfig);
            }
        }

        services.AddSingleton(factory);
        return services;
    }
}

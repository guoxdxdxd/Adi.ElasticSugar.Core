using System.Collections.Concurrent;
using System.Reflection;
using Adi.ElasticSugar.Core.Models;

namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// 索引名称生成器
/// 统一使用 IIndexNameGenerator 接口生成索引名称
/// IndexFormat 格式（Year、YearMonth）通过预设的生成器实现
/// 支持完全自定义的索引名称生成逻辑
/// </summary>
public static class IndexNameGenerator
{
    // 静态注册的自定义生成器缓存（类型 -> 生成器实例）
    private static readonly ConcurrentDictionary<Type, object> _customGenerators = new();

    /// <summary>
    /// 注册自定义索引名称生成器
    /// 允许在运行时注册自定义生成器，优先级高于特性中指定的生成器
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="generator">自定义索引名称生成器实例</param>
    /// <exception cref="ArgumentNullException">当 generator 为 null 时抛出</exception>
    public static void RegisterGenerator<T>(IIndexNameGenerator<T> generator) where T : BaseEsModel
    {
        if (generator == null)
        {
            throw new ArgumentNullException(nameof(generator));
        }

        _customGenerators.AddOrUpdate(typeof(T), generator, (_, _) => generator);
    }

    /// <summary>
    /// 取消注册自定义索引名称生成器
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <returns>如果成功移除返回 true，否则返回 false</returns>
    public static bool UnregisterGenerator<T>() where T : BaseEsModel
    {
        return _customGenerators.TryRemove(typeof(T), out _);
    }

    /// <summary>
    /// 获取索引名称生成器
    /// 优先从静态注册中获取，其次从特性中读取并实例化，最后根据 Format 创建预设生成器
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置则使用此值，如果此值也为空则使用类名</param>
    /// <returns>生成器实例</returns>
    private static IIndexNameGenerator<T> GetGenerator<T>(string? defaultPrefix = null) where T : BaseEsModel
    {
        // 优先从静态注册中获取
        if (_customGenerators.TryGetValue(typeof(T), out var registeredGenerator))
        {
            return (IIndexNameGenerator<T>)registeredGenerator;
        }

        // 从特性中读取配置
        var attribute = GetIndexAttribute<T>();
        
        // 如果特性中指定了自定义生成器类型，则实例化它
        if (attribute?.CustomGeneratorType != null)
        {
            try
            {
                // 检查类型是否实现了 IIndexNameGenerator<T> 接口
                var generatorInterface = typeof(IIndexNameGenerator<>).MakeGenericType(typeof(T));
                if (!generatorInterface.IsAssignableFrom(attribute.CustomGeneratorType))
                {
                    throw new InvalidOperationException(
                        $"自定义生成器类型 {attribute.CustomGeneratorType.Name} 必须实现 IIndexNameGenerator<{typeof(T).Name}> 接口");
                }

                // 实例化生成器（支持无参构造函数）
                var generator = Activator.CreateInstance(attribute.CustomGeneratorType);
                if (generator == null)
                {
                    throw new InvalidOperationException(
                        $"无法实例化自定义生成器类型 {attribute.CustomGeneratorType.Name}，请确保有无参构造函数");
                }

                var typedGenerator = (IIndexNameGenerator<T>)generator;
                
                // 缓存实例化的生成器，避免重复创建
                _customGenerators.TryAdd(typeof(T), typedGenerator);
                
                return typedGenerator;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"创建自定义索引名称生成器失败: {ex.Message}", ex);
            }
        }

        // 如果没有自定义生成器，则根据 Format 创建预设生成器
        var format = attribute?.Format ?? IndexFormat.YearMonth;
        var prefix = GetIndexPrefix<T>(defaultPrefix);
        
        IIndexNameGenerator<T> defaultGenerator = format switch
        {
            IndexFormat.Year => new YearIndexNameGenerator<T>(prefix),
            IndexFormat.YearMonth => new YearMonthIndexNameGenerator<T>(prefix),
            _ => new YearMonthIndexNameGenerator<T>(prefix) // 默认使用年月格式
        };
        
        // 缓存预设生成器，避免重复创建
        _customGenerators.TryAdd(typeof(T), defaultGenerator);
        
        return defaultGenerator;
    }

    /// <summary>
    /// 从类型特性中获取索引配置
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <returns>索引配置特性，如果不存在则返回null</returns>
    private static EsIndexAttribute? GetIndexAttribute<T>()
    {
        return typeof(T).GetCustomAttribute<EsIndexAttribute>();
    }

    /// <summary>
    /// 获取索引前缀
    /// 优先从特性中读取，如果特性中未设置则使用类名的小写形式
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置且此参数不为空则使用此值</param>
    /// <returns>索引前缀</returns>
    private static string GetIndexPrefix<T>(string? defaultPrefix = null)
    {
        var attribute = GetIndexAttribute<T>();
        if (attribute != null && !string.IsNullOrWhiteSpace(attribute.IndexPrefix))
        {
            return attribute.IndexPrefix;
        }

        if (!string.IsNullOrWhiteSpace(defaultPrefix))
        {
            return defaultPrefix;
        }

        return typeof(T).Name.ToLowerInvariant();
    }


    /// <summary>
    /// 生成索引名称（基于年月）
    /// 格式：{indexPrefix}-{yyyy-MM}
    /// 例如：orders-2024-01
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="indexPrefix">索引前缀（例如："orders"）</param>
    /// <param name="dateTime">时间字段（用于生成年月部分）</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexName<T>(string indexPrefix, DateTime dateTime)
    {
        if (string.IsNullOrWhiteSpace(indexPrefix))
        {
            throw new ArgumentException("索引前缀不能为空", nameof(indexPrefix));
        }

        var yearMonth = dateTime.ToString("yyyy-MM");
        return $"{indexPrefix}-{yearMonth}";
    }

    /// <summary>
    /// 生成索引名称（基于年月）
    /// 从文档实例中提取时间字段
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="indexPrefix">索引前缀</param>
    /// <param name="document">文档实例</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexName<T>(string indexPrefix, T document) where T : BaseEsModel
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return GenerateIndexName<T>(indexPrefix, document.EsDateTime);
    }

    /// <summary>
    /// 生成索引名称（基于当前年月）
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="indexPrefix">索引前缀</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexName<T>(string indexPrefix)
    {
        return GenerateIndexName<T>(indexPrefix, DateTime.Now);
    }

    /// <summary>
    /// 根据特性配置自动生成索引名称
    /// 统一使用 IIndexNameGenerator 生成索引名称
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="document">文档实例</param>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置则使用此值，如果此值也为空则使用类名</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexNameFromAttribute<T>(T document, string? defaultPrefix = null) where T : BaseEsModel
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var generator = GetGenerator<T>(defaultPrefix);
        return generator.GenerateIndexName(document);
    }

    /// <summary>
    /// 根据特性配置自动生成索引名称（基于时间）
    /// 统一使用 IIndexNameGenerator 生成索引名称
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="dateTime">时间字段</param>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置则使用此值，如果此值也为空则使用类名</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexNameFromAttribute<T>(DateTime dateTime, string? defaultPrefix = null) where T : BaseEsModel
    {
        var generator = GetGenerator<T>(defaultPrefix);
        return generator.GenerateIndexName(dateTime);
    }

    /// <summary>
    /// 根据特性配置自动生成索引名称（基于当前时间）
    /// 统一使用 IIndexNameGenerator 生成索引名称
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置则使用此值，如果此值也为空则使用类名</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexNameFromAttribute<T>(string? defaultPrefix = null) where T : BaseEsModel
    {
        return GenerateIndexNameFromAttribute<T>(DateTime.Now, defaultPrefix);
    }

    /// <summary>
    /// 生成索引通配符模式（用于查询多个索引）
    /// 格式：{indexPrefix}-*
    /// 例如：orders-*
    /// </summary>
    /// <param name="indexPrefix">索引前缀</param>
    /// <returns>索引通配符模式</returns>
    public static string GenerateIndexPattern(string indexPrefix)
    {
        if (string.IsNullOrWhiteSpace(indexPrefix))
        {
            throw new ArgumentException("索引前缀不能为空", nameof(indexPrefix));
        }

        return $"{indexPrefix}-*";
    }

    /// <summary>
    /// 根据特性配置生成索引通配符模式
    /// 统一使用 IIndexNameGenerator 生成通配符模式
    /// </summary>
    /// <typeparam name="T">文档类型</typeparam>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置则使用此值，如果此值也为空则使用类名</param>
    /// <returns>索引通配符模式</returns>
    public static string GenerateIndexPatternFromAttribute<T>(string? defaultPrefix = null) where T : BaseEsModel
    {
        var generator = GetGenerator<T>(defaultPrefix);
        return generator.GenerateIndexPattern();
    }

    /// <summary>
    /// 根据文档实例的类型特性和 EsDateTime 生成索引名称（非泛型版本）
    /// 用于 BaseEsModel 实例方法调用
    /// </summary>
    /// <param name="document">文档实例</param>
    /// <param name="defaultPrefix">默认前缀，如果特性中未设置则使用此值，如果此值也为空则使用类名</param>
    /// <returns>索引名称</returns>
    public static string GenerateIndexNameFromAttribute(BaseEsModel document, string? defaultPrefix = null)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var type = document.GetType();
        
        // 查找泛型方法 GenerateIndexNameFromAttribute<T>(T document, string? defaultPrefix)
        var methods = typeof(IndexNameGenerator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(GenerateIndexNameFromAttribute) 
                && m.IsGenericMethod 
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsGenericParameter
                && m.GetParameters()[1].ParameterType == typeof(string))
            .ToList();

        if (methods.Count == 0)
        {
            throw new InvalidOperationException(
                $"无法找到 GenerateIndexNameFromAttribute<T> 泛型方法");
        }

        // 使用第一个匹配的方法（应该是接受文档实例的版本）
        var genericMethod = methods[0].MakeGenericMethod(type);
        
        // 调用泛型方法，传入文档实例
        var result = genericMethod.Invoke(null, new object?[] { document, defaultPrefix });
        
        return result?.ToString() ?? throw new InvalidOperationException(
            $"生成索引名称失败，返回值为 null");
    }
}


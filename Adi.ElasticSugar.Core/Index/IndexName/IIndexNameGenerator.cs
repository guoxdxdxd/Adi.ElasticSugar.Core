using Adi.ElasticSugar.Core.Models;

namespace Adi.ElasticSugar.Core.Index;

/// <summary>
/// 索引名称生成器接口
/// 允许开发者实现完全自定义的索引名称生成逻辑
/// </summary>
/// <typeparam name="T">文档类型，必须继承 BaseEsModel</typeparam>
public interface IIndexNameGenerator<T> where T : BaseEsModel
{
    /// <summary>
    /// 根据文档实例生成索引名称
    /// </summary>
    /// <param name="document">文档实例</param>
    /// <returns>索引名称</returns>
    string GenerateIndexName(T document);

    /// <summary>
    /// 根据时间字段生成索引名称
    /// </summary>
    /// <param name="dateTime">时间字段</param>
    /// <returns>索引名称</returns>
    string GenerateIndexName(DateTime dateTime);

    /// <summary>
    /// 生成索引通配符模式（用于查询多个索引）
    /// </summary>
    /// <returns>索引通配符模式，例如：orders-*</returns>
    string GenerateIndexPattern();
}


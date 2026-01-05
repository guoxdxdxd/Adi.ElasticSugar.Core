namespace Adi.ElasticSugar.Core.Utils;

/// <summary>
/// 字符串辅助工具类
/// 提供字符串转换和处理的通用方法
/// </summary>
public static class StringHelper
{
    /// <summary>
    /// 将 PascalCase 转换为 camelCase
    /// 例如：IntField -> intField, NullableBoolField -> nullableBoolField
    /// 用于匹配 Elasticsearch 客户端序列化时的字段命名约定
    /// Elasticsearch 客户端在序列化文档时会自动将 C# 的 PascalCase 属性名转换为 camelCase
    /// 因此索引映射、查询和排序时也需要使用 camelCase 字段名才能正确匹配
    /// </summary>
    /// <param name="pascalCase">PascalCase 格式的字符串</param>
    /// <returns>camelCase 格式的字符串</returns>
    /// <remarks>
    /// 转换规则：
    /// - 如果字符串为空或 null，直接返回原值
    /// - 如果第一个字符已经是小写，直接返回原值
    /// - 否则将第一个字符转换为小写，其余字符保持不变
    /// </remarks>
    public static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
        {
            return pascalCase;
        }

        // 如果第一个字符是小写，直接返回
        if (char.IsLower(pascalCase[0]))
        {
            return pascalCase;
        }

        // 将第一个字符转换为小写
        if (pascalCase.Length == 1)
        {
            return char.ToLowerInvariant(pascalCase[0]).ToString();
        }

        return char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
    }
}


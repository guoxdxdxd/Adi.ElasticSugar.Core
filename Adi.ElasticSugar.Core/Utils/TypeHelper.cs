namespace Adi.ElasticSugar.Core.Utils;

/// <summary>
/// 类型辅助工具类
/// 提供类型判断和处理的通用方法
/// </summary>
public static class TypeHelper
{
    /// <summary>
    /// 判断是否为枚举类型
    /// 包括可空枚举类型（Nullable&lt;EnumType&gt;）
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>如果是枚举类型（包括可空枚举类型）返回 true，否则返回 false</returns>
    /// <remarks>
    /// 此方法会处理可空类型（Nullable&lt;T&gt;），如果传入的是可空枚举类型，
    /// 会先提取内部的枚举类型，然后判断是否为枚举类型。
    /// 例如：Nullable&lt;OrderStatus&gt; 会被识别为枚举类型。
    /// </remarks>
    public static bool IsEnumType(Type type)
    {
        if (type == null)
        {
            return false;
        }

        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type.IsEnum;
    }
}


using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Adi.ElasticSugar.Core.Models;
using Elastic.Clients.Elasticsearch.QueryDsl;
using static Elastic.Clients.Elasticsearch.FieldValue;

namespace Adi.ElasticSugar.Core.Search;

/// <summary>
/// 表达式树解析器
/// 将 Lambda 表达式解析为 Elasticsearch 查询条件
/// </summary>
public static class ExpressionParser
{
    /// <summary>
    /// 解析表达式并转换为查询动作
    /// </summary>
    public static Action<QueryDescriptor<T>>? ParseExpression<T>(Expression<Func<T, bool>> expression)
    {
        if (expression == null)
        {
            return null;
        }

        return ParseNode<T>(expression.Body);
    }

    /// <summary>
    /// 解析表达式节点
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseNode<T>(Expression node)
    {
        return node switch
        {
            // 二元运算符：==, !=, >, <, >=, <=
            BinaryExpression binary => ParseBinaryExpression<T>(binary),
            
            // 方法调用：Contains, StartsWith, EndsWith 等
            MethodCallExpression methodCall => ParseMethodCall<T>(methodCall),
            
            // 成员访问表达式：处理布尔字段的直接引用（如 x => x.BoolField）
            MemberExpression member => ParseMemberExpression<T>(member),
            
            // 一元表达式：处理类型转换（如 (bool)x.BoolField）
            UnaryExpression unary when unary.NodeType == ExpressionType.Convert => ParseNode<T>(unary.Operand),
            
            // 逻辑运算符：&&, ||
            // 注意：在 C# 中，&& 和 || 会被编译为 BinaryExpression，但 NodeType 不同
            _ => null
        };
    }

    /// <summary>
    /// 解析二元表达式（包括比较运算符和逻辑运算符）
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseBinaryExpression<T>(BinaryExpression binary)
    {
        return binary.NodeType switch
        {
            // 逻辑 AND
            ExpressionType.AndAlso => ParseAndExpression<T>(binary),
            
            // 逻辑 OR
            ExpressionType.OrElse => ParseOrExpression<T>(binary),
            
            // 等于
            ExpressionType.Equal => ParseComparison<T>(binary, ComparisonType.Equals),
            
            // 不等于
            ExpressionType.NotEqual => ParseComparison<T>(binary, ComparisonType.NotEquals),
            
            // 大于
            ExpressionType.GreaterThan => ParseComparison<T>(binary, ComparisonType.GreaterThan),
            
            // 大于等于
            ExpressionType.GreaterThanOrEqual => ParseComparison<T>(binary, ComparisonType.GreaterThanOrEqual),
            
            // 小于
            ExpressionType.LessThan => ParseComparison<T>(binary, ComparisonType.LessThan),
            
            // 小于等于
            ExpressionType.LessThanOrEqual => ParseComparison<T>(binary, ComparisonType.LessThanOrEqual),
            
            _ => null
        };
    }

    /// <summary>
    /// 解析 AND 表达式
    /// 将左右两个查询条件组合成 Bool.Must 查询
    /// 注意：每个 action 都会创建一个独立的查询，然后通过 Bool.Must 组合
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseAndExpression<T>(BinaryExpression binary)
    {
        var left = ParseNode<T>(binary.Left);
        var right = ParseNode<T>(binary.Right);

        if (left == null && right == null)
        {
            return null;
        }

        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        // 组合成 Bool.Must 查询
        // 每个 action 会创建一个查询，然后通过 Bool.Must 组合
        // 这样可以正确处理嵌套查询和普通查询的组合
        return q => q.Bool(b => b.Must(new[] { left, right }));
    }

    /// <summary>
    /// 解析 OR 表达式
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseOrExpression<T>(BinaryExpression binary)
    {
        var left = ParseNode<T>(binary.Left);
        var right = ParseNode<T>(binary.Right);

        if (left == null && right == null)
        {
            return null;
        }

        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        // 组合成 Bool.Should 查询
        return q => q.Bool(b => b.Should(new[] { left, right }));
    }

    /// <summary>
    /// 解析比较表达式（等于、不等于、大于、小于、大于等于、小于等于）
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseComparison<T>(BinaryExpression binary, ComparisonType comparisonType)
    {
        // 提取字段路径和值
        var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(binary.Left, binary.Right);
        
        if (string.IsNullOrEmpty(fieldPath) || value == null)
        {
            return null;
        }

        // 对于精确匹配（等于、不等于）和范围查询，需要判断是否使用 keyword
        // 使用从 ExtractFieldFromExpression 返回的 lastProperty，确保嵌套字段的属性信息正确
        var finalFieldPath = comparisonType == ComparisonType.Equals || comparisonType == ComparisonType.NotEquals
            ? GetFieldPathForExactMatch(fieldPath, lastProperty)
            : GetFieldPathForRangeQuery(fieldPath, lastProperty, value);

        return BuildComparisonQuery<T>(finalFieldPath, nestedPath, comparisonType, value);
    }

    /// <summary>
    /// 解析方法调用（Contains, StartsWith, EndsWith 等）
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseMethodCall<T>(MethodCallExpression methodCall)
    {
        var methodName = methodCall.Method.Name;

        // Contains 方法
        if (methodName == "Contains")
        {
            return ParseContains<T>(methodCall);
        }

        // StartsWith 方法
        if (methodName == "StartsWith")
        {
            return ParseStartsWith<T>(methodCall);
        }

        // EndsWith 方法
        if (methodName == "EndsWith")
        {
            return ParseEndsWith<T>(methodCall);
        }

        // Any 方法（用于集合）
        if (methodName == "Any")
        {
            return ParseAny<T>(methodCall);
        }

        return null;
    }

    /// <summary>
    /// 解析 Contains 方法
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseContains<T>(MethodCallExpression methodCall)
    {
        // 支持两种形式：
        // 1. field.Contains(value) - 字符串包含
        // 2. collection.Contains(field) - 集合包含值

        if (methodCall.Object != null)
        {
            // 形式1：field.Contains(value)
            var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
            if (!string.IsNullOrEmpty(fieldPath) && value != null)
            {
                // 根据字段类型选择合适的查询方式
                // text 类型字段使用 match 查询（支持分词），keyword 类型字段使用 wildcard 查询
                if (IsKeywordField(lastProperty))
                {
                    // keyword 类型字段使用 wildcard 查询
                    return BuildWildcardQuery<T>(fieldPath, nestedPath, $"*{value}*");
                }
                else
                {
                    // text 类型字段使用 match 查询（支持全文搜索和分词）
                    return BuildMatchQuery<T>(fieldPath, nestedPath, value.ToString() ?? string.Empty);
                }
            }
        }
        else if (methodCall.Arguments.Count == 2)
        {
            // 形式2：collection.Contains(field)
            var collection = EvaluateExpression(methodCall.Arguments[0]);
            var (fieldPath, nestedPath, lastProperty) = ExtractFieldFromExpression<T>(methodCall.Arguments[1]);
            
            if (!string.IsNullOrEmpty(fieldPath) && collection is IEnumerable enumerable)
            {
                // 对于精确匹配（Terms 查询），需要判断是否使用 keyword
                var finalFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                return BuildTermsQuery<T>(finalFieldPath, nestedPath, enumerable);
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 StartsWith 方法
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseStartsWith<T>(MethodCallExpression methodCall)
    {
        if (methodCall.Object == null || methodCall.Arguments.Count == 0)
        {
            return null;
        }

        var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
        if (!string.IsNullOrEmpty(fieldPath) && value != null)
        {
            // 根据字段类型选择合适的查询方式
            if (IsKeywordField(lastProperty))
            {
                // keyword 类型字段使用 wildcard 查询
                return BuildWildcardQuery<T>(fieldPath, nestedPath, $"{value}*");
            }
            else
            {
                // text 类型字段使用 match_phrase_prefix 查询（匹配以指定值开头的短语）
                return BuildMatchPhrasePrefixQuery<T>(fieldPath, nestedPath, value.ToString() ?? string.Empty);
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 EndsWith 方法
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseEndsWith<T>(MethodCallExpression methodCall)
    {
        if (methodCall.Object == null || methodCall.Arguments.Count == 0)
        {
            return null;
        }

        var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
        if (!string.IsNullOrEmpty(fieldPath) && value != null)
        {
            // 根据字段类型选择合适的查询方式
            if (IsKeywordField(lastProperty))
            {
                // keyword 类型字段使用 wildcard 查询
                return BuildWildcardQuery<T>(fieldPath, nestedPath, $"*{value}");
            }
            else
            {
                // text 类型字段的 EndsWith 查询比较复杂，可以使用 wildcard 查询在 .keyword 子字段上
                // 或者使用 match 查询配合正则表达式，但最简单的方式是使用 .keyword 子字段的 wildcard 查询
                // 对于 text 类型字段，EndsWith 应该使用 .keyword 子字段进行 wildcard 查询
                var keywordFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                return BuildWildcardQuery<T>(keywordFieldPath, nestedPath, $"*{value}");
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 Any 方法（用于集合查询）
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseAny<T>(MethodCallExpression methodCall)
    {
        // 暂时不支持，可以后续扩展
        return null;
    }

    /// <summary>
    /// 解析成员访问表达式
    /// 主要用于处理布尔字段的直接引用（如 x => x.BoolField）
    /// 当表达式是布尔类型的成员访问时，将其转换为 field == true 的查询
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseMemberExpression<T>(MemberExpression member)
    {
        // 检查成员的类型是否为布尔类型
        Type? memberType = null;
        
        if (member.Member is PropertyInfo propertyInfo)
        {
            memberType = propertyInfo.PropertyType;
        }
        else if (member.Member is FieldInfo fieldInfo)
        {
            memberType = fieldInfo.FieldType;
        }

        // 如果不是布尔类型，不支持直接引用
        if (memberType == null)
        {
            return null;
        }

        // 处理可空布尔类型
        var underlyingType = memberType;
        if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            underlyingType = memberType.GetGenericArguments()[0];
        }

        // 只有布尔类型才支持直接引用
        if (underlyingType != typeof(bool))
        {
            return null;
        }

        // 提取字段路径
        var (fieldPath, nestedPath, lastProperty) = ExtractFieldFromExpression<T>(member);
        if (string.IsNullOrEmpty(fieldPath))
        {
            return null;
        }

        // 对于布尔类型，不需要 keyword 后缀
        // 构建 field == true 的查询
        return BuildComparisonQuery<T>(fieldPath, nestedPath, ComparisonType.Equals, true);
    }

    /// <summary>
    /// 从表达式中提取字段路径和值
    /// </summary>
    private static (string? fieldPath, string? nestedPath, PropertyInfo? lastProperty, object? value) ExtractFieldAndValue<T>(
        Expression left, Expression right)
    {
        // 尝试从左边提取字段，从右边提取值
        var (fieldPath, nestedPath, lastProperty) = ExtractFieldFromExpression<T>(left);
        var value = EvaluateExpression(right);

        if (!string.IsNullOrEmpty(fieldPath))
        {
            return (fieldPath, nestedPath, lastProperty, value);
        }

        // 如果左边不是字段，尝试从右边提取字段，从左边提取值
        (fieldPath, nestedPath, lastProperty) = ExtractFieldFromExpression<T>(right);
        value = EvaluateExpression(left);

        return (fieldPath, nestedPath, lastProperty, value);
    }

    /// <summary>
    /// 从表达式中提取字段路径
    /// 返回字段路径、嵌套路径和最后一个属性的 PropertyInfo（用于获取特性信息）
    /// 如果字段配置了 IndexName，则使用配置的索引名称
    /// </summary>
    private static (string? fieldPath, string? nestedPath, PropertyInfo? lastProperty) ExtractFieldFromExpression<T>(Expression expression)
    {
        var path = new List<string>();
        var properties = new List<PropertyInfo>();
        var nestedPath = (string?)null;
        var current = expression;

        // 处理类型转换
        if (current is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            current = unary.Operand;
        }

        // 提取成员访问路径
        while (current is MemberExpression member)
        {
            // 如果是属性，保存 PropertyInfo 并获取索引名称
            if (member.Member is PropertyInfo propertyInfo)
            {
                properties.Insert(0, propertyInfo);
                
                // 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
                // 如果没有配置 FieldName，需要将 PascalCase 转换为 camelCase
                // 因为 Elasticsearch 客户端在序列化文档时会自动将属性名转换为 camelCase
                var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();
                var fieldName = !string.IsNullOrEmpty(esFieldAttr?.FieldName) 
                    ? esFieldAttr.FieldName 
                    : ToCamelCase(propertyInfo.Name);
                
                path.Insert(0, fieldName);
            }
            else
            {
                // 非属性成员（如字段），将 PascalCase 转换为 camelCase
                path.Insert(0, ToCamelCase(member.Member.Name));
            }
            
            current = member.Expression;

            // 检查是否是嵌套字段（需要 Nested 查询）
            // 这里可以根据业务规则判断，比如某些字段需要 Nested 查询
            // 暂时不自动判断，后续可以通过特性或配置来标记
        }

        if (path.Count == 0)
        {
            return (null, null, null);
        }

        var fieldPath = string.Join(".", path);

        // 如果路径包含多个部分，需要检查第一个部分是否是嵌套类型
        // 例如：address.city，如果 address 是嵌套类型，则 address 是嵌套路径，city 是字段路径
        // 注意：在嵌套查询中，字段路径应该是相对于嵌套路径的，例如：
        // - 如果查询 x.Address.City，则 nestedPath = "address"，fieldPath = "city"
        // - 在构建嵌套查询时，会使用 nestedPath 作为 path，fieldPath 作为嵌套查询内的字段路径
        if (path.Count > 1 && properties.Count > 0)
        {
            // 获取第一个属性（最外层的属性）
            var firstProperty = properties[0];
            var firstPropertyType = firstProperty.PropertyType;
            
            // 检查第一个属性是否是嵌套类型
            // 首先检查 EsFieldAttribute.IsNested 特性
            var esFieldAttr = firstProperty.GetCustomAttribute<EsFieldAttribute>();
            bool isNested = esFieldAttr?.IsNested ?? IsNestedType(firstPropertyType);
            
            if (isNested)
            {
                // 第一个属性是嵌套类型，将其作为嵌套路径
                // 注意：path[0] 已经是 camelCase 格式的字段名（例如 "address"）
                nestedPath = path[0];
                // 剩余部分作为字段路径（相对于嵌套路径）
                // 注意：path.Skip(1) 中的字段名也已经是 camelCase 格式（例如 "city"）
                fieldPath = string.Join(".", path.Skip(1));
            }
        }

        // 返回最后一个属性的 PropertyInfo（用于获取字段特性）
        var lastProperty = properties.Count > 0 ? properties[properties.Count - 1] : null;

        return (fieldPath, nestedPath, lastProperty);
    }

    /// <summary>
    /// 判断是否为嵌套类型
    /// 引用类型（除了 string）且不是集合类型，会被识别为嵌套文档
    /// 与 IndexMappingBuilder.IsNestedType 逻辑保持一致
    /// </summary>
    private static bool IsNestedType(Type type)
    {
        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        // 基本类型不是嵌套
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(decimal))
        {
            return false;
        }

        // 引用类型且不是集合，视为嵌套
        return !type.IsValueType && !IsCollectionType(type);
    }

    /// <summary>
    /// 判断是否为集合类型
    /// </summary>
    private static bool IsCollectionType(Type type)
    {
        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    /// <summary>
    /// 计算表达式的值（常量或变量）
    /// </summary>
    private static object? EvaluateExpression(Expression expression)
    {
        // 处理常量表达式
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        // 处理成员访问表达式（访问变量或属性）
        if (expression is MemberExpression member)
        {
            // 如果是访问闭包变量，需要编译表达式来获取值
            if (member.Expression is ConstantExpression constantExpr)
            {
                var obj = constantExpr.Value;
                if (obj != null)
                {
                    if (member.Member is FieldInfo fieldInfo)
                    {
                        return fieldInfo.GetValue(obj);
                    }
                    if (member.Member is PropertyInfo propertyInfo)
                    {
                        return propertyInfo.GetValue(obj);
                    }
                }
            }
        }

        // 对于复杂表达式，尝试编译并执行
        try
        {
            var lambda = Expression.Lambda(expression);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 构建比较查询（等于、不等于、大于、小于、大于等于、小于等于）
    /// </summary>
    /// <param name="fieldPath">字段路径（相对于嵌套路径，如果存在嵌套路径）</param>
    /// <param name="nestedPath">嵌套路径（如果字段在嵌套文档中）</param>
    /// <param name="comparisonType">比较类型</param>
    /// <param name="value">比较值</param>
    /// <remarks>
    /// 对于嵌套查询：
    /// - nestedPath 是嵌套文档的路径（例如 "address"）
    /// - fieldPath 是嵌套文档内的字段路径（例如 "city.keyword"）
    /// - 在构建嵌套查询时，字段路径需要包含完整的嵌套路径（例如 "address.city.keyword"）
    /// - 这是因为 Elasticsearch 嵌套查询中的字段路径需要是完整路径，而不是相对于嵌套路径的相对路径
    /// </remarks>
    private static Action<QueryDescriptor<T>> BuildComparisonQuery<T>(
        string fieldPath, string? nestedPath, ComparisonType comparisonType, object value)
    {
        return query =>
        {
            // 处理嵌套查询
            if (!string.IsNullOrEmpty(nestedPath))
            {
                // 构建嵌套查询
                // nestedPath 是嵌套文档的路径（例如 "address"）
                // fieldPath 是嵌套文档内的字段路径（例如 "city.keyword"）
                // 在嵌套查询中，字段路径需要包含完整的嵌套路径（例如 "address.city.keyword"）
                var fullFieldPath = $"{nestedPath}.{fieldPath}";
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => ApplyComparisonToQuery(nq, fullFieldPath, comparisonType, value))
                );
            }
            else
            {
                // 普通查询（非嵌套）
                ApplyComparisonToQuery(query, fieldPath, comparisonType, value);
            }
        };
    }

    /// <summary>
    /// 应用比较查询到 QueryDescriptor
    /// </summary>
    private static void ApplyComparisonToQuery<T>(
        QueryDescriptor<T> query, string fieldPath, ComparisonType comparisonType, object value)
    {
        switch (comparisonType)
        {
            case ComparisonType.Equals:
                ApplyEqualsQuery(query, fieldPath, value);
                break;

            case ComparisonType.NotEquals:
                query.Bool(b => b.MustNot(mn => ApplyEqualsQuery(mn, fieldPath, value)));
                break;

            case ComparisonType.GreaterThan:
            case ComparisonType.GreaterThanOrEqual:
            case ComparisonType.LessThan:
            case ComparisonType.LessThanOrEqual:
                ApplyRangeQuery(query, fieldPath, comparisonType, value);
                break;
        }
    }

    /// <summary>
    /// 从表达式中获取最后一个属性的 PropertyInfo
    /// </summary>
    private static PropertyInfo? GetLastPropertyFromExpression<T>(Expression expression)
    {
        var current = expression;

        // 处理类型转换
        if (current is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            current = unary.Operand;
        }

        // 提取成员访问路径，找到最后一个属性
        PropertyInfo? lastProperty = null;
        while (current is MemberExpression member)
        {
            if (member.Member is PropertyInfo propertyInfo)
            {
                lastProperty = propertyInfo;
            }
            current = member.Expression;
        }

        return lastProperty;
    }

    /// <summary>
    /// 判断字段是否需要使用 .keyword 后缀（用于精确匹配和排序）
    /// 根据索引构建规则：
    /// 1. 如果 FieldType == "keyword"，字段直接是 keyword 类型，不需要添加 .keyword
    /// 2. 如果 FieldType == "text" 或未指定，且 NeedKeyword == true，字段是 text 类型且有 .keyword 子字段，需要添加 .keyword
    /// 3. 如果字段类型是 string，默认需要 .keyword（除非明确指定不需要）
    /// </summary>
    internal static string GetFieldPathForExactMatch(string fieldPath, PropertyInfo? propertyInfo)
    {
        // 如果不是字符串类型，不需要 keyword
        if (propertyInfo == null)
        {
            return fieldPath;
        }

        var propertyType = propertyInfo.PropertyType;
        // 处理可空类型
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            propertyType = propertyType.GetGenericArguments()[0];
        }

        // 只有字符串类型才可能需要 keyword
        if (propertyType != typeof(string))
        {
            return fieldPath;
        }

        // 获取字段特性
        var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();

        // 如果 FieldType 明确指定为 "keyword"，字段本身就是 keyword 类型，不需要添加 .keyword
        if (esFieldAttr?.FieldType?.ToLower() == "keyword")
        {
            return fieldPath;
        }

        // 如果 FieldType 明确指定为 "text" 或未指定（默认是 text），且 NeedKeyword == true（默认也是 true），需要添加 .keyword
        var fieldType = esFieldAttr?.FieldType?.ToLower() ?? "text";
        var needKeyword = esFieldAttr?.NeedKeyword ?? true;

        if (fieldType == "text" && needKeyword)
        {
            return $"{fieldPath}.keyword";
        }

        // 默认情况下，string 类型字段如果是 text 类型，需要添加 .keyword
        return fieldPath;
    }

    /// <summary>
    /// 判断字段是否需要使用 .keyword 后缀（用于范围查询）
    /// 范围查询通常也需要精确匹配，所以逻辑与精确匹配相同
    /// </summary>
    private static string GetFieldPathForRangeQuery(string fieldPath, PropertyInfo? propertyInfo, object value)
    {
        // 范围查询通常用于数字和日期类型，字符串类型的范围查询也需要 keyword
        if (propertyInfo != null)
        {
            var propertyType = propertyInfo.PropertyType;
            // 处理可空类型
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                propertyType = propertyType.GetGenericArguments()[0];
            }

            // 字符串类型的范围查询也需要 keyword
            if (propertyType == typeof(string) || value is string)
            {
                return GetFieldPathForExactMatch(fieldPath, propertyInfo);
            }
        }

        return fieldPath;
    }

    /// <summary>
    /// 应用等值查询
    /// </summary>
    private static void ApplyEqualsQuery<T>(QueryDescriptor<T> query, string fieldPath, object value)
    {
        var valueType = value.GetType();

        if (valueType == typeof(DateTime))
        {
            var time = ((DateTime)value).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            query.Term(t => t.Field(fieldPath).Value(time));
        }
        else if (valueType == typeof(DateTimeOffset))
        {
            // DateTimeOffset 使用 ISO 8601 格式（包含时区信息）
            // 格式：yyyy-MM-ddTHH:mm:ss.fffzzz（例如：2024-01-14T10:00:00.000+08:00）
            var timeOffset = ((DateTimeOffset)value).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
            query.Term(t => t.Field(fieldPath).Value(timeOffset));
        }
        else if (IsNumericType(valueType))
        {
            query.Term(t => t.Field(fieldPath).Value(Convert.ToDouble(value)));
        }
        else if (valueType == typeof(bool))
        {
            query.Term(t => t.Field(fieldPath).Value(Boolean((bool)value)));
        }
        else if (valueType == typeof(Guid))
        {
            query.Term(t => t.Field(fieldPath).Value(value?.ToString() ?? string.Empty));
        }
        else
        {
            var stringValue = value?.ToString() ?? string.Empty;
            query.Term(t => t.Field(fieldPath).Value(stringValue));
        }
    }

    /// <summary>
    /// 应用范围查询（大于、小于、大于等于、小于等于）
    /// </summary>
    private static void ApplyRangeQuery<T>(
        QueryDescriptor<T> query, string fieldPath, ComparisonType comparisonType, object value)
    {
        var valueType = value.GetType();

        // 日期时间类型
        if (value is DateTime dateTime)
        {
            query.Range(r => r
                .DateRange(dr => dr
                    .Field(fieldPath)
                    .ApplyRangeComparison(comparisonType, dateTime)
                )
            );
            return;
        }

        // DateTimeOffset 类型
        // 将 DateTimeOffset 转换为 DateTime（使用 UTC 时间）用于范围查询
        // 因为 Elasticsearch 的日期范围查询接受 DateTime 类型
        if (value is DateTimeOffset dateTimeOffset)
        {
            // 将 DateTimeOffset 转换为 UTC 的 DateTime
            var utcDateTime = dateTimeOffset.UtcDateTime;
            query.Range(r => r
                .DateRange(dr => dr
                    .Field(fieldPath)
                    .ApplyRangeComparison(comparisonType, utcDateTime)
                )
            );
            return;
        }

        // 数字类型
        if (IsNumericType(valueType))
        {
            var numValue = Convert.ToDouble(value);
            query.Range(r => r
                .NumberRange(nr => nr
                    .Field(fieldPath)
                    .ApplyRangeComparison(comparisonType, numValue)
                )
            );
            return;
        }

        throw new ArgumentException($"范围查询不支持类型: {valueType.Name}");
    }

    /// <summary>
    /// 判断字段是否为 keyword 类型
    /// </summary>
    private static bool IsKeywordField(PropertyInfo? propertyInfo)
    {
        if (propertyInfo == null)
        {
            return false;
        }

        var propertyType = propertyInfo.PropertyType;
        // 处理可空类型
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            propertyType = propertyType.GetGenericArguments()[0];
        }

        // 只有字符串类型才可能是 keyword
        if (propertyType != typeof(string))
        {
            return false;
        }

        // 获取字段特性
        var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();

        // 如果 FieldType 明确指定为 "keyword"，则是 keyword 类型
        return esFieldAttr?.FieldType?.ToLower() == "keyword";
    }

    /// <summary>
    /// 构建 Match 查询（用于 text 类型字段的全文搜索）
    /// Match 查询会对查询文本进行分词，然后匹配分词后的词项
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildMatchQuery<T>(
        string fieldPath, string? nestedPath, string queryText)
    {
        return query =>
        {
            if (!string.IsNullOrEmpty(nestedPath))
            {
                // 在嵌套查询中，字段路径需要包含完整的嵌套路径
                var fullFieldPath = $"{nestedPath}.{fieldPath}";
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => nq.Match(m => m.Field(fullFieldPath).Query(queryText)))
                );
            }
            else
            {
                query.Match(m => m.Field(fieldPath).Query(queryText));
            }
        };
    }

    /// <summary>
    /// 构建 Match Phrase Prefix 查询（用于 text 类型字段的前缀匹配）
    /// Match Phrase Prefix 查询会匹配以指定短语开头的文档
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildMatchPhrasePrefixQuery<T>(
        string fieldPath, string? nestedPath, string queryText)
    {
        return query =>
        {
            if (!string.IsNullOrEmpty(nestedPath))
            {
                // 在嵌套查询中，字段路径需要包含完整的嵌套路径
                var fullFieldPath = $"{nestedPath}.{fieldPath}";
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => nq.MatchPhrasePrefix(m => m.Field(fullFieldPath).Query(queryText)))
                );
            }
            else
            {
                query.MatchPhrasePrefix(m => m.Field(fieldPath).Query(queryText));
            }
        };
    }

    /// <summary>
    /// 构建 Wildcard 查询（用于 keyword 类型字段的模式匹配）
    /// 注意：Wildcard 查询只能用于 keyword 类型字段，不能用于 text 类型字段
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildWildcardQuery<T>(
        string fieldPath, string? nestedPath, string pattern)
    {
        return query =>
        {
            if (!string.IsNullOrEmpty(nestedPath))
            {
                // 在嵌套查询中，字段路径需要包含完整的嵌套路径
                var fullFieldPath = $"{nestedPath}.{fieldPath}";
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => nq.Wildcard(w => w.Field(fullFieldPath).Value(pattern)))
                );
            }
            else
            {
                query.Wildcard(w => w.Field(fieldPath).Value(pattern));
            }
        };
    }

    /// <summary>
    /// 构建 Terms 查询（用于 In 查询）
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildTermsQuery<T>(
        string fieldPath, string? nestedPath, IEnumerable values)
    {
        var valueList = values.Cast<object>().ToList();
        if (!valueList.Any())
        {
            return _ => { };
        }

        var fieldValues = ConvertToFieldValues(valueList);
        var termsQueryField = new TermsQueryField(fieldValues);

        return query =>
        {
            if (!string.IsNullOrEmpty(nestedPath))
            {
                // 在嵌套查询中，字段路径需要包含完整的嵌套路径
                var fullFieldPath = $"{nestedPath}.{fieldPath}";
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => nq.Terms(ts => ts.Field(fullFieldPath).Terms(termsQueryField)))
                );
            }
            else
            {
                query.Terms(ts => ts.Field(fieldPath).Terms(termsQueryField));
            }
        };
    }

    /// <summary>
    /// 将值列表转换为 FieldValue 数组
    /// </summary>
    private static Elastic.Clients.Elasticsearch.FieldValue[] ConvertToFieldValues(List<object> values)
    {
        if (!values.Any())
        {
            return Array.Empty<Elastic.Clients.Elasticsearch.FieldValue>();
        }

        var firstValue = values.First();
        var valueType = firstValue.GetType();

        // 数字类型
        if (IsNumericType(valueType))
        {
            return values.Select(v => Double(Convert.ToDouble(v))).ToArray();
        }

        // 字符串类型
        if (valueType == typeof(string))
        {
            return values.Select(v => String(v?.ToString() ?? string.Empty)).ToArray();
        }

        // Guid 类型
        if (valueType == typeof(Guid))
        {
            return values.Select(v => String(v?.ToString() ?? string.Empty)).ToArray();
        }

        // 日期时间类型
        if (valueType == typeof(DateTime))
        {
            return values.Select(v => String(((DateTime)v).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))).ToArray();
        }

        // DateTimeOffset 类型
        if (valueType == typeof(DateTimeOffset))
        {
            // DateTimeOffset 使用 ISO 8601 格式（包含时区信息）
            // 格式：yyyy-MM-ddTHH:mm:ss.fffzzz（例如：2024-01-14T10:00:00.000+08:00）
            return values.Select(v => String(((DateTimeOffset)v).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"))).ToArray();
        }

        // 默认转换为字符串
        return values.Select(v => String(v?.ToString() ?? string.Empty)).ToArray();
    }

    /// <summary>
    /// 判断是否为数字类型
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    /// <summary>
    /// 将 PascalCase 转换为 camelCase
    /// 例如：NullableBoolField -> nullableBoolField
    /// 用于匹配 Elasticsearch 客户端序列化时的字段命名约定
    /// Elasticsearch 客户端在序列化文档时会自动将 C# 的 PascalCase 属性名转换为 camelCase
    /// 因此查询时也需要使用 camelCase 字段名才能正确匹配
    /// </summary>
    /// <param name="pascalCase">PascalCase 格式的字符串</param>
    /// <returns>camelCase 格式的字符串</returns>
    private static string ToCamelCase(string pascalCase)
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

/// <summary>
/// 范围查询扩展方法
/// </summary>
internal static class RangeQueryExtensions
{
    /// <summary>
    /// 应用范围比较到日期范围查询描述符
    /// </summary>
    public static DateRangeQueryDescriptor<T> ApplyRangeComparison<T>(
        this DateRangeQueryDescriptor<T> descriptor, ComparisonType comparisonType, DateTime value)
    {
        return comparisonType switch
        {
            ComparisonType.GreaterThan => descriptor.Gt(value),
            ComparisonType.GreaterThanOrEqual => descriptor.Gte(value),
            ComparisonType.LessThan => descriptor.Lt(value),
            ComparisonType.LessThanOrEqual => descriptor.Lte(value),
            _ => descriptor
        };
    }

    /// <summary>
    /// 应用范围比较到数字范围查询描述符
    /// </summary>
    public static NumberRangeQueryDescriptor<T> ApplyRangeComparison<T>(
        this NumberRangeQueryDescriptor<T> descriptor, ComparisonType comparisonType, double value)
    {
        return comparisonType switch
        {
            ComparisonType.GreaterThan => descriptor.Gt(value),
            ComparisonType.GreaterThanOrEqual => descriptor.Gte(value),
            ComparisonType.LessThan => descriptor.Lt(value),
            ComparisonType.LessThanOrEqual => descriptor.Lte(value),
            _ => descriptor
        };
    }
}

/// <summary>
/// 比较类型枚举
/// </summary>
internal enum ComparisonType
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}



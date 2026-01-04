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
        var (fieldPath, nestedPath, value) = ExtractFieldAndValue<T>(binary.Left, binary.Right);
        
        if (string.IsNullOrEmpty(fieldPath) || value == null)
        {
            return null;
        }

        // 获取字段的 PropertyInfo（用于判断是否需要 keyword）
        PropertyInfo? lastProperty = null;
        if (binary.Left is MemberExpression leftMember)
        {
            lastProperty = GetLastPropertyFromExpression<T>(binary.Left);
        }
        else if (binary.Right is MemberExpression rightMember)
        {
            lastProperty = GetLastPropertyFromExpression<T>(binary.Right);
        }

        // 对于精确匹配（等于、不等于）和范围查询，需要判断是否使用 keyword
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
            var (fieldPath, nestedPath, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
            if (!string.IsNullOrEmpty(fieldPath) && value != null)
            {
                return BuildWildcardQuery<T>(fieldPath, nestedPath, $"*{value}*");
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

        var (fieldPath, nestedPath, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
        if (!string.IsNullOrEmpty(fieldPath) && value != null)
        {
            return BuildWildcardQuery<T>(fieldPath, nestedPath, $"{value}*");
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

        var (fieldPath, nestedPath, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
        if (!string.IsNullOrEmpty(fieldPath) && value != null)
        {
            return BuildWildcardQuery<T>(fieldPath, nestedPath, $"*{value}");
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
    private static (string? fieldPath, string? nestedPath, object? value) ExtractFieldAndValue<T>(
        Expression left, Expression right)
    {
        // 尝试从左边提取字段，从右边提取值
        var (fieldPath, nestedPath, _) = ExtractFieldFromExpression<T>(left);
        var value = EvaluateExpression(right);

        if (!string.IsNullOrEmpty(fieldPath))
        {
            return (fieldPath, nestedPath, value);
        }

        // 如果左边不是字段，尝试从右边提取字段，从左边提取值
        (fieldPath, nestedPath, _) = ExtractFieldFromExpression<T>(right);
        value = EvaluateExpression(left);

        return (fieldPath, nestedPath, value);
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
                var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();
                var fieldName = !string.IsNullOrEmpty(esFieldAttr?.FieldName) 
                    ? esFieldAttr.FieldName 
                    : propertyInfo.Name;
                
                path.Insert(0, fieldName);
            }
            else
            {
                // 非属性成员（如字段），直接使用名称
                path.Insert(0, member.Member.Name);
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

        // 如果路径包含多个部分，第一个部分可能是嵌套路径
        // 例如：order.paymentStatus，order 是嵌套路径
        if (path.Count > 1)
        {
            nestedPath = path[0];
        }

        // 返回最后一个属性的 PropertyInfo（用于获取字段特性）
        var lastProperty = properties.Count > 0 ? properties[properties.Count - 1] : null;

        return (fieldPath, nestedPath, lastProperty);
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
    private static Action<QueryDescriptor<T>> BuildComparisonQuery<T>(
        string fieldPath, string? nestedPath, ComparisonType comparisonType, object value)
    {
        return query =>
        {
            // 处理嵌套查询
            if (!string.IsNullOrEmpty(nestedPath))
            {
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => ApplyComparisonToQuery(nq, fieldPath, comparisonType, value))
                );
            }
            else
            {
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
    /// 构建 Wildcard 查询（用于 Contains, StartsWith, EndsWith）
    /// 注意：Wildcard 查询使用 text 字段本身，不需要 .keyword 后缀
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildWildcardQuery<T>(
        string fieldPath, string? nestedPath, string pattern)
    {
        return query =>
        {
            if (!string.IsNullOrEmpty(nestedPath))
            {
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => nq.Wildcard(w => w.Field(fieldPath).Value(pattern)))
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

        return query =>
        {
            if (!string.IsNullOrEmpty(nestedPath))
            {
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => nq.Terms(ts => ts.Field(fieldPath).Terms(new TermsQueryField(fieldValues))))
                );
            }
            else
            {
                query.Terms(ts => ts.Field(fieldPath).Terms(new TermsQueryField(fieldValues)));
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


using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Elastic.Clients.Elasticsearch.QueryDsl;
using static Elastic.Clients.Elasticsearch.FieldValue;

namespace Adi.ElasticSugar.Core;

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

        return BuildComparisonQuery<T>(fieldPath, nestedPath, comparisonType, value);
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
            var (fieldPath, nestedPath) = ExtractFieldFromExpression<T>(methodCall.Arguments[1]);
            
            if (!string.IsNullOrEmpty(fieldPath) && collection is IEnumerable enumerable)
            {
                return BuildTermsQuery<T>(fieldPath, nestedPath, enumerable);
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
    /// 从表达式中提取字段路径和值
    /// </summary>
    private static (string? fieldPath, string? nestedPath, object? value) ExtractFieldAndValue<T>(
        Expression left, Expression right)
    {
        // 尝试从左边提取字段，从右边提取值
        var (fieldPath, nestedPath) = ExtractFieldFromExpression<T>(left);
        var value = EvaluateExpression(right);

        if (!string.IsNullOrEmpty(fieldPath))
        {
            return (fieldPath, nestedPath, value);
        }

        // 如果左边不是字段，尝试从右边提取字段，从左边提取值
        (fieldPath, nestedPath) = ExtractFieldFromExpression<T>(right);
        value = EvaluateExpression(left);

        return (fieldPath, nestedPath, value);
    }

    /// <summary>
    /// 从表达式中提取字段路径
    /// </summary>
    private static (string? fieldPath, string? nestedPath) ExtractFieldFromExpression<T>(Expression expression)
    {
        var path = new List<string>();
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
            path.Insert(0, member.Member.Name);
            current = member.Expression;

            // 检查是否是嵌套字段（需要 Nested 查询）
            // 这里可以根据业务规则判断，比如某些字段需要 Nested 查询
            // 暂时不自动判断，后续可以通过特性或配置来标记
        }

        if (path.Count == 0)
        {
            return (null, null);
        }

        var fieldPath = string.Join(".", path);

        // 如果路径包含多个部分，第一个部分可能是嵌套路径
        // 例如：order.paymentStatus，order 是嵌套路径
        if (path.Count > 1)
        {
            nestedPath = path[0];
        }

        return (fieldPath, nestedPath);
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
            var dateRange = new DateRangeQueryDescriptor<T>(fieldPath);
            dateRange = comparisonType switch
            {
                ComparisonType.GreaterThan => dateRange.Gt(dateTime),
                ComparisonType.GreaterThanOrEqual => dateRange.Gte(dateTime),
                ComparisonType.LessThan => dateRange.Lt(dateTime),
                ComparisonType.LessThanOrEqual => dateRange.Lte(dateTime),
                _ => dateRange
            };
            query.Range(r => r.DateRange(dateRange));
            return;
        }

        // 数字类型
        if (IsNumericType(valueType))
        {
            var numValue = Convert.ToDouble(value);
            var numRange = new NumberRangeQueryDescriptor<T>(fieldPath);
            numRange = comparisonType switch
            {
                ComparisonType.GreaterThan => numRange.Gt(numValue),
                ComparisonType.GreaterThanOrEqual => numRange.Gte(numValue),
                ComparisonType.LessThan => numRange.Lt(numValue),
                ComparisonType.LessThanOrEqual => numRange.Lte(numValue),
                _ => numRange
            };
            query.Range(r => r.NumberRange(numRange));
            return;
        }

        throw new ArgumentException($"范围查询不支持类型: {valueType.Name}");
    }

    /// <summary>
    /// 构建 Wildcard 查询（用于 Contains, StartsWith, EndsWith）
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
                    .Query(nq => nq.Terms(ts => ts.Field(fieldPath).Terms(new TermsQueryField(fieldValues.ToList()))))
                );
            }
            else
            {
                query.Terms(ts => ts.Field(fieldPath).Terms(new TermsQueryField(fieldValues.ToList())));
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


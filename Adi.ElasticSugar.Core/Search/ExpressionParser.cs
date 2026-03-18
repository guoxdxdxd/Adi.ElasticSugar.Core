using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Adi.ElasticSugar.Core.Models;
using Adi.ElasticSugar.Core.Utils;
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
    /// 使用 DNF（析取范式）格式处理表达式：(a&&b&&c)||(d&&e&&f)||(g.h&&i)
    /// 顶层是 OR 关系，每个 OR 分支是一个 AND 条件组
    /// 相同嵌套路径的条件会合并到同一个 nested 查询中
    /// </summary>
    public static Action<QueryDescriptor<T>>? ParseExpression<T>(Expression<Func<T, bool>> expression)
    {
        if (expression == null)
        {
            return null;
        }

        // 步骤1：将表达式转换为布尔树（不做 DNF 展开，避免 OR 组爆炸）
        var boolNode = ConvertToBoolNode<T>(expression.Body);
        if (boolNode == null)
        {
            return null;
        }

        // 步骤2：根据布尔树生成查询
        // 核心目标：在保持语义正确的前提下，尽量把相同 nestedPath 的条件合并为单个 nested 查询
        return BuildQueryFromBoolNode<T>(boolNode);
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
            // 逻辑 AND - 使用 DNF 格式处理
            ExpressionType.AndAlso => ParseAndExpression<T>(binary),
            
            // 逻辑 OR - 使用 DNF 格式处理
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
    /// 使用 DNF 格式处理，确保相同嵌套路径的条件合并到同一个 nested 查询中
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseAndExpression<T>(BinaryExpression binary)
    {
        // 将表达式转换为布尔树（不做 DNF 展开）
        var boolNode = ConvertToBoolNode<T>(binary);
        if (boolNode == null)
        {
            return null;
        }

        // 生成查询
        return BuildQueryFromBoolNode<T>(boolNode);
    }

    /// <summary>
    /// 解析 OR 表达式
    /// 使用 DNF 格式处理，确保相同嵌套路径的条件合并到同一个 nested 查询中
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseOrExpression<T>(BinaryExpression binary)
    {
        // 将表达式转换为布尔树（不做 DNF 展开）
        var boolNode = ConvertToBoolNode<T>(binary);
        if (boolNode == null)
        {
            return null;
        }

        // 生成查询
        return BuildQueryFromBoolNode<T>(boolNode);
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

        return BuildComparisonQuery<T>(finalFieldPath, nestedPath, comparisonType, value, lastProperty);
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
                // 字符串 Contains 的语义是“子串匹配”，优先走 keyword 子字段以避免分词导致的误命中
                // 规则说明：
                // - 如果模型/映射允许 .keyword（GetFieldPathForExactMatch 会返回带 .keyword 的路径），使用 wildcard 进行子串匹配
                // - 如果没有 .keyword 子字段（NeedKeyword=false 或字段本身非 text），回退到 match（保留全文检索能力）
                var exactMatchFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                var useKeywordSubField = !string.Equals(exactMatchFieldPath, fieldPath, StringComparison.Ordinal);
                
                if (useKeywordSubField || IsKeywordField(lastProperty))
                {
                    // keyword 类型字段使用 wildcard 查询，匹配精确子串
                    return BuildWildcardQuery<T>(exactMatchFieldPath, nestedPath, $"*{value}*");
                }

                // text 类型字段使用 match 查询（支持全文搜索和分词）
                return BuildMatchQuery<T>(fieldPath, nestedPath, value.ToString() ?? string.Empty);
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
                return BuildTermsQuery<T>(finalFieldPath, nestedPath, enumerable, lastProperty);
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
            // StartsWith 优先走 keyword 子字段，避免分词导致误匹配
            var exactMatchFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
            var useKeywordSubField = !string.Equals(exactMatchFieldPath, fieldPath, StringComparison.Ordinal);
            
            if (useKeywordSubField || IsKeywordField(lastProperty))
            {
                // keyword 类型字段使用 wildcard 查询
                return BuildWildcardQuery<T>(exactMatchFieldPath, nestedPath, $"{value}*");
            }

            // text 类型字段使用 match_phrase_prefix 查询（匹配以指定值开头的短语）
            return BuildMatchPhrasePrefixQuery<T>(fieldPath, nestedPath, value.ToString() ?? string.Empty);
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
            // EndsWith 优先走 keyword 子字段，避免分词导致误匹配
            var exactMatchFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
            var useKeywordSubField = !string.Equals(exactMatchFieldPath, fieldPath, StringComparison.Ordinal);
            
            // keyword 类型字段或具备 .keyword 子字段时使用 wildcard 查询
            if (useKeywordSubField || IsKeywordField(lastProperty))
            {
                return BuildWildcardQuery<T>(exactMatchFieldPath, nestedPath, $"*{value}");
            }

            // 没有 keyword 子字段时只能退回到原字段的 wildcard（可能存在分词影响）
            return BuildWildcardQuery<T>(fieldPath, nestedPath, $"*{value}");
        }

        return null;
    }

    /// <summary>
    /// 解析 Any 方法（用于集合查询）
    /// </summary>
    private static Action<QueryDescriptor<T>>? ParseAny<T>(MethodCallExpression methodCall)
    {
        // Any 支持两类场景：
        // 1) 值类型/字符串数组：items.Any(v => v == value)
        // 2) 对象数组：items.Any(x => x.Field == value)
        // 注意：复杂组合条件（如 x => x.A == 1 && x.B == 2）暂不在 Any 中展开，
        // 需要时可扩展为“嵌套布尔树 + 相对路径”解析。

        if (!TryExtractAnySource(methodCall, out var collectionExpression, out var predicate))
        {
            return null;
        }

        // Any 必须基于索引字段（即集合字段）
        var (collectionFieldPath, collectionNestedPath, collectionProperty) = ExtractFieldFromExpression<T>(collectionExpression);
        if (string.IsNullOrEmpty(collectionFieldPath) || predicate == null)
        {
            return null;
        }

        var condition = ParseAnyPredicate<T>(predicate, collectionFieldPath, collectionNestedPath, collectionProperty);
        if (condition == null)
        {
            return null;
        }

        return BuildConditionQuery<T>(condition);
    }

    /// <summary>
    /// 解析 Any 的谓词表达式为 QueryCondition
    /// </summary>
    private static QueryCondition<T>? ParseAnyPredicate<T>(
        LambdaExpression predicate,
        string collectionFieldPath,
        string? collectionNestedPath,
        PropertyInfo? collectionProperty)
    {
        if (predicate.Parameters.Count != 1)
        {
            return null;
        }

        var parameter = predicate.Parameters[0];
        var body = predicate.Body;

        // 统一处理类型转换
        if (body is UnaryExpression convert && convert.NodeType == ExpressionType.Convert)
        {
            body = convert.Operand;
        }

        // 处理逻辑非（!）
        var isNegated = false;
        if (body is UnaryExpression notUnary && notUnary.NodeType == ExpressionType.Not)
        {
            isNegated = true;
            body = notUnary.Operand;
        }

        // 0) 复合逻辑（&& / ||）
        if (body is BinaryExpression logicBinary &&
            (logicBinary.NodeType == ExpressionType.AndAlso || logicBinary.NodeType == ExpressionType.OrElse))
        {
            var boolNode = ConvertToBoolNodeForAny<T>(body, parameter, collectionFieldPath, collectionNestedPath, collectionProperty);
            if (boolNode == null)
            {
                return null;
            }

            var collectionIsNested = IsNestedCollectionProperty(collectionProperty);
            Action<QueryDescriptor<T>> queryAction;

            if (collectionIsNested)
            {
                var nestedPath = string.IsNullOrEmpty(collectionNestedPath)
                    ? collectionFieldPath
                    : $"{collectionNestedPath}.{collectionFieldPath}";

                var relativeAction = BuildQueryRelativeToNested<T>(boolNode, nestedPath);
                queryAction = query => query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq => relativeAction(nq))
                );
            }
            else
            {
                queryAction = BuildQueryFromBoolNode<T>(boolNode);
            }

            return new QueryCondition<T>
            {
                ConditionType = ConditionType.CustomQuery,
                CustomQueryAction = queryAction,
                IsNegated = isNegated
            };
        }

        // 1) 比较表达式（==, !=, >, <, >=, <=）
        if (body is BinaryExpression binary && IsComparisonOperator(binary.NodeType))
        {
            var comparisonType = GetComparisonType(binary.NodeType);
            if (comparisonType == null)
            {
                return null;
            }

            if (!TryExtractAnyFieldAndValue(binary.Left, binary.Right, parameter, out var innerFieldPath, out var innerProperty, out var value, out var isElementSelf))
            {
                return null;
            }

            var (finalFieldPath, finalNestedPath, lastProperty) = BuildAnyFieldPath(
                collectionFieldPath, collectionNestedPath, collectionProperty,
                innerFieldPath, innerProperty, isElementSelf);

            if (string.IsNullOrEmpty(finalFieldPath) || value == null)
            {
                return null;
            }

            // 等值/不等值走精确匹配，范围走范围字段规则
            var resolvedFieldPath = comparisonType == ComparisonType.Equals || comparisonType == ComparisonType.NotEquals
                ? GetFieldPathForExactMatch(finalFieldPath, lastProperty)
                : GetFieldPathForRangeQuery(finalFieldPath, lastProperty, value);

            return new QueryCondition<T>
            {
                FieldPath = resolvedFieldPath,
                NestedPath = finalNestedPath,
                LastProperty = lastProperty,
                ComparisonType = comparisonType.Value,
                Value = value,
                ConditionType = ConditionType.Comparison,
                IsNegated = isNegated
            };
        }

        // 2) 元素本身是布尔类型（例如 flags.Any(x => x)）
        if (body is ParameterExpression parameterExpression && parameterExpression == parameter)
        {
            var elementType = GetCollectionElementType(collectionProperty?.PropertyType);
            if (!IsBooleanType(elementType))
            {
                return null;
            }

            var (finalFieldPath, finalNestedPath, lastProperty) = BuildAnyFieldPath(
                collectionFieldPath, collectionNestedPath, collectionProperty,
                innerFieldPath: null, innerProperty: null, isElementSelf: true);

            if (string.IsNullOrEmpty(finalFieldPath))
            {
                return null;
            }

            return new QueryCondition<T>
            {
                FieldPath = finalFieldPath,
                NestedPath = finalNestedPath,
                LastProperty = lastProperty,
                ComparisonType = ComparisonType.Equals,
                Value = true,
                ConditionType = ConditionType.Comparison,
                IsNegated = isNegated
            };
        }

        // 3) 布尔成员访问（例如 items.Any(x => x.IsEnabled)）
        if (body is MemberExpression member)
        {
            if (!TryGetMemberBooleanType(member, out var memberType) || !IsBooleanType(memberType))
            {
                return null;
            }

            if (!TryExtractAnyFieldFromExpression(member, parameter, out var innerFieldPath, out var innerProperty, out var isElementSelf))
            {
                return null;
            }

            var (finalFieldPath, finalNestedPath, lastProperty) = BuildAnyFieldPath(
                collectionFieldPath, collectionNestedPath, collectionProperty,
                innerFieldPath, innerProperty, isElementSelf);

            if (string.IsNullOrEmpty(finalFieldPath))
            {
                return null;
            }

            return new QueryCondition<T>
            {
                FieldPath = finalFieldPath,
                NestedPath = finalNestedPath,
                LastProperty = lastProperty,
                ComparisonType = ComparisonType.Equals,
                Value = true,
                ConditionType = ConditionType.Comparison,
                IsNegated = isNegated
            };
        }

        return null;
    }

    /// <summary>
    /// 将 Any 的谓词表达式转换为布尔树
    /// </summary>
    private static BoolNode<T>? ConvertToBoolNodeForAny<T>(
        Expression expression,
        ParameterExpression parameter,
        string collectionFieldPath,
        string? collectionNestedPath,
        PropertyInfo? collectionProperty)
    {
        // 处理类型转换
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return ConvertToBoolNodeForAny<T>(unary.Operand, parameter, collectionFieldPath, collectionNestedPath, collectionProperty);
        }

        if (expression is BinaryExpression binary)
        {
            return binary.NodeType switch
            {
                ExpressionType.OrElse => MergeOrNodes<T>(
                    ConvertToBoolNodeForAny<T>(binary.Left, parameter, collectionFieldPath, collectionNestedPath, collectionProperty),
                    ConvertToBoolNodeForAny<T>(binary.Right, parameter, collectionFieldPath, collectionNestedPath, collectionProperty)
                ),
                ExpressionType.AndAlso => MergeAndNodes<T>(
                    ConvertToBoolNodeForAny<T>(binary.Left, parameter, collectionFieldPath, collectionNestedPath, collectionProperty),
                    ConvertToBoolNodeForAny<T>(binary.Right, parameter, collectionFieldPath, collectionNestedPath, collectionProperty)
                ),
                _ => CreateAtomicBoolNodeForAny<T>(expression, parameter, collectionFieldPath, collectionNestedPath, collectionProperty)
            };
        }

        return CreateAtomicBoolNodeForAny<T>(expression, parameter, collectionFieldPath, collectionNestedPath, collectionProperty);
    }

    /// <summary>
    /// 创建 Any 的原子布尔节点
    /// </summary>
    private static BoolNode<T>? CreateAtomicBoolNodeForAny<T>(
        Expression expression,
        ParameterExpression parameter,
        string collectionFieldPath,
        string? collectionNestedPath,
        PropertyInfo? collectionProperty)
    {
        var condition = ParseAnyAtomicCondition<T>(expression, parameter, collectionFieldPath, collectionNestedPath, collectionProperty);
        if (condition == null)
        {
            return null;
        }

        return new AtomicBoolNode<T>(condition);
    }

    /// <summary>
    /// 解析 Any 的原子条件（比较表达式、布尔成员访问等）
    /// </summary>
    private static QueryCondition<T>? ParseAnyAtomicCondition<T>(
        Expression expression,
        ParameterExpression parameter,
        string collectionFieldPath,
        string? collectionNestedPath,
        PropertyInfo? collectionProperty)
    {
        // 处理一元表达式（类型转换 / 逻辑非）
        if (expression is UnaryExpression unary)
        {
            if (unary.NodeType == ExpressionType.Convert)
            {
                return ParseAnyAtomicCondition<T>(unary.Operand, parameter, collectionFieldPath, collectionNestedPath, collectionProperty);
            }

            if (unary.NodeType == ExpressionType.Not)
            {
                var innerCondition = ParseAnyAtomicCondition<T>(unary.Operand, parameter, collectionFieldPath, collectionNestedPath, collectionProperty);
                if (innerCondition == null)
                {
                    return null;
                }

                innerCondition.IsNegated = !innerCondition.IsNegated;
                return innerCondition;
            }
        }

        var collectionIsNested = IsNestedCollectionProperty(collectionProperty);

        // 比较表达式
        if (expression is BinaryExpression binary && IsComparisonOperator(binary.NodeType))
        {
            var comparisonType = GetComparisonType(binary.NodeType);
            if (comparisonType == null)
            {
                return null;
            }

            if (!TryExtractAnyFieldAndValue(binary.Left, binary.Right, parameter, out var innerFieldPath, out var innerProperty, out var value, out var isElementSelf))
            {
                return null;
            }

            if (collectionIsNested && isElementSelf)
            {
                // nested 对象数组无法直接对元素本身做比较
                return null;
            }

            var lastProperty = isElementSelf ? collectionProperty : innerProperty;
            string? fieldPath;
            string? nestedPath;

            if (collectionIsNested)
            {
                if (string.IsNullOrEmpty(innerFieldPath))
                {
                    return null;
                }

                fieldPath = innerFieldPath;
                nestedPath = null;
            }
            else
            {
                if (isElementSelf)
                {
                    fieldPath = collectionFieldPath;
                }
                else
                {
                    if (string.IsNullOrEmpty(innerFieldPath))
                    {
                        return null;
                    }

                    fieldPath = $"{collectionFieldPath}.{innerFieldPath}";
                }

                nestedPath = collectionNestedPath;
            }

            if (string.IsNullOrEmpty(fieldPath) || value == null)
            {
                return null;
            }

            var resolvedFieldPath = comparisonType == ComparisonType.Equals || comparisonType == ComparisonType.NotEquals
                ? GetFieldPathForExactMatch(fieldPath, lastProperty)
                : GetFieldPathForRangeQuery(fieldPath, lastProperty, value);

            return new QueryCondition<T>
            {
                FieldPath = resolvedFieldPath,
                NestedPath = nestedPath,
                LastProperty = lastProperty,
                ComparisonType = comparisonType.Value,
                Value = value,
                ConditionType = ConditionType.Comparison
            };
        }

        // 元素本身是布尔类型（例如 flags.Any(x => x)）
        if (expression is ParameterExpression parameterExpression && parameterExpression == parameter)
        {
            var elementType = GetCollectionElementType(collectionProperty?.PropertyType);
            if (!IsBooleanType(elementType))
            {
                return null;
            }

            if (collectionIsNested)
            {
                // nested 对象数组不支持直接对元素本身做布尔判断
                return null;
            }

            return new QueryCondition<T>
            {
                FieldPath = collectionFieldPath,
                NestedPath = collectionNestedPath,
                LastProperty = collectionProperty,
                ComparisonType = ComparisonType.Equals,
                Value = true,
                ConditionType = ConditionType.Comparison
            };
        }

        // 布尔成员访问（例如 items.Any(x => x.IsEnabled)）
        if (expression is MemberExpression member)
        {
            if (!TryGetMemberBooleanType(member, out var memberType) || !IsBooleanType(memberType))
            {
                return null;
            }

            if (!TryExtractAnyFieldFromExpression(member, parameter, out var innerFieldPath, out var innerProperty, out var isElementSelf))
            {
                return null;
            }

            if (collectionIsNested && isElementSelf)
            {
                return null;
            }

            string? fieldPath;
            string? nestedPath;
            var lastProperty = isElementSelf ? collectionProperty : innerProperty;

            if (collectionIsNested)
            {
                if (string.IsNullOrEmpty(innerFieldPath))
                {
                    return null;
                }

                fieldPath = innerFieldPath;
                nestedPath = null;
            }
            else
            {
                if (isElementSelf)
                {
                    fieldPath = collectionFieldPath;
                }
                else
                {
                    if (string.IsNullOrEmpty(innerFieldPath))
                    {
                        return null;
                    }

                    fieldPath = $"{collectionFieldPath}.{innerFieldPath}";
                }

                nestedPath = collectionNestedPath;
            }

            return new QueryCondition<T>
            {
                FieldPath = fieldPath,
                NestedPath = nestedPath,
                LastProperty = lastProperty,
                ComparisonType = ComparisonType.Equals,
                Value = true,
                ConditionType = ConditionType.Comparison
            };
        }

        return null;
    }

    /// <summary>
    /// 从 Any 的谓词中提取字段和值
    /// </summary>
    private static bool TryExtractAnyFieldAndValue(
        Expression left,
        Expression right,
        ParameterExpression parameter,
        out string? innerFieldPath,
        out PropertyInfo? innerProperty,
        out object? value,
        out bool isElementSelf)
    {
        if (TryExtractAnyFieldFromExpression(left, parameter, out innerFieldPath, out innerProperty, out isElementSelf))
        {
            value = EvaluateExpression(right);
            return true;
        }

        if (TryExtractAnyFieldFromExpression(right, parameter, out innerFieldPath, out innerProperty, out isElementSelf))
        {
            value = EvaluateExpression(left);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// 从 Any 的谓词表达式中提取相对于元素参数的字段路径
    /// </summary>
    private static bool TryExtractAnyFieldFromExpression(
        Expression expression,
        ParameterExpression parameter,
        out string? fieldPath,
        out PropertyInfo? lastProperty,
        out bool isElementSelf)
    {
        fieldPath = null;
        lastProperty = null;
        isElementSelf = false;

        // 处理类型转换
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            expression = unary.Operand;
        }

        // 直接访问元素本身（值类型数组）
        if (expression is ParameterExpression parameterExpression && parameterExpression == parameter)
        {
            isElementSelf = true;
            return true;
        }

        var path = new List<string>();
        var properties = new List<PropertyInfo>();
        var current = expression;

        while (current is MemberExpression member)
        {
            if (member.Member is PropertyInfo propertyInfo)
            {
                // 跳过 Nullable<T>.Value
                if (IsNullableValueProperty(propertyInfo) && member.Expression != null)
                {
                    current = member.Expression;
                    continue;
                }

                properties.Insert(0, propertyInfo);
                var fieldName = FieldNameHelper.GetIndexFieldName(propertyInfo);
                path.Insert(0, fieldName);
            }
            else
            {
                path.Insert(0, FieldNameHelper.GetIndexFieldName(member.Member.Name));
            }

            current = member.Expression;
        }

        if (path.Count == 0)
        {
            return false;
        }

        if (current != parameter)
        {
            return false;
        }

        fieldPath = string.Join(".", path);
        lastProperty = properties.Count > 0 ? properties[^1] : null;
        return true;
    }

    /// <summary>
    /// 组合集合字段与元素字段，得到最终查询字段与嵌套路径
    /// </summary>
    private static (string fieldPath, string? nestedPath, PropertyInfo? lastProperty) BuildAnyFieldPath(
        string collectionFieldPath,
        string? collectionNestedPath,
        PropertyInfo? collectionProperty,
        string? innerFieldPath,
        PropertyInfo? innerProperty,
        bool isElementSelf)
    {
        // 先判断集合字段是否为 nested（依赖配置特性）
        var collectionIsNested = IsNestedCollectionProperty(collectionProperty);

        if (collectionIsNested)
        {
            // nested 对象数组：将集合字段作为 nestedPath，字段路径只保留元素内的相对路径
            // 例如 items.Any(x => x.Id == 1)
            // nestedPath = "items"，fieldPath = "id"
            var finalNestedPath = string.IsNullOrEmpty(collectionNestedPath)
                ? collectionFieldPath
                : $"{collectionNestedPath}.{collectionFieldPath}";

            if (string.IsNullOrEmpty(innerFieldPath))
            {
                return (string.Empty, finalNestedPath, innerProperty);
            }

            return (innerFieldPath, finalNestedPath, innerProperty);
        }

        // 非 nested 数组：字段路径需要包含集合字段前缀，嵌套路径沿用外层嵌套信息
        // 例如 items.Any(x => x.Id == 1) => fieldPath = "items.id"
        if (isElementSelf)
        {
            return (collectionFieldPath, collectionNestedPath, collectionProperty);
        }

        if (string.IsNullOrEmpty(innerFieldPath))
        {
            return (string.Empty, collectionNestedPath, innerProperty);
        }

        return ($"{collectionFieldPath}.{innerFieldPath}", collectionNestedPath, innerProperty);
    }

    /// <summary>
    /// 判断集合字段是否配置为 nested
    /// </summary>
    private static bool IsNestedCollectionProperty(PropertyInfo? propertyInfo)
    {
        if (propertyInfo == null)
        {
            return false;
        }

        var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();
        if (esFieldAttr?.IsNested != null)
        {
            return esFieldAttr.IsNested.Value;
        }

        // 与 IndexMappingBuilder 的逻辑保持一致：
        // 1) 如果字段本身是嵌套类型，视为 nested
        // 2) 如果是集合且元素类型为嵌套类型，视为 nested
        var propertyType = propertyInfo.PropertyType;
        if (IsNestedType(propertyType))
        {
            return true;
        }

        if (IsCollectionType(propertyType))
        {
            var elementType = GetCollectionElementType(propertyType);
            if (elementType != null && IsNestedType(elementType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取集合元素类型
    /// </summary>
    private static Type? GetCollectionElementType(Type? type)
    {
        if (type == null)
        {
            return null;
        }

        // 处理可空类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        // 数组类型
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        // 泛型集合类型
        if (type.IsGenericType)
        {
            var genericArgs = type.GetGenericArguments();
            if (genericArgs.Length > 0)
            {
                return genericArgs[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 判断是否为布尔类型（含可空）
    /// </summary>
    private static bool IsBooleanType(Type? type)
    {
        if (type == null)
        {
            return false;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type == typeof(bool);
    }

    /// <summary>
    /// 尝试获取成员表达式的类型
    /// </summary>
    private static bool TryGetMemberBooleanType(MemberExpression member, out Type? memberType)
    {
        memberType = null;

        if (member.Member is PropertyInfo propertyInfo)
        {
            memberType = propertyInfo.PropertyType;
            return true;
        }

        if (member.Member is FieldInfo fieldInfo)
        {
            memberType = fieldInfo.FieldType;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 提取 Any 的集合表达式和谓词
    /// </summary>
    private static bool TryExtractAnySource(MethodCallExpression methodCall, out Expression collectionExpression, out LambdaExpression? predicate)
    {
        predicate = null;

        // Any 是扩展方法时，Object 为空，集合在第一个参数
        if (methodCall.Object == null)
        {
            if (methodCall.Arguments.Count < 1)
            {
                collectionExpression = null!;
                return false;
            }

            collectionExpression = methodCall.Arguments[0];
            if (methodCall.Arguments.Count > 1)
            {
                predicate = UnwrapLambda(methodCall.Arguments[1]);
            }

            return true;
        }

        // 实例方法（较少见）
        collectionExpression = methodCall.Object;
        if (methodCall.Arguments.Count > 0)
        {
            predicate = UnwrapLambda(methodCall.Arguments[0]);
        }

        return true;
    }

    /// <summary>
    /// 解包 Lambda（处理 Quote）
    /// </summary>
    private static LambdaExpression? UnwrapLambda(Expression expression)
    {
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Quote)
        {
            expression = unary.Operand;
        }

        return expression as LambdaExpression;
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
        return BuildComparisonQuery<T>(fieldPath, nestedPath, ComparisonType.Equals, true, lastProperty);
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
            // 特殊处理 Nullable<T>.Value：
            // 这是代码层面的空值解包，不是索引字段的一部分，不能出现在字段路径中。
            // 例如 o.Field.Value 应等价于 o.Field，避免生成 "field.value" 的查询字段。
            if (member.Member is PropertyInfo valuePropertyInfo &&
                IsNullableValueProperty(valuePropertyInfo) &&
                member.Expression != null)
            {
                current = member.Expression;
                continue;
            }

            // 如果是属性，保存 PropertyInfo 并获取索引名称
            if (member.Member is PropertyInfo propertyInfo)
            {
                properties.Insert(0, propertyInfo);
                
                // 使用 FieldNameHelper 获取字段的字段名称（如果配置了 FieldName，则使用配置的名称）
                // 如果没有配置 FieldName，会自动将 PascalCase 转换为 camelCase
                var fieldName = FieldNameHelper.GetIndexFieldName(propertyInfo);
                
                path.Insert(0, fieldName);
            }
            else
            {
                // 非属性成员（如字段），将 PascalCase 转换为 camelCase
                path.Insert(0, FieldNameHelper.GetIndexFieldName(member.Member.Name));
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

        // 只允许从参数表达式（即查询对象本身）提取字段路径。
        // 这样可以避免把闭包变量或本地变量当作索引字段，例如：
        // var a = new List<string> { "3", "7" };
        // a.Contains(o.DistributeType)
        // 此时 a 是值集合，不应被识别为字段路径。
        if (!IsParameterExpressionOfType<T>(current))
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

    private static bool IsNestedType(Type type) => TypeHelper.IsNestedType(type);

    private static bool IsCollectionType(Type type) => TypeHelper.IsCollectionType(type);

    /// <summary>
    /// 判断表达式是否为指定类型的参数表达式（允许显式转换）
    /// </summary>
    private static bool IsParameterExpressionOfType<T>(Expression? expression)
    {
        if (expression == null)
        {
            return false;
        }

        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return IsParameterExpressionOfType<T>(unary.Operand);
        }

        if (expression is ParameterExpression parameter)
        {
            return parameter.Type == typeof(T) || typeof(T).IsAssignableFrom(parameter.Type);
        }

        return false;
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
    /// <param name="lastProperty">最后一个属性的 PropertyInfo（用于获取字段类型信息）</param>
    /// <remarks>
    /// 对于嵌套查询：
    /// - nestedPath 是嵌套文档的路径（例如 "address"）
    /// - fieldPath 是嵌套文档内的字段路径（例如 "city.keyword"）
    /// - 在构建嵌套查询时，字段路径需要包含完整的嵌套路径（例如 "address.city.keyword"）
    /// - 这是因为 Elasticsearch 嵌套查询中的字段路径需要是完整路径，而不是相对于嵌套路径的相对路径
    /// </remarks>
    private static Action<QueryDescriptor<T>> BuildComparisonQuery<T>(
        string fieldPath, string? nestedPath, ComparisonType comparisonType, object value, PropertyInfo? lastProperty)
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
                    .Query(nq => ApplyComparisonToQuery(nq, fullFieldPath, comparisonType, value, lastProperty))
                );
            }
            else
            {
                // 普通查询（非嵌套）
                ApplyComparisonToQuery(query, fieldPath, comparisonType, value, lastProperty);
            }
        };
    }

    /// <summary>
    /// 应用比较查询到 QueryDescriptor
    /// </summary>
    private static void ApplyComparisonToQuery<T>(
        QueryDescriptor<T> query, string fieldPath, ComparisonType comparisonType, object value, PropertyInfo? lastProperty)
    {
        switch (comparisonType)
        {
            case ComparisonType.Equals:
                ApplyEqualsQuery(query, fieldPath, value, lastProperty);
                break;

            case ComparisonType.NotEquals:
                query.Bool(b => b.MustNot(mn => ApplyEqualsQuery(mn, fieldPath, value, lastProperty)));
                break;

            case ComparisonType.GreaterThan:
            case ComparisonType.GreaterThanOrEqual:
            case ComparisonType.LessThan:
            case ComparisonType.LessThanOrEqual:
                ApplyRangeQuery(query, fieldPath, comparisonType, value, lastProperty);
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
                // Nullable<T>.Value 不是索引字段，跳过并继续向上查找真实字段
                if (IsNullableValueProperty(propertyInfo) && member.Expression != null)
                {
                    current = member.Expression;
                    continue;
                }

                lastProperty = propertyInfo;
            }
            current = member.Expression;
        }

        return lastProperty;
    }

    /// <summary>
    /// 判断成员是否为 Nullable&lt;T&gt;.Value
    /// </summary>
    private static bool IsNullableValueProperty(PropertyInfo propertyInfo)
    {
        return propertyInfo.Name == "Value"
            && propertyInfo.DeclaringType != null
            && propertyInfo.DeclaringType.IsGenericType
            && propertyInfo.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    /// <summary>
    /// 判断字段是否需要使用 .keyword 后缀（用于精确匹配和排序）
    /// 根据索引构建规则：
    /// 1. 如果 FieldType == "keyword"，字段直接是 keyword 类型，不需要添加 .keyword
    /// 2. 如果 FieldType == "text" 或未指定，且 NeedKeyword == true，字段是 text 类型且有 .keyword 子字段，需要添加 .keyword
    /// 3. 枚举类型：
    ///    - 如果配置为数值类型（int/long/short/byte），不需要添加 .keyword（因为字段本身就是数值类型）
    ///    - 如果配置为 keyword，不需要添加 .keyword（字段本身就是 keyword 类型）
    ///    - 如果配置为 text 或未配置，且 NeedKeyword == true，需要添加 .keyword（字段是 text 类型，有 .keyword 子字段）
    /// </summary>
    internal static string GetFieldPathForExactMatch(string fieldPath, PropertyInfo? propertyInfo)
    {
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

        // 获取字段特性
        var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();

        // 枚举类型特殊处理
        if (TypeHelper.IsEnumType(propertyType))
        {
            // 如果配置为数值类型，不需要添加 .keyword（字段本身就是数值类型）
            if (EnumFieldHelper.IsEnumStoredAsNumeric(propertyInfo, esFieldAttr))
            {
                return fieldPath;
            }

            // 获取字段类型：枚举类型未配置 FieldType 时，默认使用 "text"（与索引映射逻辑一致）
            // 在 BuildEnumPropertyMappingForGeneric 中，如果 fieldType 不是数值类型，会调用 BuildStringPropertyMappingForGeneric
            // BuildStringPropertyMappingForGeneric 使用 esFieldAttr?.FieldType ?? "text"，所以默认是 "text"
            var fieldType = esFieldAttr?.FieldType?.ToLower();
            
            // 如果明确配置为 keyword，不需要添加 .keyword（字段本身就是 keyword 类型）
            if (fieldType == "keyword")
            {
                return fieldPath;
            }

            // 如果配置为 text 或未配置（默认是 text），且 NeedKeyword == true（默认也是 true），需要添加 .keyword
            // 这与 BuildStringPropertyMappingForGeneric 的逻辑一致
            var actualFieldType = fieldType ?? "text";
            var needKeyword = esFieldAttr?.NeedKeyword ?? true;
            
            if (actualFieldType == "text" && needKeyword)
            {
                return $"{fieldPath}.keyword";
            }

            // 如果配置为 text 但 NeedKeyword == false，不需要添加 .keyword
            return fieldPath;
        }

        // 字符串类型处理
        if (propertyType == typeof(string))
        {
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
        }

        // 其他类型不需要 keyword
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
    private static void ApplyEqualsQuery<T>(QueryDescriptor<T> query, string fieldPath, object value, PropertyInfo? lastProperty)
    {
        var valueType = value.GetType();

        // 检查字段类型是否为枚举类型
        // 如果值是整数类型，但字段是枚举类型，则需要将整数转换为枚举，然后使用枚举的名称
        Type? fieldType = null;
        if (lastProperty != null)
        {
            fieldType = lastProperty.PropertyType;
            // 处理可空类型
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                fieldType = fieldType.GetGenericArguments()[0];
            }
        }

        // 如果字段是枚举类型，需要根据字段配置决定使用枚举名称还是数值
        if (fieldType != null && TypeHelper.IsEnumType(fieldType))
        {
            // 获取字段特性
            var esFieldAttr = lastProperty?.GetCustomAttribute<EsFieldAttribute>();
            
            // 如果配置为数值类型，使用枚举的数值进行查询
            if (EnumFieldHelper.IsEnumStoredAsNumeric(lastProperty!, esFieldAttr))
            {
                // 如果值是整数类型，直接使用
                if (IsNumericType(valueType))
                {
                    query.Term(t => t.Field(fieldPath).Value(Convert.ToInt64(value)));
                    return;
                }
                
                // 如果值是枚举类型，转换为数值
                if (TypeHelper.IsEnumType(valueType))
                {
                    var enumValue = EnumFieldHelper.GetEnumValue(value, valueType);
                    query.Term(t => t.Field(fieldPath).Value(enumValue));
                    return;
                }
            }
            else
            {
                // 如果配置为 keyword/text 或未配置，使用枚举的名称进行查询
                // 如果值是整数类型，需要将整数转换为枚举，然后使用枚举的名称
                if (IsNumericType(valueType))
                {
                    var enumValue = Enum.ToObject(fieldType, value);
                    var enumName = enumValue?.ToString() ?? string.Empty;
                    query.Term(t => t.Field(fieldPath).Value(enumName));
                    return;
                }
                
                // 如果值是枚举类型，使用枚举的名称
                if (TypeHelper.IsEnumType(valueType))
                {
                    var enumName = value?.ToString() ?? string.Empty;
                    query.Term(t => t.Field(fieldPath).Value(enumName));
                    return;
                }
            }
        }

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
        QueryDescriptor<T> query, string fieldPath, ComparisonType comparisonType, object value, PropertyInfo? lastProperty)
    {
        var valueType = value.GetType();

        // 检查字段类型是否为枚举类型
        Type? fieldType = null;
        if (lastProperty != null)
        {
            fieldType = lastProperty.PropertyType;
            // 处理可空类型
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                fieldType = fieldType.GetGenericArguments()[0];
            }
        }

        // 如果字段是枚举类型，需要检查是否配置为数值类型
        if (fieldType != null && TypeHelper.IsEnumType(fieldType))
        {
            // 获取字段特性
            var esFieldAttr = lastProperty?.GetCustomAttribute<EsFieldAttribute>();
            
            // 如果配置为数值类型，允许范围查询
            if (EnumFieldHelper.IsEnumStoredAsNumeric(lastProperty!, esFieldAttr))
            {
                // 枚举配置为数值类型，可以使用范围查询
                // 继续执行范围查询逻辑
            }
            else
            {
                // 如果配置为 keyword/text 或未配置，不支持范围查询
                throw new ArgumentException($"枚举类型不支持范围查询，请使用等值查询（==）或 In 查询（Contains）。如果需要对枚举进行范围查询，请将字段配置为数值类型（如 FieldType = \"integer\" 或 \"long\"）: {fieldType.Name}");
            }
        }

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

        // 枚举类型：不支持范围查询
        // 枚举值之间没有明确的顺序关系，且 Elasticsearch 中枚举存储为字符串
        // 如果需要范围查询，应该使用等值查询或 Terms 查询
        if (TypeHelper.IsEnumType(valueType))
        {
            throw new ArgumentException($"枚举类型不支持范围查询，请使用等值查询（==）或 In 查询（Contains）: {valueType.Name}");
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
        string fieldPath, string? nestedPath, IEnumerable values, PropertyInfo? lastProperty = null)
    {
        var valueList = values.Cast<object>().ToList();
        if (!valueList.Any())
        {
            return _ => { };
        }

        var fieldValues = ConvertToFieldValues(valueList, lastProperty);
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
    /// 根据字段配置决定枚举值使用名称还是数值
    /// </summary>
    /// <param name="values">值列表</param>
    /// <param name="propertyInfo">字段属性信息（可选，用于判断枚举字段配置）</param>
    private static Elastic.Clients.Elasticsearch.FieldValue[] ConvertToFieldValues(List<object> values, PropertyInfo? propertyInfo = null)
    {
        if (!values.Any())
        {
            return Array.Empty<Elastic.Clients.Elasticsearch.FieldValue>();
        }

        var firstValue = values.First();
        var valueType = firstValue.GetType();

        // 枚举类型：根据字段配置决定使用名称还是数值
        if (TypeHelper.IsEnumType(valueType))
        {
            // 如果提供了字段信息，检查是否配置为数值类型
            if (propertyInfo != null)
            {
                var esFieldAttr = propertyInfo.GetCustomAttribute<EsFieldAttribute>();
                
                // 如果配置为数值类型，使用枚举的数值
                if (EnumFieldHelper.IsEnumStoredAsNumeric(propertyInfo, esFieldAttr))
                {
                    return values.Select(v =>
                    {
                        if (v == null)
                        {
                            return Double(0);
                        }
                        var enumValue = EnumFieldHelper.GetEnumValue(v, valueType);
                        return Double(enumValue);
                    }).ToArray();
                }
            }
            
            // 默认使用枚举的名称（保持向后兼容）
            return values.Select(v => String(v?.ToString() ?? string.Empty)).ToArray();
        }

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
    /// 将表达式转换为布尔树（不做 DNF 展开）
    /// 目标：保留原始逻辑结构，避免 OR 组交叉组合带来的数量爆炸
    /// </summary>
    private static BoolNode<T>? ConvertToBoolNode<T>(Expression expression)
    {
        // 处理类型转换，保证解析一致性
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return ConvertToBoolNode<T>(unary.Operand);
        }

        // 处理二元表达式
        if (expression is BinaryExpression binary)
        {
            return binary.NodeType switch
            {
                // OR 运算符：构建 Or 节点并扁平化相邻的 Or 结构
                ExpressionType.OrElse => MergeOrNodes<T>(
                    ConvertToBoolNode<T>(binary.Left),
                    ConvertToBoolNode<T>(binary.Right)
                ),

                // AND 运算符：构建 And 节点并扁平化相邻的 And 结构
                ExpressionType.AndAlso => MergeAndNodes<T>(
                    ConvertToBoolNode<T>(binary.Left),
                    ConvertToBoolNode<T>(binary.Right)
                ),

                // 其他二元运算符（比较运算符）作为原子条件
                _ => CreateAtomicBoolNode<T>(expression)
            };
        }

        // 处理其他表达式类型（方法调用、成员访问等）作为原子条件
        return CreateAtomicBoolNode<T>(expression);
    }

    /// <summary>
    /// 创建原子布尔节点
    /// </summary>
    private static BoolNode<T>? CreateAtomicBoolNode<T>(Expression expression)
    {
        var condition = ParseAtomicCondition<T>(expression);
        if (condition == null)
        {
            return null;
        }

        return new AtomicBoolNode<T>(condition);
    }

    /// <summary>
    /// 合并 AND 节点并扁平化结构，避免多层嵌套
    /// </summary>
    private static BoolNode<T>? MergeAndNodes<T>(BoolNode<T>? left, BoolNode<T>? right)
    {
        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        var merged = new AndBoolNode<T>();
        AppendNode(merged.Children, left, isAnd: true);
        AppendNode(merged.Children, right, isAnd: true);
        return merged;
    }

    /// <summary>
    /// 合并 OR 节点并扁平化结构，避免多层嵌套
    /// </summary>
    private static BoolNode<T>? MergeOrNodes<T>(BoolNode<T>? left, BoolNode<T>? right)
    {
        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        var merged = new OrBoolNode<T>();
        AppendNode(merged.Children, left, isAnd: false);
        AppendNode(merged.Children, right, isAnd: false);
        return merged;
    }

    /// <summary>
    /// 将节点追加到目标列表中，同时进行同类型扁平化
    /// </summary>
    private static void AppendNode<T>(List<BoolNode<T>> target, BoolNode<T> node, bool isAnd)
    {
        if (isAnd && node is AndBoolNode<T> andNode)
        {
            target.AddRange(andNode.Children);
            return;
        }

        if (!isAnd && node is OrBoolNode<T> orNode)
        {
            target.AddRange(orNode.Children);
            return;
        }

        target.Add(node);
    }

    /// <summary>
    /// 从布尔树生成查询
    /// 核心思想：优先合并相同 nestedPath 的条件，避免多个 nested 查询
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildQueryFromBoolNode<T>(BoolNode<T> node)
    {
        return node switch
        {
            AtomicBoolNode<T> atomic => BuildConditionQuery<T>(atomic.Condition),
            AndBoolNode<T> andNode => BuildQueryFromAndNode<T>(andNode),
            OrBoolNode<T> orNode => BuildQueryFromOrNode<T>(orNode),
            _ => _ => { }
        };
    }

    /// <summary>
    /// 构建 AND 逻辑的查询
    /// 规则：
    /// - 同一 nestedPath 的子节点合并为一个 nested 查询（内部 must）
    /// - 其他子节点保持原结构，使用 must 组合
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildQueryFromAndNode<T>(AndBoolNode<T> node)
    {
        if (node.Children.Count == 0)
        {
            return _ => { };
        }

        if (node.Children.Count == 1)
        {
            return BuildQueryFromBoolNode<T>(node.Children[0]);
        }

        var nestedGroups = new Dictionary<string, List<BoolNode<T>>>();
        var regularNodes = new List<BoolNode<T>>();

        foreach (var child in node.Children)
        {
            if (TryGetUniformNestedPath(child, out var nestedPath))
            {
                if (!nestedGroups.TryGetValue(nestedPath, out var list))
                {
                    list = new List<BoolNode<T>>();
                    nestedGroups[nestedPath] = list;
                }

                list.Add(child);
                continue;
            }

            regularNodes.Add(child);
        }

        var queryActions = new List<Action<QueryDescriptor<T>>>();

        // 处理可合并的 nested 组
        foreach (var (nestedPath, groupNodes) in nestedGroups)
        {
            var nestedActions = groupNodes
                .Select(n => BuildQueryRelativeToNested<T>(n, nestedPath))
                .ToArray();

            queryActions.Add(query =>
            {
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq =>
                    {
                        if (nestedActions.Length == 1)
                        {
                            nestedActions[0](nq);
                        }
                        else
                        {
                            nq.Bool(b => b.Must(nestedActions));
                        }
                    })
                );
            });
        }

        // 处理无法合并的节点
        foreach (var child in regularNodes)
        {
            queryActions.Add(BuildQueryFromBoolNode<T>(child));
        }

        if (queryActions.Count == 0)
        {
            return _ => { };
        }

        if (queryActions.Count == 1)
        {
            return queryActions[0];
        }

        return query => query.Bool(b => b.Must(queryActions.ToArray()));
    }

    /// <summary>
    /// 构建 OR 逻辑的查询
    /// 规则：
    /// - 同一 nestedPath 的子节点合并为一个 nested 查询（内部 should）
    /// - 其他子节点保持原结构，使用 should 组合
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildQueryFromOrNode<T>(OrBoolNode<T> node)
    {
        if (node.Children.Count == 0)
        {
            return _ => { };
        }

        if (node.Children.Count == 1)
        {
            return BuildQueryFromBoolNode<T>(node.Children[0]);
        }

        var nestedGroups = new Dictionary<string, List<BoolNode<T>>>();
        var regularNodes = new List<BoolNode<T>>();

        foreach (var child in node.Children)
        {
            if (TryGetUniformNestedPath(child, out var nestedPath))
            {
                if (!nestedGroups.TryGetValue(nestedPath, out var list))
                {
                    list = new List<BoolNode<T>>();
                    nestedGroups[nestedPath] = list;
                }

                list.Add(child);
                continue;
            }

            regularNodes.Add(child);
        }

        var queryActions = new List<Action<QueryDescriptor<T>>>();

        // 处理可合并的 nested 组
        foreach (var (nestedPath, groupNodes) in nestedGroups)
        {
            var nestedActions = groupNodes
                .Select(n => BuildQueryRelativeToNested<T>(n, nestedPath))
                .ToArray();

            queryActions.Add(query =>
            {
                query.Nested(n => n
                    .Path(nestedPath)
                    .Query(nq =>
                    {
                        if (nestedActions.Length == 1)
                        {
                            nestedActions[0](nq);
                        }
                        else
                        {
                            nq.Bool(b => b.Should(nestedActions));
                        }
                    })
                );
            });
        }

        // 处理无法合并的节点
        foreach (var child in regularNodes)
        {
            queryActions.Add(BuildQueryFromBoolNode<T>(child));
        }

        if (queryActions.Count == 0)
        {
            return _ => { };
        }

        if (queryActions.Count == 1)
        {
            return queryActions[0];
        }

        return query => query.Bool(b => b.Should(queryActions.ToArray()));
    }

    /// <summary>
    /// 尝试判断节点是否完全属于同一 nestedPath（且不包含逻辑非）
    /// 用于判断是否可以合并为一个 nested 查询
    /// </summary>
    private static bool TryGetUniformNestedPath<T>(BoolNode<T> node, out string nestedPath)
    {
        nestedPath = string.Empty;

        switch (node)
        {
            case AtomicBoolNode<T> atomic:
                if (atomic.Condition.IsNegated || string.IsNullOrEmpty(atomic.Condition.NestedPath))
                {
                    return false;
                }

                nestedPath = atomic.Condition.NestedPath!;
                return true;

            case AndBoolNode<T> andNode:
                return TryGetUniformNestedPathFromChildren(andNode.Children, out nestedPath);

            case OrBoolNode<T> orNode:
                return TryGetUniformNestedPathFromChildren(orNode.Children, out nestedPath);

            default:
                return false;
        }
    }

    /// <summary>
    /// 从子节点集合中判断是否存在统一的 nestedPath
    /// </summary>
    private static bool TryGetUniformNestedPathFromChildren<T>(IReadOnlyList<BoolNode<T>> children, out string nestedPath)
    {
        nestedPath = string.Empty;
        if (children.Count == 0)
        {
            return false;
        }

        foreach (var child in children)
        {
            if (!TryGetUniformNestedPath(child, out var childPath))
            {
                return false;
            }

            if (string.IsNullOrEmpty(nestedPath))
            {
                nestedPath = childPath;
                continue;
            }

            if (nestedPath != childPath)
            {
                return false;
            }
        }

        return !string.IsNullOrEmpty(nestedPath);
    }

    /// <summary>
    /// 在已知 nestedPath 的前提下生成相对查询（不再重复包 nested）
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildQueryRelativeToNested<T>(BoolNode<T> node, string nestedPath)
    {
        switch (node)
        {
            case AtomicBoolNode<T> atomic:
                var fullFieldPath = $"{nestedPath}.{atomic.Condition.FieldPath}";
                return q => ApplyConditionToQueryWithNegation(q, fullFieldPath, atomic.Condition);

            case AndBoolNode<T> andNode:
                return BuildRelativeBoolQuery(andNode.Children, nestedPath, useShould: false);

            case OrBoolNode<T> orNode:
                return BuildRelativeBoolQuery(orNode.Children, nestedPath, useShould: true);

            default:
                return _ => { };
        }
    }

    /// <summary>
    /// 构建相对于 nestedPath 的 Bool 查询（不包 nested）
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildRelativeBoolQuery<T>(
        IReadOnlyList<BoolNode<T>> children,
        string nestedPath,
        bool useShould)
    {
        if (children.Count == 0)
        {
            return _ => { };
        }

        var actions = children
            .Select(child => BuildQueryRelativeToNested<T>(child, nestedPath))
            .ToArray();

        if (actions.Length == 1)
        {
            return actions[0];
        }

        return query =>
        {
            if (useShould)
            {
                query.Bool(b => b.Should(actions));
            }
            else
            {
                query.Bool(b => b.Must(actions));
            }
        };
    }


    /// <summary>
    /// 将表达式转换为 DNF（析取范式）格式
    /// DNF 格式：(a&&b&&c)||(d&&e&&f)||(g.h&&i)
    /// 顶层是 OR 关系（OrGroups），每个 OR 分支是一个 AND 条件组（AndConditions）
    /// </summary>
    private static DnfExpression<T>? ConvertToDnf<T>(Expression expression)
    {
        // 处理类型转换
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return ConvertToDnf<T>(unary.Operand);
        }

        // 处理二元表达式
        if (expression is BinaryExpression binary)
        {
            return binary.NodeType switch
            {
                // OR 运算符：合并左右两边的 OR 组
                ExpressionType.OrElse => MergeOrGroups<T>(
                    ConvertToDnf<T>(binary.Left),
                    ConvertToDnf<T>(binary.Right)
                ),
                
                // AND 运算符：交叉组合左右两边的条件
                ExpressionType.AndAlso => MergeAndConditions<T>(
                    ConvertToDnf<T>(binary.Left),
                    ConvertToDnf<T>(binary.Right)
                ),
                
                // 其他二元运算符（比较运算符）作为原子条件
                _ => CreateAtomicDnf<T>(expression)
            };
        }

        // 处理其他表达式类型（方法调用、成员访问等）作为原子条件
        return CreateAtomicDnf<T>(expression);
    }

    /// <summary>
    /// 合并两个 DNF 表达式的 OR 组
    /// (A||B) || (C||D) = (A||B||C||D)
    /// </summary>
    private static DnfExpression<T> MergeOrGroups<T>(DnfExpression<T>? left, DnfExpression<T>? right)
    {
        var result = new DnfExpression<T>();
        
        if (left != null && left.OrGroups.Count > 0)
        {
            result.OrGroups.AddRange(left.OrGroups);
        }
        
        if (right != null && right.OrGroups.Count > 0)
        {
            result.OrGroups.AddRange(right.OrGroups);
        }
        
        // 如果两边都为空，返回 null
        if (result.OrGroups.Count == 0)
        {
            return left ?? right ?? new DnfExpression<T>();
        }
        
        return result;
    }

    /// <summary>
    /// 合并两个 DNF 表达式的 AND 条件
    /// (A||B) && (C||D) = (A&&C)||(A&&D)||(B&&C)||(B&&D)
    /// </summary>
    private static DnfExpression<T> MergeAndConditions<T>(DnfExpression<T>? left, DnfExpression<T>? right)
    {
        var result = new DnfExpression<T>();
        
        // 如果左边为空，返回右边
        if (left == null || left.OrGroups.Count == 0)
        {
            return right ?? new DnfExpression<T>();
        }
        
        // 如果右边为空，返回左边
        if (right == null || right.OrGroups.Count == 0)
        {
            return left;
        }
        
        // 交叉组合：每个左边的 OR 组与每个右边的 OR 组组合
        foreach (var leftGroup in left.OrGroups)
        {
            foreach (var rightGroup in right.OrGroups)
            {
                // 合并两个 AND 条件组
                var mergedGroup = new AndConditionGroup<T>();
                mergedGroup.Conditions.AddRange(leftGroup.Conditions);
                mergedGroup.Conditions.AddRange(rightGroup.Conditions);
                result.OrGroups.Add(mergedGroup);
            }
        }
        
        return result;
    }

    /// <summary>
    /// 创建原子条件的 DNF 表达式
    /// 原子条件（如 x.Field == value）转换为 DNF 格式：只有一个 OR 组，该组只有一个 AND 条件
    /// </summary>
    private static DnfExpression<T>? CreateAtomicDnf<T>(Expression expression)
    {
        // 尝试解析为查询条件
        var condition = ParseAtomicCondition<T>(expression);
        if (condition == null)
        {
            return null;
        }
        
        // 创建 DNF 表达式：一个 OR 组包含一个 AND 条件
        var result = new DnfExpression<T>();
        var group = new AndConditionGroup<T>();
        group.Conditions.Add(condition);
        result.OrGroups.Add(group);
        
        return result;
    }

    /// <summary>
    /// 解析原子条件（比较表达式、方法调用等）
    /// </summary>
    private static QueryCondition<T>? ParseAtomicCondition<T>(Expression expression)
    {
        // 处理一元表达式（类型转换 / 逻辑非）
        if (expression is UnaryExpression unary)
        {
            if (unary.NodeType == ExpressionType.Convert)
            {
                return ParseAtomicCondition<T>(unary.Operand);
            }

            // 逻辑非：将条件标记为取反
            if (unary.NodeType == ExpressionType.Not)
            {
                var innerCondition = ParseAtomicCondition<T>(unary.Operand);
                if (innerCondition == null)
                {
                    return null;
                }

                innerCondition.IsNegated = !innerCondition.IsNegated;
                return innerCondition;
            }
        }

        // 处理比较表达式
        if (expression is BinaryExpression binary && IsComparisonOperator(binary.NodeType))
        {
            var comparisonType = GetComparisonType(binary.NodeType);
            if (comparisonType == null)
            {
                return null;
            }
            
            var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(binary.Left, binary.Right);
            if (string.IsNullOrEmpty(fieldPath) || value == null)
            {
                return null;
            }
            
            // 对于精确匹配，需要判断是否使用 keyword
            var finalFieldPath = comparisonType == ComparisonType.Equals || comparisonType == ComparisonType.NotEquals
                ? GetFieldPathForExactMatch(fieldPath, lastProperty)
                : GetFieldPathForRangeQuery(fieldPath, lastProperty, value);
            
            return new QueryCondition<T>
            {
                FieldPath = finalFieldPath,
                NestedPath = nestedPath,
                LastProperty = lastProperty,
                ComparisonType = comparisonType.Value,
                Value = value,
                ConditionType = ConditionType.Comparison
            };
        }

        // 处理方法调用（Contains, StartsWith, EndsWith 等）
        if (expression is MethodCallExpression methodCall)
        {
            return ParseMethodCallCondition<T>(methodCall);
        }

        // 处理成员访问（布尔字段的直接引用）
        if (expression is MemberExpression member)
        {
            return ParseMemberCondition<T>(member);
        }

        return null;
    }

    /// <summary>
    /// 解析方法调用条件
    /// </summary>
    private static QueryCondition<T>? ParseMethodCallCondition<T>(MethodCallExpression methodCall)
    {
        var methodName = methodCall.Method.Name;
        
        if (methodName == "Contains")
        {
            if (methodCall.Object != null)
            {
                // 形式1：field.Contains(value)
                var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
                if (!string.IsNullOrEmpty(fieldPath) && value != null)
                {
                    // 如果值是集合，应该走 terms 查询，避免把集合 ToString 变成类型名
                    if (value is IEnumerable enumerable && value is not string)
                    {
                        var finalFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                        return new QueryCondition<T>
                        {
                            FieldPath = finalFieldPath,
                            NestedPath = nestedPath,
                            LastProperty = lastProperty,
                            Value = enumerable,
                            ConditionType = ConditionType.Terms
                        };
                    }

                    // Contains 优先走 keyword 子字段，避免分词导致误匹配
                    var exactMatchFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                    var useKeywordSubField = !string.Equals(exactMatchFieldPath, fieldPath, StringComparison.Ordinal);
                    
                    if (useKeywordSubField || IsKeywordField(lastProperty))
                    {
                        return new QueryCondition<T>
                        {
                            FieldPath = exactMatchFieldPath,
                            NestedPath = nestedPath,
                            LastProperty = lastProperty,
                            Value = value,
                            ConditionType = ConditionType.Wildcard,
                            WildcardPattern = $"*{value}*"
                        };
                    }

                    return new QueryCondition<T>
                    {
                        FieldPath = fieldPath,
                        NestedPath = nestedPath,
                        LastProperty = lastProperty,
                        Value = value,
                        ConditionType = ConditionType.Match,
                        MatchText = value.ToString()
                    };
                }
            }
            else if (methodCall.Arguments.Count == 2)
            {
                // 形式2：collection.Contains(field)
                var collection = EvaluateExpression(methodCall.Arguments[0]);
                var (fieldPath, nestedPath, lastProperty) = ExtractFieldFromExpression<T>(methodCall.Arguments[1]);
                
                if (!string.IsNullOrEmpty(fieldPath) && collection is IEnumerable enumerable)
                {
                    var finalFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                    return new QueryCondition<T>
                    {
                        FieldPath = finalFieldPath,
                        NestedPath = nestedPath,
                        LastProperty = lastProperty,
                        Value = enumerable,
                        ConditionType = ConditionType.Terms
                    };
                }
            }
        }
        else if (methodName == "StartsWith")
        {
            if (methodCall.Object != null && methodCall.Arguments.Count > 0)
            {
                var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
                if (!string.IsNullOrEmpty(fieldPath) && value != null)
                {
                    // StartsWith 优先走 keyword 子字段，避免分词导致误匹配
                    var exactMatchFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                    var useKeywordSubField = !string.Equals(exactMatchFieldPath, fieldPath, StringComparison.Ordinal);
                    
                    if (useKeywordSubField || IsKeywordField(lastProperty))
                    {
                        return new QueryCondition<T>
                        {
                            FieldPath = exactMatchFieldPath,
                            NestedPath = nestedPath,
                            LastProperty = lastProperty,
                            Value = value,
                            ConditionType = ConditionType.Wildcard,
                            WildcardPattern = $"{value}*"
                        };
                    }

                    return new QueryCondition<T>
                    {
                        FieldPath = fieldPath,
                        NestedPath = nestedPath,
                        LastProperty = lastProperty,
                        Value = value,
                        ConditionType = ConditionType.MatchPhrasePrefix,
                        MatchText = value.ToString()
                    };
                }
            }
        }
        else if (methodName == "EndsWith")
        {
            if (methodCall.Object != null && methodCall.Arguments.Count > 0)
            {
                var (fieldPath, nestedPath, lastProperty, value) = ExtractFieldAndValue<T>(methodCall.Object, methodCall.Arguments[0]);
                if (!string.IsNullOrEmpty(fieldPath) && value != null)
                {
                    // EndsWith 优先走 keyword 子字段，避免分词导致误匹配
                    var exactMatchFieldPath = GetFieldPathForExactMatch(fieldPath, lastProperty);
                    var useKeywordSubField = !string.Equals(exactMatchFieldPath, fieldPath, StringComparison.Ordinal);
                    var finalFieldPath = (useKeywordSubField || IsKeywordField(lastProperty))
                        ? exactMatchFieldPath
                        : fieldPath;

                    return new QueryCondition<T>
                    {
                        FieldPath = finalFieldPath,
                        NestedPath = nestedPath,
                        LastProperty = lastProperty,
                        Value = value,
                        ConditionType = ConditionType.Wildcard,
                        WildcardPattern = $"*{value}"
                    };
                }
            }
        }
        else if (methodName == "Any")
        {
            if (TryExtractAnySource(methodCall, out var collectionExpression, out var predicate))
            {
                var (collectionFieldPath, collectionNestedPath, collectionProperty) = ExtractFieldFromExpression<T>(collectionExpression);
                if (!string.IsNullOrEmpty(collectionFieldPath) && predicate != null)
                {
                    return ParseAnyPredicate<T>(predicate, collectionFieldPath, collectionNestedPath, collectionProperty);
                }
            }
        }
        
        return null;
    }

    /// <summary>
    /// 解析成员访问条件（布尔字段的直接引用）
    /// </summary>
    private static QueryCondition<T>? ParseMemberCondition<T>(MemberExpression member)
    {
        Type? memberType = null;
        
        if (member.Member is PropertyInfo propertyInfo)
        {
            memberType = propertyInfo.PropertyType;
        }
        else if (member.Member is FieldInfo fieldInfo)
        {
            memberType = fieldInfo.FieldType;
        }

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

        var (fieldPath, nestedPath, lastProperty) = ExtractFieldFromExpression<T>(member);
        if (string.IsNullOrEmpty(fieldPath))
        {
            return null;
        }

        return new QueryCondition<T>
        {
            FieldPath = fieldPath,
            NestedPath = nestedPath,
            LastProperty = lastProperty,
            ComparisonType = ComparisonType.Equals,
            Value = true,
            ConditionType = ConditionType.Comparison
        };
    }

    /// <summary>
    /// 判断是否为比较运算符
    /// </summary>
    private static bool IsComparisonOperator(ExpressionType nodeType)
    {
        return nodeType == ExpressionType.Equal ||
               nodeType == ExpressionType.NotEqual ||
               nodeType == ExpressionType.GreaterThan ||
               nodeType == ExpressionType.GreaterThanOrEqual ||
               nodeType == ExpressionType.LessThan ||
               nodeType == ExpressionType.LessThanOrEqual;
    }

    /// <summary>
    /// 获取比较类型
    /// </summary>
    private static ComparisonType? GetComparisonType(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => ComparisonType.Equals,
            ExpressionType.NotEqual => ComparisonType.NotEquals,
            ExpressionType.GreaterThan => ComparisonType.GreaterThan,
            ExpressionType.GreaterThanOrEqual => ComparisonType.GreaterThanOrEqual,
            ExpressionType.LessThan => ComparisonType.LessThan,
            ExpressionType.LessThanOrEqual => ComparisonType.LessThanOrEqual,
            _ => null
        };
    }

    /// <summary>
    /// 从 DNF 表达式生成查询
    /// 优化：按嵌套路径对 OR 组进行分组，属于相同嵌套路径的 OR 组会合并到一个 nested 查询中，减少 nested 查询数量以提高性能
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildQueryFromDnf<T>(DnfExpression<T> dnf)
    {
        if (dnf.OrGroups.Count == 0)
        {
            return _ => { };
        }

        // 如果只有一个 OR 组，直接生成该组的查询
        if (dnf.OrGroups.Count == 1)
        {
            return BuildAndGroupQuery<T>(dnf.OrGroups[0]);
        }

        // 按嵌套路径对 OR 组进行分组
        // 对于属于相同嵌套路径的 OR 组，合并到一个 nested 查询中
        // 对于包含多个嵌套路径或非嵌套条件的 OR 组，独立处理
        var queryActions = new List<Action<QueryDescriptor<T>>>();
        
        // 按嵌套路径分组 OR 组
        var nestedGroups = dnf.OrGroups
            .Select(group => new { Group = group, NestedPath = GetGroupNestedPath<T>(group) })
            .GroupBy(x => x.NestedPath ?? "__NON_NESTED__")
            .ToList();

        foreach (var nestedGroup in nestedGroups)
        {
            var nestedPath = nestedGroup.Key;
            var groups = nestedGroup.Select(x => x.Group).ToList();

            if (nestedPath == "__NON_NESTED__")
            {
                // 非嵌套路径的 OR 组，独立处理
                foreach (var group in groups)
                {
                    queryActions.Add(BuildAndGroupQuery<T>(group));
                }
            }
            else
            {
                // 相同嵌套路径的 OR 组，合并到一个 nested 查询中
                if (groups.Count == 1)
                {
                    // 只有一个 OR 组，直接生成嵌套查询
                    queryActions.Add(BuildAndGroupQuery<T>(groups[0]));
                }
                else
                {
                    // 多个 OR 组，合并到一个 nested 查询中
                    queryActions.Add(query =>
                    {
                        query.Nested(n => n
                            .Path(nestedPath)
                            .Query(nq =>
                            {
                                // 为每个 OR 组生成查询（相对于嵌套路径）
                                var shouldActions = groups
                                    .Select(group => BuildAndGroupQueryForNested<T>(group, nestedPath))
                                    .ToArray();
                                
                                if (shouldActions.Length == 1)
                                {
                                    shouldActions[0](nq);
                                }
                                else
                                {
                                    nq.Bool(b => b.Should(shouldActions));
                                }
                            })
                        );
                    });
                }
            }
        }

        // 组合所有查询
        if (queryActions.Count == 0)
        {
            return _ => { };
        }

        if (queryActions.Count == 1)
        {
            return queryActions[0];
        }

        return query => query.Bool(b => b.Should(queryActions.ToArray()));
    }

    /// <summary>
    /// 获取 OR 组的嵌套路径
    /// 如果 OR 组内所有条件都属于相同的嵌套路径，返回该嵌套路径；否则返回 null
    /// 如果 OR 组内包含多个不同的嵌套路径或混合嵌套和非嵌套条件，返回 null
    /// </summary>
    private static string? GetGroupNestedPath<T>(AndConditionGroup<T> group)
    {
        if (group.Conditions.Count == 0)
        {
            return null;
        }

        string? groupNestedPath = null;
        bool hasNonNested = false;

        foreach (var condition in group.Conditions)
        {
            // 含有逻辑非的嵌套条件，不参与嵌套路径合并，避免语义错误
            if (condition.IsNegated && !string.IsNullOrEmpty(condition.NestedPath))
            {
                return null;
            }

            if (string.IsNullOrEmpty(condition.NestedPath))
            {
                // 该条件不是嵌套条件
                hasNonNested = true;
            }
            else
            {
                // 该条件是嵌套条件
                if (groupNestedPath == null)
                {
                    // 第一个嵌套条件，记录嵌套路径
                    groupNestedPath = condition.NestedPath;
                }
                else if (groupNestedPath != condition.NestedPath)
                {
                    // 该 OR 组内有多个不同的嵌套路径，返回 null 表示无法合并
                    return null;
                }
            }
        }

        // 如果该 OR 组既有嵌套条件又有非嵌套条件，返回 null 表示无法合并
        if (hasNonNested && groupNestedPath != null)
        {
            return null;
        }

        // 返回嵌套路径（如果所有条件都属于相同的嵌套路径）
        return groupNestedPath;
    }

    /// <summary>
    /// 检查所有 OR 组是否都属于相同的嵌套路径
    /// 如果所有 OR 组都属于相同的嵌套路径（或都没有嵌套路径），返回该嵌套路径；否则返回 null
    /// </summary>
    private static string? GetCommonNestedPath<T>(List<AndConditionGroup<T>> orGroups)
    {
        if (orGroups.Count == 0)
        {
            return null;
        }

        string? commonNestedPath = null;
        bool hasNonNestedConditions = false;

        foreach (var group in orGroups)
        {
            // 检查该 OR 组内的所有条件
            string? groupNestedPath = null;
            bool groupHasNonNested = false;

            foreach (var condition in group.Conditions)
            {
                // 含有逻辑非的嵌套条件，不参与公共嵌套路径判断
                if (condition.IsNegated && !string.IsNullOrEmpty(condition.NestedPath))
                {
                    return null;
                }

                if (string.IsNullOrEmpty(condition.NestedPath))
                {
                    // 该条件不是嵌套条件
                    groupHasNonNested = true;
                }
                else
                {
                    // 该条件是嵌套条件
                    if (groupNestedPath == null)
                    {
                        // 第一个嵌套条件，记录嵌套路径
                        groupNestedPath = condition.NestedPath;
                    }
                    else if (groupNestedPath != condition.NestedPath)
                    {
                        // 该 OR 组内有多个不同的嵌套路径，无法合并
                        return null;
                    }
                }
            }

            // 如果该 OR 组既有嵌套条件又有非嵌套条件，无法合并
            if (groupHasNonNested && groupNestedPath != null)
            {
                return null;
            }

            // 如果该 OR 组有非嵌套条件
            if (groupHasNonNested)
            {
                hasNonNestedConditions = true;
                // 如果之前已经有嵌套路径，无法合并
                if (commonNestedPath != null)
                {
                    return null;
                }
            }

            // 如果该 OR 组有嵌套路径
            if (groupNestedPath != null)
            {
                // 如果之前已经有非嵌套条件，无法合并
                if (hasNonNestedConditions)
                {
                    return null;
                }

                if (commonNestedPath == null)
                {
                    // 第一个有嵌套路径的 OR 组，记录嵌套路径
                    commonNestedPath = groupNestedPath;
                }
                else if (commonNestedPath != groupNestedPath)
                {
                    // 不同的嵌套路径，无法合并
                    return null;
                }
            }
        }

        // 返回公共嵌套路径（如果所有 OR 组都属于相同的嵌套路径）
        // 注意：如果所有 OR 组都是非嵌套条件（commonNestedPath == null），返回 null，表示不需要合并到 nested 查询
        return commonNestedPath;
    }

    /// <summary>
    /// 为嵌套查询生成 AND 条件组的查询
    /// 与 BuildAndGroupQuery 类似，但假设所有条件都属于指定的嵌套路径
    /// 生成的查询是相对于嵌套路径的，不需要再包装 nested 查询
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildAndGroupQueryForNested<T>(AndConditionGroup<T> group, string nestedPath)
    {
        if (group.Conditions.Count == 0)
        {
            return _ => { };
        }

        // 如果只有一个条件，直接生成该条件的查询（相对于嵌套路径）
        if (group.Conditions.Count == 1)
        {
            var condition = group.Conditions[0];
            var fullFieldPath = $"{nestedPath}.{condition.FieldPath}";
            return query => ApplyConditionToQueryWithNegation(query, fullFieldPath, condition);
        }

        // 多个条件，使用 Bool.Must 组合（相对于嵌套路径）
        var queryActions = group.Conditions.Select(condition =>
        {
            var fullFieldPath = $"{nestedPath}.{condition.FieldPath}";
            return new Action<QueryDescriptor<T>>(q => ApplyConditionToQueryWithNegation(q, fullFieldPath, condition));
        }).ToArray();

        return query => query.Bool(b => b.Must(queryActions));
    }

    /// <summary>
    /// 生成 AND 条件组的查询
    /// 对于相同嵌套路径的条件，会合并到同一个 nested 查询中
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildAndGroupQuery<T>(AndConditionGroup<T> group)
    {
        if (group.Conditions.Count == 0)
        {
            return _ => { };
        }

        // 如果只有一个条件，直接生成该条件的查询
        if (group.Conditions.Count == 1)
        {
            return BuildConditionQuery<T>(group.Conditions[0]);
        }

        // 按嵌套路径分组条件
        var nestedGroups = group.Conditions
            .Where(c => !string.IsNullOrEmpty(c.NestedPath) && !c.IsNegated)
            .GroupBy(c => c.NestedPath!)
            .ToList();

        var regularConditions = group.Conditions
            .Where(c => string.IsNullOrEmpty(c.NestedPath) || c.IsNegated)
            .ToList();

        var queryActions = new List<Action<QueryDescriptor<T>>>();

        // 处理嵌套查询：相同嵌套路径的条件合并到一个 nested 查询中
        foreach (var nestedGroup in nestedGroups)
        {
            var nestedPath = nestedGroup.Key;
            var conditions = nestedGroup.ToList();

            if (conditions.Count == 1)
            {
                // 只有一个条件，直接生成嵌套查询
                var condition = conditions[0];
                queryActions.Add(query =>
                {
                    var fullFieldPath = $"{nestedPath}.{condition.FieldPath}";
                    query.Nested(n => n
                        .Path(nestedPath)
                        .Query(nq => ApplyConditionToQueryWithNegation(nq, fullFieldPath, condition))
                    );
                });
            }
            else
            {
                // 多个条件，合并到同一个 nested 查询中
                queryActions.Add(query =>
                {
                    query.Nested(n => n
                        .Path(nestedPath)
                        .Query(nq =>
                        {
                            var nestedQueryActions = conditions.Select(condition =>
                            {
                                var fullFieldPath = $"{nestedPath}.{condition.FieldPath}";
                                return new Action<QueryDescriptor<T>>(nq2 => ApplyConditionToQueryWithNegation(nq2, fullFieldPath, condition));
                            }).ToArray();

                            if (nestedQueryActions.Length == 1)
                            {
                                nestedQueryActions[0](nq);
                            }
                            else
                            {
                                nq.Bool(b => b.Must(nestedQueryActions));
                            }
                        })
                    );
                });
            }
        }

        // 处理普通查询（非嵌套）
        foreach (var condition in regularConditions)
        {
            queryActions.Add(BuildConditionQuery<T>(condition));
        }

        // 组合所有查询
        if (queryActions.Count == 0)
        {
            return _ => { };
        }

        if (queryActions.Count == 1)
        {
            return queryActions[0];
        }

        return query => query.Bool(b => b.Must(queryActions.ToArray()));
    }

    /// <summary>
    /// 生成单个条件的查询
    /// </summary>
    private static Action<QueryDescriptor<T>> BuildConditionQuery<T>(QueryCondition<T> condition)
    {
        if (!string.IsNullOrEmpty(condition.NestedPath))
        {
            // 嵌套查询
            var fullFieldPath = $"{condition.NestedPath}.{condition.FieldPath}";
            return query =>
            {
                // 逻辑非的嵌套条件：需要在外层使用 must_not 包裹 nested 查询
                if (condition.IsNegated)
                {
                    query.Bool(b => b.MustNot(mn => mn.Nested(n => n
                        .Path(condition.NestedPath)
                        .Query(nq => ApplyConditionToQuery(nq, fullFieldPath, condition))
                    )));
                }
                else
                {
                    query.Nested(n => n
                        .Path(condition.NestedPath)
                        .Query(nq => ApplyConditionToQuery(nq, fullFieldPath, condition))
                    );
                }
            };
        }
        else
        {
            // 普通查询
            return query => ApplyConditionToQueryWithNegation(query, condition.FieldPath, condition);
        }
    }

    /// <summary>
    /// 应用条件到查询描述符（支持逻辑非）
    /// 逻辑非通过 must_not 包裹原子条件，避免丢失条件或错误命中
    /// </summary>
    private static void ApplyConditionToQueryWithNegation<T>(QueryDescriptor<T> query, string fieldPath, QueryCondition<T> condition)
    {
        if (condition.IsNegated)
        {
            query.Bool(b => b.MustNot(mn => ApplyConditionToQuery(mn, fieldPath, condition)));
            return;
        }

        ApplyConditionToQuery(query, fieldPath, condition);
    }

    /// <summary>
    /// 应用条件到查询描述符
    /// </summary>
    private static void ApplyConditionToQuery<T>(QueryDescriptor<T> query, string fieldPath, QueryCondition<T> condition)
    {
        switch (condition.ConditionType)
        {
            case ConditionType.Comparison:
                ApplyComparisonToQuery(query, fieldPath, condition.ComparisonType!.Value, condition.Value!, condition.LastProperty);
                break;

            case ConditionType.Match:
                query.Match(m => m.Field(fieldPath).Query(condition.MatchText ?? string.Empty));
                break;

            case ConditionType.MatchPhrasePrefix:
                query.MatchPhrasePrefix(m => m.Field(fieldPath).Query(condition.MatchText ?? string.Empty));
                break;

            case ConditionType.Wildcard:
                query.Wildcard(w => w.Field(fieldPath).Value(condition.WildcardPattern ?? string.Empty));
                break;

            case ConditionType.Terms:
                if (condition.Value is IEnumerable enumerable)
                {
                    var valueList = enumerable.Cast<object>().ToList();
                    if (valueList.Any())
                    {
                        var fieldValues = ConvertToFieldValues(valueList, condition.LastProperty);
                        var termsQueryField = new TermsQueryField(fieldValues);
                        // 使用 Values 方法而不是 Terms 方法
                        query.Terms(ts => ts.Field(fieldPath).Terms(termsQueryField));
                    }
                }
                break;
            case ConditionType.CustomQuery:
                condition.CustomQueryAction?.Invoke(query);
                break;
        }
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

/// <summary>
/// 条件类型枚举
/// </summary>
internal enum ConditionType
{
    Comparison,          // 比较查询（==, !=, >, <, >=, <=）
    Match,               // Match 查询（用于 text 类型字段）
    MatchPhrasePrefix,   // Match Phrase Prefix 查询（用于 StartsWith）
    Wildcard,            // Wildcard 查询（用于 Contains、EndsWith）
    Terms,               // Terms 查询（用于 In 查询）
    CustomQuery          // 自定义查询（用于复杂 Any 等）
}

/// <summary>
/// DNF（析取范式）表达式
/// 格式：(a&&b&&c)||(d&&e&&f)||(g.h&&i)
/// 顶层是 OR 关系（OrGroups），每个 OR 分支是一个 AND 条件组（AndConditions）
/// </summary>
internal class DnfExpression<T>
{
    /// <summary>
    /// OR 组列表，每个组是一个 AND 条件组
    /// </summary>
    public List<AndConditionGroup<T>> OrGroups { get; } = new();
}

/// <summary>
/// 布尔树节点基类
/// 用于在不做 DNF 展开的情况下保留表达式结构
/// </summary>
internal abstract class BoolNode<T>
{
}

/// <summary>
/// 原子节点（单个查询条件）
/// </summary>
internal sealed class AtomicBoolNode<T> : BoolNode<T>
{
    public AtomicBoolNode(QueryCondition<T> condition)
    {
        Condition = condition;
    }

    public QueryCondition<T> Condition { get; }
}

/// <summary>
/// AND 节点
/// </summary>
internal sealed class AndBoolNode<T> : BoolNode<T>
{
    public List<BoolNode<T>> Children { get; } = new();
}

/// <summary>
/// OR 节点
/// </summary>
internal sealed class OrBoolNode<T> : BoolNode<T>
{
    public List<BoolNode<T>> Children { get; } = new();
}

/// <summary>
/// AND 条件组
/// 包含多个通过 AND 连接的查询条件
/// </summary>
internal class AndConditionGroup<T>
{
    /// <summary>
    /// AND 条件列表
    /// </summary>
    public List<QueryCondition<T>> Conditions { get; } = new();
}

    /// <summary>
    /// 查询条件
    /// 表示一个原子查询条件（如 x.Field == value, x.Field.Contains("text") 等）
    /// </summary>
internal class QueryCondition<T>
{
    /// <summary>
    /// 字段路径（相对于嵌套路径，如果存在嵌套路径）
    /// </summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>
    /// 嵌套路径（如果字段在嵌套文档中）
    /// </summary>
    public string? NestedPath { get; set; }

    /// <summary>
    /// 最后一个属性的 PropertyInfo（用于获取字段类型信息）
    /// </summary>
    public PropertyInfo? LastProperty { get; set; }

    /// <summary>
    /// 比较类型（仅用于比较查询）
    /// </summary>
    public ComparisonType? ComparisonType { get; set; }

    /// <summary>
    /// 条件值
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 条件类型
    /// </summary>
    public ConditionType ConditionType { get; set; }

    /// <summary>
    /// 是否为逻辑非条件（如 !x.Field.Contains(...)）
    /// </summary>
    public bool IsNegated { get; set; }

    /// <summary>
    /// Wildcard 模式（用于 Wildcard 查询）
    /// </summary>
    public string? WildcardPattern { get; set; }

    /// <summary>
    /// Match 文本（用于 Match 和 MatchPhrasePrefix 查询）
    /// </summary>
    public string? MatchText { get; set; }

    /// <summary>
    /// 自定义查询动作（用于复杂条件）
    /// </summary>
    public Action<QueryDescriptor<T>>? CustomQueryAction { get; set; }
}



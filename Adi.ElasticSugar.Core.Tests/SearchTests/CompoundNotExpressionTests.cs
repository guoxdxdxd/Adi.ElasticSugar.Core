using System.Linq.Expressions;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 复合逻辑非（!(A &amp;&amp; B &amp;&amp; C)）解析测试，不依赖 Elasticsearch。
/// </summary>
public class CompoundNotExpressionTests
{
    /// <summary>
    /// !(A &amp;&amp; B &amp;&amp; C &amp;&amp; D) 应能解析为非空查询动作（修复前会静默返回 null 并被 AND 丢弃）。
    /// </summary>
    [Fact]
    public void ParseExpression_NotOfCompoundAnd_ShouldReturnQueryAction()
    {
        Expression<Func<TestDocument, bool>> expression = x =>
            !(x.IntField == 10
              && x.BoolField == true
              && x.IntField >= 5
              && x.TextField != "");

        var action = ExpressionParser.ParseExpression(expression);

        action.Should().NotBeNull();
    }

    /// <summary>
    /// !(A || B) 也应能解析。
    /// </summary>
    [Fact]
    public void ParseExpression_NotOfCompoundOr_ShouldReturnQueryAction()
    {
        Expression<Func<TestDocument, bool>> expression = x =>
            !(x.IntField == 10 || x.BoolField == true);

        var action = ExpressionParser.ParseExpression(expression);

        action.Should().NotBeNull();
    }

    /// <summary>
    /// 外层 AND 串联复合 Not 时，不应因 Not 子树为 null 而丢掉该段条件。
    /// </summary>
    [Fact]
    public void ParseExpression_AndWithNotOfCompoundAnd_ShouldReturnQueryAction()
    {
        Expression<Func<TestDocument, bool>> expression = x =>
            x.IntField > 0
            && !(x.IntField == 10 && x.BoolField == true && x.DoubleField > 1)
            && x.BoolField == false;

        var action = ExpressionParser.ParseExpression(expression);

        action.Should().NotBeNull();
    }

    /// <summary>
    /// 原子 Not（如 !x.BoolField）仍应可用。
    /// </summary>
    [Fact]
    public void ParseExpression_NotOfAtomicBool_ShouldReturnQueryAction()
    {
        Expression<Func<TestDocument, bool>> expression = x => !x.BoolField;

        var action = ExpressionParser.ParseExpression(expression);

        action.Should().NotBeNull();
    }
}

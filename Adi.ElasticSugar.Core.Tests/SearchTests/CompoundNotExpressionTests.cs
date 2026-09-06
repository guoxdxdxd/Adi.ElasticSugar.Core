using System.Linq.Expressions;
using System.Text;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
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

    /// <summary>
    /// !x.BoolField 应生成 term false（与 == false 一致），而不是 must_not term true。
    /// </summary>
    [Fact]
    public void ParseExpression_NotOfAtomicBool_ShouldSerializeAsTermFalse()
    {
        Expression<Func<TestDocument, bool>> expression = x => !x.BoolField;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"boolField\"");
        json.Should().Contain("\"value\":false");
        json.Should().NotContain("must_not");
        json.Should().NotContain("\"value\":true");
    }

    /// <summary>
    /// x.BoolField != true 对非可空 bool 也应生成 term false。
    /// </summary>
    [Fact]
    public void ParseExpression_BoolNotEqualsTrue_ShouldSerializeAsTermFalse()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.BoolField != true;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"value\":false");
        json.Should().NotContain("must_not");
    }

    /// <summary>
    /// x.BoolField == false 保持 term false（对照基线）。
    /// </summary>
    [Fact]
    public void ParseExpression_BoolEqualsFalse_ShouldSerializeAsTermFalse()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.BoolField == false;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"value\":false");
        json.Should().NotContain("must_not");
    }

    /// <summary>
    /// 可空 bool? != true 仍用 must_not，以保留命中 null/缺字段的语义。
    /// </summary>
    [Fact]
    public void ParseExpression_NullableBoolNotEqualsTrue_ShouldKeepMustNot()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.NullableBoolField != true;

        var json = SerializeQuery(expression);

        json.Should().Contain("must_not");
        json.Should().Contain("\"value\":true");
    }

    private static string SerializeQuery(Expression<Func<TestDocument, bool>> expression)
    {
        var action = ExpressionParser.ParseExpression(expression);
        action.Should().NotBeNull();

        var client = new ElasticsearchClient(new Uri("http://localhost:9200"));
        var descriptor = new SearchRequestDescriptor<TestDocument>();
        descriptor.Index("test").Query(action!);

        using var stream = new MemoryStream();
        client.RequestResponseSerializer.Serialize(descriptor, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

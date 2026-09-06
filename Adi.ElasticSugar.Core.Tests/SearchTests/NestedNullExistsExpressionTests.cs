using System.Linq.Expressions;
using System.Text;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// nested 根字段空值判断（!= null / == null）的 DSL 翻译测试，不依赖 Elasticsearch。
/// 修复前会生成根级 exists，对 nested 映射无效导致 0 命中。
/// </summary>
public class NestedNullExistsExpressionTests
{
    /// <summary>
    /// x.Items != null 应翻译为 nested(path=items) + match_all，而不是根级 exists(items)。
    /// </summary>
    [Fact]
    public void ParseExpression_NestedCollection_NotNull_ShouldSerializeAsNestedMatchAll()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.Items != null;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"nested\"");
        json.Should().Contain("\"path\":\"items\"");
        json.Should().Contain("\"match_all\"");
        json.Should().NotContain("\"exists\"");
    }

    /// <summary>
    /// x.Items == null 应翻译为 must_not nested(path=items, match_all)。
    /// </summary>
    [Fact]
    public void ParseExpression_NestedCollection_EqualsNull_ShouldSerializeAsMustNotNestedMatchAll()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.Items == null;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"must_not\"");
        json.Should().Contain("\"nested\"");
        json.Should().Contain("\"path\":\"items\"");
        json.Should().Contain("\"match_all\"");
        json.Should().NotContain("\"exists\"");
    }

    /// <summary>
    /// x.Address != null（嵌套对象）同样应走 nested + match_all。
    /// </summary>
    [Fact]
    public void ParseExpression_NestedObject_NotNull_ShouldSerializeAsNestedMatchAll()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.Address != null;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"nested\"");
        json.Should().Contain("\"path\":\"address\"");
        json.Should().Contain("\"match_all\"");
        json.Should().NotContain("\"exists\"");
    }

    /// <summary>
    /// nested 根字段 != null 与 Any 条件并存时，不得出现根级 exists。
    /// 对齐商城订单大额快筛：DetailEsDto != null &amp;&amp; DetailEsDto.Any(...)
    /// </summary>
    [Fact]
    public void ParseExpression_NestedCollection_NotNull_AndAny_ShouldNotEmitRootExists()
    {
        Expression<Func<TestDocument, bool>> expression = x =>
            x.Items != null
            && x.Items.Any(i => i.Price >= 100m);

        var json = SerializeQuery(expression);

        json.Should().Contain("\"nested\"");
        json.Should().Contain("\"path\":\"items\"");
        json.Should().NotContain("\"exists\"");
        json.Should().Contain("\"gte\"");
    }

    /// <summary>
    /// 普通可空标量字段 != null 仍应翻译为 exists（回归保护）。
    /// </summary>
    [Fact]
    public void ParseExpression_NullableScalar_NotNull_ShouldStillSerializeAsExists()
    {
        Expression<Func<TestDocument, bool>> expression = x => x.NullableBoolField != null;

        var json = SerializeQuery(expression);

        json.Should().Contain("\"exists\"");
        json.Should().Contain("\"nullableBoolField\"");
        json.Should().NotContain("\"nested\"");
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

using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// Sum 聚合测试
/// 覆盖单字段与多字段聚合，确保表达式解析出的字段名与聚合结果一致
/// </summary>
public class SumAggregationTests : TestBase
{
    private readonly string _tag = $"sum-{Guid.NewGuid():N}";
    private readonly List<TestDocument> _documents = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备一组带唯一标识的测试数据
        // 使用 KeywordField 做过滤，避免与其他测试数据相互影响
        _documents.AddRange(new[]
        {
            new TestDocument
            {
                Id = "sum-1",
                EsDateTime = new DateTime(2024, 1, 15),
                KeywordField = _tag,
                DoubleField = 10.5,
                DecimalField = 100.25m
            },
            new TestDocument
            {
                Id = "sum-2",
                EsDateTime = new DateTime(2024, 1, 15),
                KeywordField = _tag,
                DoubleField = 20.5,
                DecimalField = 200.75m
            },
            new TestDocument
            {
                Id = "sum-3",
                EsDateTime = new DateTime(2024, 1, 15),
                KeywordField = _tag,
                DoubleField = 30.0,
                DecimalField = 300.0m
            }
        });

        // 推送测试数据并刷新索引，确保聚合查询可见
        await Client.PushDocumentsAsync(_documents);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 单字段 Sum 聚合：验证表达式字段解析 + 聚合值计算
    /// </summary>
    [Fact]
    public async Task SumAsync_SingleField_ShouldReturnCorrectValue()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var sum = await Client.Search<TestDocument>(indexName)
            .Where(x => x.KeywordField == _tag)
            .SumAsync(x => x.DoubleField);

        // Assert
        sum.Should().NotBeNull();
        sum!.Value.Should().BeApproximately(61.0, 0.0001);
    }

    /// <summary>
    /// 多字段 Sum 聚合：验证多字段聚合名称与返回字典 key 一致
    /// </summary>
    [Fact]
    public async Task SumAsync_MultipleFields_ShouldReturnDictionaryValues()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var sums = await Client.Search<TestDocument>(indexName)
            .Where(x => x.KeywordField == _tag)
            .SumAsync(x => x.DoubleField, x => x.DecimalField);

        // Assert
        sums.Should().ContainKey("doubleField");
        sums.Should().ContainKey("decimalField");
        sums["doubleField"].Should().NotBeNull();
        sums["decimalField"].Should().NotBeNull();
        sums["doubleField"]!.Value.Should().BeApproximately(61.0, 0.0001);
        sums["decimalField"]!.Value.Should().BeApproximately(601.0, 0.0001);
    }
}

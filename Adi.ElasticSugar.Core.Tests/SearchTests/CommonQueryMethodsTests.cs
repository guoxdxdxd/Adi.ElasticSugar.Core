using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 常用查询方法测试
/// 覆盖 First/Any/Count(predicate)/Avg/Min/Max/GroupBy/Select 等能力
/// </summary>
public class CommonQueryMethodsTests : TestBase
{
    private readonly string _tag = $"common-{Guid.NewGuid():N}";
    private readonly List<TestDocument> _documents = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _documents.AddRange(new[]
        {
            new TestDocument
            {
                Id = "common-1",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = _tag,
                KeywordField = "group-a",
                DoubleField = 10.0,
                IntField = 1
            },
            new TestDocument
            {
                Id = "common-2",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = _tag,
                KeywordField = "group-a",
                DoubleField = 20.0,
                IntField = 2
            },
            new TestDocument
            {
                Id = "common-3",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = _tag,
                KeywordField = "group-b",
                DoubleField = 30.0,
                IntField = 3
            }
        });

        await Client.PushDocumentsAsync(_documents);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ShouldReturnFirstDocumentByOrder()
    {
        var indexName = "test-documents-2024-01";

        var first = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == _tag)
            .OrderBy(x => x.DoubleField)
            .FirstOrDefaultAsync();

        first.Should().NotBeNull();
        first!.DoubleField.Should().Be(10.0);
    }

    [Fact]
    public async Task FirstAsync_NoResult_ShouldThrow()
    {
        var indexName = "test-documents-2024-01";

        var act = async () => await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "not-exists")
            .FirstAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AnyAsync_ShouldReturnTrueWhenExists()
    {
        var indexName = "test-documents-2024-01";

        var exists = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == _tag)
            .AnyAsync();

        var notExists = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "not-exists")
            .AnyAsync();

        exists.Should().BeTrue();
        notExists.Should().BeFalse();
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ShouldReturnFilteredCount()
    {
        var indexName = "test-documents-2024-01";

        var count = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == _tag)
            .CountAsync(x => x.DoubleField >= 20.0);

        count.Should().Be(2);
    }

    [Fact]
    public async Task AvgMinMaxAsync_ShouldReturnCorrectValues()
    {
        var indexName = "test-documents-2024-01";

        var query = Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == _tag);

        var avg = await query.AvgAsync(x => x.DoubleField);
        var min = await query.MinAsync(x => x.DoubleField);
        var max = await query.MaxAsync(x => x.DoubleField);

        avg.Should().NotBeNull();
        min.Should().NotBeNull();
        max.Should().NotBeNull();
        avg!.Value.Should().BeApproximately(20.0, 0.0001);
        min!.Value.Should().BeApproximately(10.0, 0.0001);
        max!.Value.Should().BeApproximately(30.0, 0.0001);
    }

    [Fact]
    public async Task GroupByAsync_ShouldReturnBucketCounts()
    {
        var indexName = "test-documents-2024-01";

        var buckets = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == _tag)
            .GroupByAsync(x => x.KeywordField, size: 10);

        buckets.Should().Contain(x => x.Key == "group-a" && x.Count == 2);
        buckets.Should().Contain(x => x.Key == "group-b" && x.Count == 1);
    }

    [Fact]
    public async Task Select_ShouldOnlyReturnIncludedFields()
    {
        var indexName = "test-documents-2024-01";

        var results = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == _tag)
            .OrderBy(x => x.IntField)
            .Select(x => x.KeywordField, x => x.IntField)
            .ToListAsync();

        results.Should().NotBeEmpty();
        results.All(x => x.DoubleField == 0).Should().BeTrue();
        results.All(x => !string.IsNullOrEmpty(x.KeywordField)).Should().BeTrue();
    }
}

using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 日期时间类型查询测试
/// 测试 DateTime、DateTimeOffset 等日期时间类型字段的各种查询方式
/// </summary>
public class DateTimeQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            new TestDocument { Id = 1, EsDateTime = new DateTime(2024, 1, 15), DateTimeField = baseDate.AddDays(-5), DateTimeOffsetField = new DateTimeOffset(baseDate.AddDays(-5)) },
            new TestDocument { Id = 2, EsDateTime = new DateTime(2024, 1, 15), DateTimeField = baseDate.AddDays(-3), DateTimeOffsetField = new DateTimeOffset(baseDate.AddDays(-3)) },
            new TestDocument { Id = 3, EsDateTime = new DateTime(2024, 1, 15), DateTimeField = baseDate.AddDays(-1), DateTimeOffsetField = new DateTimeOffset(baseDate.AddDays(-1)) },
            new TestDocument { Id = 4, EsDateTime = new DateTime(2024, 1, 15), DateTimeField = baseDate.AddDays(1), DateTimeOffsetField = new DateTimeOffset(baseDate.AddDays(1)) },
            new TestDocument { Id = 5, EsDateTime = new DateTime(2024, 1, 15), DateTimeField = baseDate.AddDays(3), DateTimeOffsetField = new DateTimeOffset(baseDate.AddDays(3)) },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试 DateTime 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var targetDate = _testDocuments[2].DateTimeField;

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField == targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().DateTimeField.Should().BeCloseTo(targetDate, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试 DateTime 字段的大于查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeField_GreaterThan_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var targetDate = baseDate.AddDays(-1);

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField > targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.DateTimeField > targetDate).Should().BeTrue();
    }

    /// <summary>
    /// 测试 DateTime 字段的大于等于查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeField_GreaterThanOrEqual_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var targetDate = baseDate.AddDays(-1);

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField >= targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.DateTimeField >= targetDate).Should().BeTrue();
    }

    /// <summary>
    /// 测试 DateTime 字段的小于查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeField_LessThan_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var targetDate = baseDate.AddDays(1);

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField < targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.DateTimeField < targetDate).Should().BeTrue();
    }

    /// <summary>
    /// 测试 DateTime 字段的小于等于查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeField_LessThanOrEqual_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var targetDate = baseDate.AddDays(1);

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField <= targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(4);
        result.Documents.All(x => x.DateTimeField <= targetDate).Should().BeTrue();
    }

    /// <summary>
    /// 测试 DateTime 字段的范围查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeField_Range_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var startDate = baseDate.AddDays(-3);
        var endDate = baseDate.AddDays(1);

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField >= startDate && x.DateTimeField <= endDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.DateTimeField >= startDate && x.DateTimeField <= endDate).Should().BeTrue();
    }

    /// <summary>
    /// 测试 DateTimeOffset 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeOffsetField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var targetDate = _testDocuments[2].DateTimeOffsetField;

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeOffsetField == targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().DateTimeOffsetField.Should().BeCloseTo(targetDate, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试 DateTimeOffset 字段的范围查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeOffsetField_Range_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var baseDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var startDate = new DateTimeOffset(baseDate.AddDays(-3));
        var endDate = new DateTimeOffset(baseDate.AddDays(1));

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeOffsetField >= startDate && x.DateTimeOffsetField <= endDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.DateTimeOffsetField >= startDate && x.DateTimeOffsetField <= endDate).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空 DateTime 字段的查询
    /// </summary>
    [Fact]
    public async Task Where_NullableDateTimeField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var targetDate = new DateTime(2024, 1, 15, 10, 0, 0);
        var documents = new[]
        {
            new TestDocument { Id = 10, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", NullableDateTimeField = targetDate },
            new TestDocument { Id = 11, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", NullableDateTimeField = null },
            new TestDocument { Id = 12, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", NullableDateTimeField = targetDate },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableDateTimeField == targetDate)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.NullableDateTimeField.HasValue && x.NullableDateTimeField.Value == targetDate).Should().BeTrue();
    }
}


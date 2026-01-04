using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 数值类型查询测试
/// 测试 int、long、double、decimal 等数值类型字段的各种查询方式
/// </summary>
public class NumericQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            new TestDocument { Id = 1, EsDateTime = new DateTime(2024, 1, 15), IntField = 10, LongField = 100L, DoubleField = 10.5, DecimalField = 10.5m },
            new TestDocument { Id = 2, EsDateTime = new DateTime(2024, 1, 15), IntField = 20, LongField = 200L, DoubleField = 20.5, DecimalField = 20.5m },
            new TestDocument { Id = 3, EsDateTime = new DateTime(2024, 1, 15), IntField = 30, LongField = 300L, DoubleField = 30.5, DecimalField = 30.5m },
            new TestDocument { Id = 4, EsDateTime = new DateTime(2024, 1, 15), IntField = 40, LongField = 400L, DoubleField = 40.5, DecimalField = 40.5m },
            new TestDocument { Id = 5, EsDateTime = new DateTime(2024, 1, 15), IntField = 50, LongField = 500L, DoubleField = 50.5, DecimalField = 50.5m },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试 int 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_IntField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField == 20)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().IntField.Should().Be(20);
    }

    /// <summary>
    /// 测试 int 字段的大于查询
    /// </summary>
    [Fact]
    public async Task Where_IntField_GreaterThan_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField > 30)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.IntField > 30).Should().BeTrue();
    }

    /// <summary>
    /// 测试 int 字段的大于等于查询
    /// </summary>
    [Fact]
    public async Task Where_IntField_GreaterThanOrEqual_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField >= 30)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.IntField >= 30).Should().BeTrue();
    }

    /// <summary>
    /// 测试 int 字段的小于查询
    /// </summary>
    [Fact]
    public async Task Where_IntField_LessThan_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField < 30)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.IntField < 30).Should().BeTrue();
    }

    /// <summary>
    /// 测试 int 字段的小于等于查询
    /// </summary>
    [Fact]
    public async Task Where_IntField_LessThanOrEqual_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField <= 30)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.IntField <= 30).Should().BeTrue();
    }

    /// <summary>
    /// 测试 int 字段的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_IntField_NotEquals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField != 20)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(4);
        result.Documents.All(x => x.IntField != 20).Should().BeTrue();
    }

    /// <summary>
    /// 测试 long 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_LongField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.LongField == 200L)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().LongField.Should().Be(200L);
    }

    /// <summary>
    /// 测试 long 字段的范围查询
    /// </summary>
    [Fact]
    public async Task Where_LongField_Range_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.LongField >= 200L && x.LongField <= 400L)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.LongField >= 200L && x.LongField <= 400L).Should().BeTrue();
    }

    /// <summary>
    /// 测试 double 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_DoubleField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DoubleField == 20.5)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().DoubleField.Should().Be(20.5);
    }

    /// <summary>
    /// 测试 double 字段的范围查询
    /// </summary>
    [Fact]
    public async Task Where_DoubleField_Range_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DoubleField > 20.0 && x.DoubleField < 40.0)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().DoubleField.Should().Be(30.5);
    }

    /// <summary>
    /// 测试 decimal 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_DecimalField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DecimalField == 30.5m)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().DecimalField.Should().Be(30.5m);
    }

    /// <summary>
    /// 测试可空 int 字段的查询
    /// </summary>
    [Fact]
    public async Task Where_NullableIntField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument { Id = 10, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", NullableIntField = 100 },
            new TestDocument { Id = 11, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", NullableIntField = null },
            new TestDocument { Id = 12, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", NullableIntField = 100 },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableIntField == 100)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.NullableIntField == 100).Should().BeTrue();
    }

    /// <summary>
    /// 测试 int 字段的 In 查询（使用 Contains 方法）
    /// </summary>
    [Fact]
    public async Task Where_IntField_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var values = new[] { 20, 30, 40 };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => values.Contains(x.IntField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => values.Contains(x.IntField)).Should().BeTrue();
    }
}


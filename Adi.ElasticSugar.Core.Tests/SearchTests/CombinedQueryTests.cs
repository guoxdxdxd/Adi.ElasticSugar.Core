using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 组合查询测试
/// 测试多个条件的组合查询（AND、OR 等）
/// </summary>
public class CombinedQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            new TestDocument { Id = 1, EsDateTime = new DateTime(2024, 1, 15), TextField = "Product A", IntField = 10, DoubleField = 10.5, BoolField = true, DateTimeField = new DateTime(2024, 1, 10) },
            new TestDocument { Id = 2, EsDateTime = new DateTime(2024, 1, 15), TextField = "Product B", IntField = 20, DoubleField = 20.5, BoolField = false, DateTimeField = new DateTime(2024, 1, 12) },
            new TestDocument { Id = 3, EsDateTime = new DateTime(2024, 1, 15), TextField = "Product C", IntField = 30, DoubleField = 30.5, BoolField = true, DateTimeField = new DateTime(2024, 1, 14) },
            new TestDocument { Id = 4, EsDateTime = new DateTime(2024, 1, 15), TextField = "Product D", IntField = 40, DoubleField = 40.5, BoolField = false, DateTimeField = new DateTime(2024, 1, 16) },
            new TestDocument { Id = 5, EsDateTime = new DateTime(2024, 1, 15), TextField = "Product E", IntField = 50, DoubleField = 50.5, BoolField = true, DateTimeField = new DateTime(2024, 1, 18) },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试多个 Where 条件的 AND 组合（链式调用）
    /// </summary>
    [Fact]
    public async Task Where_MultipleConditions_And_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField >= 20)
            .Where(x => x.IntField <= 40)
            .Where(x => x.BoolField == true)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().IntField.Should().Be(30);
        result.Documents.First().BoolField.Should().BeTrue();
    }

    /// <summary>
    /// 测试单个 Where 条件中的 AND 组合（使用 && 运算符）
    /// </summary>
    [Fact]
    public async Task Where_SingleCondition_And_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField >= 20 && x.IntField <= 40 && x.BoolField == true)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().IntField.Should().Be(30);
    }

    /// <summary>
    /// 测试单个 Where 条件中的 OR 组合（使用 || 运算符）
    /// </summary>
    [Fact]
    public async Task Where_SingleCondition_Or_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField == 10 || x.IntField == 30 || x.IntField == 50)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.IntField == 10 || x.IntField == 30 || x.IntField == 50).Should().BeTrue();
    }

    /// <summary>
    /// 测试复杂组合查询（AND 和 OR 混合）
    /// </summary>
    [Fact]
    public async Task Where_ComplexCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - (IntField >= 20 AND IntField <= 40) OR (IntField == 50 AND BoolField == true)
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => (x.IntField >= 20 && x.IntField <= 40) || (x.IntField == 50 && x.BoolField == true))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(4);
        result.Documents.All(x => 
            (x.IntField >= 20 && x.IntField <= 40) || 
            (x.IntField == 50 && x.BoolField == true)
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试字符串和数值类型的组合查询
    /// </summary>
    [Fact]
    public async Task Where_StringAndNumeric_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.Contains("Product") && x.IntField >= 30)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.TextField.Contains("Product") && x.IntField >= 30).Should().BeTrue();
    }

    /// <summary>
    /// 测试日期和数值类型的组合查询
    /// </summary>
    [Fact]
    public async Task Where_DateTimeAndNumeric_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var startDate = new DateTime(2024, 1, 12);
        var endDate = new DateTime(2024, 1, 16);

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.DateTimeField >= startDate && x.DateTimeField <= endDate && x.IntField >= 20)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => 
            x.DateTimeField >= startDate && 
            x.DateTimeField <= endDate && 
            x.IntField >= 20
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试多个字段的 In 查询组合
    /// </summary>
    [Fact]
    public async Task Where_MultipleInQueries_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var intValues = new[] { 20, 30, 40 };
        var keywordValues = new[] { "Product B", "Product C", "Product D" };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => intValues.Contains(x.IntField))
            .Where(x => keywordValues.Contains(x.TextField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => 
            intValues.Contains(x.IntField) && 
            keywordValues.Contains(x.TextField)
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试排序和分页的组合
    /// </summary>
    [Fact]
    public async Task Where_WithOrderByAndPaging_ShouldReturnCorrectResults()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField >= 20)
            .OrderBy(x => x.IntField)
            .Skip(1)
            .Take(2)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        var documents = result.Documents.ToList();
        documents[0].IntField.Should().Be(30);
        documents[1].IntField.Should().Be(40);
    }

    /// <summary>
    /// 测试降序排序
    /// </summary>
    [Fact]
    public async Task Where_WithOrderByDesc_ShouldReturnCorrectResults()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField >= 20)
            .OrderByDesc(x => x.IntField)
            .Take(3)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        var documents = result.Documents.ToList();
        documents[0].IntField.Should().Be(50);
        documents[1].IntField.Should().Be(40);
        documents[2].IntField.Should().Be(30);
    }

    /// <summary>
    /// 测试 TrackTotalHits 功能
    /// </summary>
    [Fact]
    public async Task Where_WithTrackTotalHits_ShouldReturnTotalCount()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.IntField >= 20)
            .TrackTotalHits()
            .Take(2)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Total.Should().BeGreaterOrEqualTo(4); // 至少有 4 条记录满足条件
    }

    /// <summary>
    /// 测试分页功能
    /// </summary>
    [Fact]
    public async Task ToPageAsync_ShouldReturnCorrectPage()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .OrderBy(x => x.IntField)
            .ToPageAsync(2, 2); // 第 2 页，每页 2 条

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        var documents = result.Documents.ToList();
        documents[0].IntField.Should().Be(30);
        documents[1].IntField.Should().Be(40);
    }
}


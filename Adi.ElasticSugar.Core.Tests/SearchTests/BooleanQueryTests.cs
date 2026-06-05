using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 布尔类型查询测试
/// 测试 bool 类型字段的查询方式
/// </summary>
public class BooleanQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            new TestDocument { Id = "1", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", BoolField = true },
            new TestDocument { Id = "2", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", BoolField = false },
            new TestDocument { Id = "3", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", BoolField = true },
            new TestDocument { Id = "4", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test4", BoolField = false },
            new TestDocument { Id = "5", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test5", BoolField = true },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试 bool 字段的等于 true 查询
    /// </summary>
    [Fact]
    public async Task Where_BoolField_EqualsTrue_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.BoolField == true)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.BoolField == true).Should().BeTrue();
    }

    /// <summary>
    /// 测试 bool 字段的等于 false 查询
    /// </summary>
    [Fact]
    public async Task Where_BoolField_EqualsFalse_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.BoolField == false)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.BoolField == false).Should().BeTrue();
    }

    /// <summary>
    /// 测试 bool 字段的简写查询（直接使用字段名）
    /// </summary>
    [Fact]
    public async Task Where_BoolField_Direct_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.BoolField)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.BoolField).Should().BeTrue();
    }

    /// <summary>
    /// 测试 bool 字段的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_BoolField_NotEquals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.BoolField != true)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.BoolField != true).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空 bool 字段的查询
    /// </summary>
    [Fact]
    public async Task Where_NullableBoolField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument { Id = "10", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", NullableBoolField = true },
            new TestDocument { Id = "11", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", NullableBoolField = null },
            new TestDocument { Id = "12", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", NullableBoolField = true },
            new TestDocument { Id = "13", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test4", NullableBoolField = false },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableBoolField == true)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.NullableBoolField == true).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空 bool 字段的非空判断与 In 查询组合。
    /// 该场景对应 `x.NullableBoolField != null && confirmStatuses.Contains(x.NullableBoolField)`，
    /// 应翻译为 exists + terms(true/false)，而不是字符串 "True"/"False"。
    /// </summary>
    [Fact]
    public async Task Where_NullableBoolField_NotNull_AndIn_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument { Id = "20", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test20", NullableBoolField = true },
            new TestDocument { Id = "21", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test21", NullableBoolField = false },
            new TestDocument { Id = "22", EsDateTime = new DateTime(2024, 1, 15), TextField = "Test22", NullableBoolField = null },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";
        var confirmStatuses = new bool?[] { true };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableBoolField != null && confirmStatuses.Contains(x.NullableBoolField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.Single().Id.Should().Be("20");
        result.Documents.All(x => x.NullableBoolField == true).Should().BeTrue();
    }

    /// <summary>
    /// 测试 bool 字段的 In 查询
    /// </summary>
    [Fact]
    public async Task Where_BoolField_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var boolValues = new[] { true };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => boolValues.Contains(x.BoolField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.BoolField).Should().BeTrue();
    }
}


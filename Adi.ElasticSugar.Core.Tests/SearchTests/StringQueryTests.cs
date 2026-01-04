using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 字符串类型查询测试
/// 测试 text、keyword 类型字段的各种查询方式
/// </summary>
public class StringQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            new TestDocument { Id = 1, EsDateTime = new DateTime(2024, 1, 15), TextField = "Hello World", KeywordField = "STATUS-ACTIVE" },
            new TestDocument { Id = 2, EsDateTime = new DateTime(2024, 1, 15), TextField = "Hello Elasticsearch", KeywordField = "STATUS-INACTIVE" },
            new TestDocument { Id = 3, EsDateTime = new DateTime(2024, 1, 15), TextField = "World Peace", KeywordField = "STATUS-ACTIVE" },
            new TestDocument { Id = 4, EsDateTime = new DateTime(2024, 1, 15), TextField = "Elasticsearch Test", KeywordField = "STATUS-PENDING" },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试 text 字段的精确匹配（使用 .keyword 子字段）
    /// </summary>
    [Fact]
    public async Task Where_TextField_Equals_ShouldReturnExactMatch()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Hello World")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().TextField.Should().Be("Hello World");
    }

    /// <summary>
    /// 测试 keyword 字段的精确匹配
    /// </summary>
    [Fact]
    public async Task Where_KeywordField_Equals_ShouldReturnExactMatch()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.KeywordField == "STATUS-ACTIVE")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.KeywordField == "STATUS-ACTIVE").Should().BeTrue();
    }

    /// <summary>
    /// 测试 text 字段的 Contains 查询（模糊匹配）
    /// </summary>
    [Fact]
    public async Task Where_TextField_Contains_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.Contains("Hello"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.TextField.Contains("Hello")).Should().BeTrue();
    }

    /// <summary>
    /// 测试 text 字段的 StartsWith 查询
    /// </summary>
    [Fact]
    public async Task Where_TextField_StartsWith_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.StartsWith("Hello"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.TextField.StartsWith("Hello")).Should().BeTrue();
    }

    /// <summary>
    /// 测试 text 字段的 EndsWith 查询
    /// </summary>
    [Fact]
    public async Task Where_TextField_EndsWith_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.EndsWith("World"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().TextField.Should().Be("Hello World");
    }

    /// <summary>
    /// 测试 text 字段的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_TextField_NotEquals_ShouldReturnNonMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField != "Hello World")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => x.TextField != "Hello World").Should().BeTrue();
    }

    /// <summary>
    /// 测试 keyword 字段的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_KeywordField_NotEquals_ShouldReturnNonMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.KeywordField != "STATUS-ACTIVE")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.KeywordField != "STATUS-ACTIVE").Should().BeTrue();
    }

    /// <summary>
    /// 测试字符串字段的 In 查询（使用 Contains 方法）
    /// </summary>
    [Fact]
    public async Task Where_KeywordField_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var statuses = new[] { "STATUS-ACTIVE", "STATUS-PENDING" };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => statuses.Contains(x.KeywordField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => statuses.Contains(x.KeywordField)).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空字符串字段的查询
    /// </summary>
    [Fact]
    public async Task Where_NullableStringField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument { Id = 10, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", NullableStringField = "Value1" },
            new TestDocument { Id = 11, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", NullableStringField = null },
            new TestDocument { Id = 12, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", NullableStringField = "Value1" },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableStringField == "Value1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.NullableStringField == "Value1").Should().BeTrue();
    }
}


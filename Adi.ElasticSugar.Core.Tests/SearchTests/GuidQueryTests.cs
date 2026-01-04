using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// Guid 类型查询测试
/// 测试 Guid 类型字段的查询方式
/// </summary>
public class GuidQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();
    private readonly Guid _testGuid1 = Guid.NewGuid();
    private readonly Guid _testGuid2 = Guid.NewGuid();
    private readonly Guid _testGuid3 = Guid.NewGuid();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            new TestDocument { Id = 1, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", GuidField = _testGuid1 },
            new TestDocument { Id = 2, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", GuidField = _testGuid2 },
            new TestDocument { Id = 3, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", GuidField = _testGuid1 },
            new TestDocument { Id = 4, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test4", GuidField = _testGuid3 },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试 Guid 字段的等于查询
    /// </summary>
    [Fact]
    public async Task Where_GuidField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.GuidField == _testGuid1)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.GuidField == _testGuid1).Should().BeTrue();
    }

    /// <summary>
    /// 测试 Guid 字段的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_GuidField_NotEquals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.GuidField != _testGuid1)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.GuidField != _testGuid1).Should().BeTrue();
    }

    /// <summary>
    /// 测试 Guid 字段的 In 查询（使用 Contains 方法）
    /// </summary>
    [Fact]
    public async Task Where_GuidField_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var guids = new[] { _testGuid1, _testGuid2 };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => guids.Contains(x.GuidField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);
        result.Documents.All(x => guids.Contains(x.GuidField)).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空 Guid 字段的查询
    /// </summary>
    [Fact]
    public async Task Where_NullableGuidField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var testGuid = Guid.NewGuid();
        var documents = new[]
        {
            new TestDocument { Id = 10, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1", NullableGuidField = testGuid },
            new TestDocument { Id = 11, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2", NullableGuidField = null },
            new TestDocument { Id = 12, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test3", NullableGuidField = testGuid },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableGuidField == testGuid)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => x.NullableGuidField == testGuid).Should().BeTrue();
    }
}


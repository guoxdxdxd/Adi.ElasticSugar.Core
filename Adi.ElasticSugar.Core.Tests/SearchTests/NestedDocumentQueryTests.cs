using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 嵌套文档查询测试
/// 测试嵌套文档（nested）类型的查询功能
/// </summary>
public class NestedDocumentQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据（包含嵌套文档）
        _testDocuments.AddRange(new[]
        {
            new TestDocument
            {
                Id = 1,
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Order 1",
                Address = new NestedAddress
                {
                    Street = "Street 1",
                    City = "Beijing",
                    ZipCode = "100001",
                    Country = "China"
                },
                Items = new List<NestedItem>
                {
                    new() { ProductName = "Product A", Quantity = 10, Price = 100.5m, IsAvailable = true },
                    new() { ProductName = "Product B", Quantity = 5, Price = 200.5m, IsAvailable = false }
                }
            },
            new TestDocument
            {
                Id = 2,
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Order 2",
                Address = new NestedAddress
                {
                    Street = "Street 2",
                    City = "Shanghai",
                    ZipCode = "200001",
                    Country = "China"
                },
                Items = new List<NestedItem>
                {
                    new() { ProductName = "Product C", Quantity = 20, Price = 300.5m, IsAvailable = true },
                    new() { ProductName = "Product A", Quantity = 15, Price = 150.5m, IsAvailable = true }
                }
            },
            new TestDocument
            {
                Id = 3,
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Order 3",
                Address = new NestedAddress
                {
                    Street = "Street 3",
                    City = "Beijing",
                    ZipCode = "100002",
                    Country = "China"
                },
                Items = new List<NestedItem>
                {
                    new() { ProductName = "Product B", Quantity = 8, Price = 250.5m, IsAvailable = true }
                }
            },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试嵌套文档字段的查询
    /// 注意：嵌套文档的查询需要使用 Nested 查询，但当前实现可能不支持直接访问嵌套字段
    /// 这个测试主要用于验证嵌套文档的存储和检索
    /// </summary>
    [Fact]
    public async Task Where_NestedDocument_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询包含嵌套文档的文档
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Order 1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var document = result.Documents.First();
        document.Address.Should().NotBeNull();
        document.Address.City.Should().Be("Beijing");
        document.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试嵌套文档的完整检索
    /// </summary>
    [Fact]
    public async Task Search_WithNestedDocuments_ShouldReturnCompleteData()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.Contains("Order"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);

        // 验证每个文档的嵌套数据
        foreach (var doc in result.Documents)
        {
            doc.Address.Should().NotBeNull();
            doc.Address.City.Should().NotBeNullOrEmpty();
            doc.Items.Should().NotBeNull();
            doc.Items.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// 测试嵌套文档集合的查询
    /// </summary>
    [Fact]
    public async Task Search_WithNestedDocumentCollection_ShouldReturnCompleteData()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Order 2")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var document = result.Documents.First();
        document.Items.Should().HaveCount(2);
        document.Items[0].ProductName.Should().Be("Product C");
        document.Items[1].ProductName.Should().Be("Product A");
    }

    /// <summary>
    /// 测试包含嵌套文档的完整文档推送和检索
    /// </summary>
    [Fact]
    public async Task PushAndSearch_WithNestedDocuments_ShouldWorkCorrectly()
    {
        // Arrange
        var document = new TestDocument
        {
            Id = 100,
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = "Test Order",
            Address = new NestedAddress
            {
                Street = "Test Street",
                City = "Test City",
                ZipCode = "999999",
                Country = "Test Country"
            },
            Items = new List<NestedItem>
            {
                new() { ProductName = "Test Product", Quantity = 1, Price = 99.99m, IsAvailable = true }
            }
        };

        // Act - 推送文档
        var pushResponse = await Client.PushDocumentAsync(document);
        pushResponse.IsSuccess().Should().BeTrue();

        // 等待索引刷新
        var indexName = document.GetIndexNameFromAttribute();
        await RefreshIndexAsync(indexName);

        // Act - 查询文档
        var searchResponse = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Test Order")
            .ToListAsync();

        // Assert
        searchResponse.IsSuccess().Should().BeTrue();
        searchResponse.Documents.Should().HaveCount(1);
        var retrieved = searchResponse.Documents.First();
        retrieved.Address.Should().NotBeNull();
        retrieved.Address.City.Should().Be("Test City");
        retrieved.Items.Should().HaveCount(1);
        retrieved.Items[0].ProductName.Should().Be("Test Product");
    }

    /// <summary>
    /// 测试嵌套字段的等于查询
    /// 测试查询嵌套文档中的字段（如 x.Address.City == "Beijing"）
    /// </summary>
    [Fact]
    public async Task Where_NestedField_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" 的文档
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        if (!result.IsSuccess())
        {
            throw new Exception($"查询失败: {result.DebugInformation}");
        }
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3 都在 Beijing
        result.Documents.All(x => x.Address.City == "Beijing").Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的 Contains 查询
    /// </summary>
    [Fact]
    public async Task Where_NestedField_Contains_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.Street 包含 "Street" 的文档
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.Street.Contains("Street"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档的 Street 都包含 "Street"
        result.Documents.All(x => x.Address.Street.Contains("Street")).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的 StartsWith 查询
    /// </summary>
    [Fact]
    public async Task Where_NestedField_StartsWith_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.Country 以 "China" 开头的文档
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.Country.StartsWith("China"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档的 Country 都是 "China"
        result.Documents.All(x => x.Address.Country.StartsWith("China")).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的多条件组合查询
    /// </summary>
    [Fact]
    public async Task Where_NestedField_WithMultipleConditions_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode == "100001"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && x.Address.ZipCode == "100001")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1); // 只有 Order 1 满足条件
        result.Documents.First().TextField.Should().Be("Order 1");
        result.Documents.First().Address.City.Should().Be("Beijing");
        result.Documents.First().Address.ZipCode.Should().Be("100001");
    }

    /// <summary>
    /// 测试嵌套字段和普通字段的组合查询
    /// </summary>
    [Fact]
    public async Task Where_NestedField_AndRegularField_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 TextField == "Order 1" AND Address.City == "Beijing"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Order 1" && x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().TextField.Should().Be("Order 1");
        result.Documents.First().Address.City.Should().Be("Beijing");
    }
}


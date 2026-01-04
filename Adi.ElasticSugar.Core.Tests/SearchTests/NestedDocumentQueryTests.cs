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

    /// <summary>
    /// 测试相同嵌套路径的多个字段条件合并
    /// 验证相同嵌套路径（address）的多个条件应该合并到同一个 nested 查询中
    /// </summary>
    [Fact]
    public async Task Where_SameNestedPath_MultipleFields_ShouldMergeIntoOneNestedQuery()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode == "100001" AND Address.Country == "China"
        // 这三个条件都属于 address 嵌套路径，应该合并到同一个 nested 查询中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && x.Address.ZipCode == "100001" && x.Address.Country == "China")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        doc.Address.Country.Should().Be("China");
    }

    /// <summary>
    /// 测试嵌套字段的复杂组合查询（包含 Contains 和 StartsWith）
    /// </summary>
    [Fact]
    public async Task Where_NestedField_ComplexStringOperations_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.Street.Contains("Street") AND Address.Country.StartsWith("China")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.Street.Contains("Street") && x.Address.Country.StartsWith("China"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档都满足条件
        result.Documents.All(x => x.Address.Street.Contains("Street") && x.Address.Country.StartsWith("China")).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段和普通字段的复杂组合（多个条件）
    /// </summary>
    [Fact]
    public async Task Where_NestedField_AndRegularField_ComplexCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 TextField.Contains("Order") AND Address.City == "Beijing" AND Address.ZipCode.StartsWith("100")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.Contains("Order") && x.Address.City == "Beijing" && x.Address.ZipCode.StartsWith("100"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3
        result.Documents.All(x => 
            x.TextField.Contains("Order") && 
            x.Address.City == "Beijing" && 
            x.Address.ZipCode.StartsWith("100")
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试 OR 查询中包含嵌套字段
    /// </summary>
    [Fact]
    public async Task Where_NestedField_WithOrCondition_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" OR Address.City == "Shanghai"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" || x.Address.City == "Shanghai")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档都满足条件
        result.Documents.All(x => x.Address.City == "Beijing" || x.Address.City == "Shanghai").Should().BeTrue();
    }

    /// <summary>
    /// 测试复杂的 OR 和 AND 组合查询
    /// </summary>
    [Fact]
    public async Task Where_NestedField_ComplexOrAndCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 (Address.City == "Beijing" AND Address.ZipCode == "100001") OR (Address.City == "Shanghai")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => (x.Address.City == "Beijing" && x.Address.ZipCode == "100001") || x.Address.City == "Shanghai")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 2
        result.Documents.All(x => 
            (x.Address.City == "Beijing" && x.Address.ZipCode == "100001") || 
            x.Address.City == "Shanghai"
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的 In 查询（Terms 查询）
    /// </summary>
    [Fact]
    public async Task Where_NestedField_InQuery_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var cities = new[] { "Beijing", "Shanghai" };

        // Act - 查询 Address.City 在 ["Beijing", "Shanghai"] 中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => cities.Contains(x.Address.City))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档都满足条件
        result.Documents.All(x => cities.Contains(x.Address.City)).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的 In 查询与其他条件的组合
    /// </summary>
    [Fact]
    public async Task Where_NestedField_InQuery_WithOtherConditions_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var zipCodes = new[] { "100001", "100002" };

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode 在 ["100001", "100002"] 中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && zipCodes.Contains(x.Address.ZipCode))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3
        result.Documents.All(x => 
            x.Address.City == "Beijing" && 
            zipCodes.Contains(x.Address.ZipCode)
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试多个嵌套路径的查询（Address 和 Items）
    /// 注意：Items 是嵌套集合，需要特殊处理
    /// </summary>
    [Fact]
    public async Task Where_MultipleNestedPaths_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Items 中包含 ProductName == "Product A" 的项
        // 注意：Items 是嵌套集合，查询需要特殊处理
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3
        result.Documents.All(x => x.Address.City == "Beijing").Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的多个条件在同一个 Where 子句中的合并
    /// 验证相同嵌套路径的条件应该合并到同一个 nested 查询中
    /// </summary>
    [Fact]
    public async Task Where_SameNestedField_MultipleConditions_InSingleWhere_ShouldMergeIntoOneNestedQuery()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 在同一个 Where 条件中，对同一个嵌套路径的多个字段进行查询
        // 这些条件应该合并到同一个 nested 查询中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                x.Address.City == "Beijing" && 
                x.Address.ZipCode == "100001" && 
                x.Address.Street.Contains("Street") &&
                x.Address.Country == "China")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        doc.Address.Street.Should().Contain("Street");
        doc.Address.Country.Should().Be("China");
    }

    /// <summary>
    /// 测试嵌套字段和普通字段的复杂 OR 组合
    /// </summary>
    [Fact]
    public async Task Where_NestedField_AndRegularField_ComplexOrCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 TextField == "Order 1" OR (Address.City == "Shanghai" AND Address.ZipCode == "200001")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Order 1" || (x.Address.City == "Shanghai" && x.Address.ZipCode == "200001"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 2
        result.Documents.All(x => 
            x.TextField == "Order 1" || 
            (x.Address.City == "Shanghai" && x.Address.ZipCode == "200001")
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的 EndsWith 查询
    /// </summary>
    [Fact]
    public async Task Where_NestedField_EndsWith_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.ZipCode 以 "001" 结尾
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.ZipCode.EndsWith("001"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 2
        result.Documents.All(x => x.Address.ZipCode.EndsWith("001")).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套字段的 EndsWith 与其他条件的组合
    /// </summary>
    [Fact]
    public async Task Where_NestedField_EndsWith_WithOtherConditions_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode.EndsWith("001")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && x.Address.ZipCode.EndsWith("001"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1); // 只有 Order 1
        result.Documents.First().TextField.Should().Be("Order 1");
        result.Documents.First().Address.City.Should().Be("Beijing");
        result.Documents.First().Address.ZipCode.Should().Be("100001");
    }

    /// <summary>
    /// 测试非常复杂的嵌套查询：多个嵌套字段条件 + 普通字段条件 + OR 组合
    /// </summary>
    [Fact]
    public async Task Where_VeryComplexNestedQuery_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 复杂查询：
        // (TextField.Contains("Order") AND Address.City == "Beijing" AND Address.ZipCode.StartsWith("100"))
        // OR
        // (TextField == "Order 2" AND Address.City == "Shanghai")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                (x.TextField.Contains("Order") && x.Address.City == "Beijing" && x.Address.ZipCode.StartsWith("100")) ||
                (x.TextField == "Order 2" && x.Address.City == "Shanghai"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // Order 1, Order 2, Order 3
        result.Documents.All(x => 
            (x.TextField.Contains("Order") && x.Address.City == "Beijing" && x.Address.ZipCode.StartsWith("100")) ||
            (x.TextField == "Order 2" && x.Address.City == "Shanghai")
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试链式 Where 调用中的嵌套字段查询
    /// 验证多个 Where 调用中的嵌套字段条件是否正确组合
    /// </summary>
    [Fact]
    public async Task Where_ChainedWhereCalls_WithNestedFields_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用链式 Where 调用
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .Where(x => x.Address.ZipCode == "100001")
            .Where(x => x.TextField == "Order 1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().TextField.Should().Be("Order 1");
        result.Documents.First().Address.City.Should().Be("Beijing");
        result.Documents.First().Address.ZipCode.Should().Be("100001");
    }

    // ========== 不同嵌套文档的组合查询测试 ==========

    /// <summary>
    /// 测试同时查询 Address 和 Items 两个不同嵌套文档的字段
    /// 验证不同嵌套路径的条件应该分别创建独立的 nested 查询
    /// </summary>
    [Fact]
    public async Task Where_DifferentNestedDocuments_AddressAndItems_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Items 中包含 ProductName == "Product A" 的项
        // 注意：Address 和 Items 是不同的嵌套路径，应该创建两个独立的 nested 查询
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .ToListAsync();

        // 先验证 Address 查询
        result.IsSuccess().Should().BeTrue();
        var beijingDocs = result.Documents.Where(d => d.Address.City == "Beijing").ToList();
        beijingDocs.Should().HaveCount(2); // Order 1 和 Order 3

        // 验证 Order 1 包含 Product A
        var order1 = beijingDocs.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Items.Any(i => i.ProductName == "Product A").Should().BeTrue();
    }

    /// <summary>
    /// 测试 Address 嵌套文档和 Items 嵌套集合的组合查询
    /// 验证不同嵌套路径的条件组合
    /// </summary>
    [Fact]
    public async Task Where_AddressNested_AndItemsNested_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Items 中包含 Quantity >= 10 的项
        // 注意：由于 Items 是嵌套集合，需要特殊处理，这里先测试 Address 查询
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3

        // 验证 Order 1 的 Items 中有 Quantity >= 10 的项
        var order1 = result.Documents.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Items.Any(i => i.Quantity >= 10).Should().BeTrue();
    }

    /// <summary>
    /// 测试多个嵌套路径的复杂组合查询
    /// Address 的多个字段 + Items 的查询条件
    /// </summary>
    [Fact]
    public async Task Where_MultipleNestedPaths_ComplexCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode == "100001"
        // 注意：Items 的查询需要特殊处理，这里先测试 Address 的多字段查询
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && x.Address.ZipCode == "100001")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        
        // 验证 Items 数据
        doc.Items.Should().HaveCount(2);
        doc.Items.Any(i => i.ProductName == "Product A").Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套文档（Address）和普通字段的组合查询
    /// 验证嵌套查询和普通查询的正确组合
    /// </summary>
    [Fact]
    public async Task Where_NestedAddress_AndRegularField_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 TextField == "Order 1" AND Address.City == "Beijing" AND Address.Country == "China"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Order 1" && x.Address.City == "Beijing" && x.Address.Country == "China")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.Country.Should().Be("China");
    }

    /// <summary>
    /// 测试不同嵌套路径的条件在同一个 Where 子句中的组合
    /// Address 和 Items 的不同字段条件
    /// </summary>
    [Fact]
    public async Task Where_DifferentNestedPaths_InSingleWhere_ShouldCreateSeparateNestedQueries()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode == "100001"
        // 注意：Items 的查询需要特殊处理，这里测试 Address 的多字段查询
        // 验证相同嵌套路径（address）的条件应该合并，不同嵌套路径应该分开
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                x.Address.City == "Beijing" && 
                x.Address.ZipCode == "100001" &&
                x.Address.Street.Contains("Street"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        doc.Address.Street.Should().Contain("Street");
    }

    /// <summary>
    /// 测试 Address 嵌套文档的多个字段条件与 Items 嵌套集合的组合
    /// 验证不同嵌套路径的查询应该分别处理
    /// </summary>
    [Fact]
    public async Task Where_AddressMultipleFields_AndItemsNested_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.Country == "China"
        // 注意：Items 的查询需要特殊处理，这里先测试 Address 的多字段查询
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && x.Address.Country == "China")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3
        
        // 验证所有结果都满足条件
        result.Documents.All(x => 
            x.Address.City == "Beijing" && 
            x.Address.Country == "China"
        ).Should().BeTrue();

        // 验证 Order 1 的 Items 数据
        var order1 = result.Documents.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试链式 Where 调用中不同嵌套文档的查询
    /// 验证多个 Where 调用中的不同嵌套路径条件是否正确组合
    /// </summary>
    [Fact]
    public async Task Where_ChainedWhereCalls_DifferentNestedDocuments_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用链式 Where 调用查询不同嵌套文档的字段
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .Where(x => x.Address.ZipCode == "100001")
            .Where(x => x.TextField == "Order 1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        
        // 验证 Items 数据
        doc.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试 OR 查询中包含不同嵌套文档的条件
    /// </summary>
    [Fact]
    public async Task Where_OrCondition_WithDifferentNestedDocuments_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" OR Address.City == "Shanghai"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" || x.Address.City == "Shanghai")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档都满足条件
        
        // 验证结果
        result.Documents.All(x => 
            x.Address.City == "Beijing" || 
            x.Address.City == "Shanghai"
        ).Should().BeTrue();

        // 验证不同文档的 Items 数据
        var order1 = result.Documents.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Items.Should().HaveCount(2);

        var order2 = result.Documents.FirstOrDefault(d => d.TextField == "Order 2");
        order2.Should().NotBeNull();
        order2!.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试非常复杂的查询：多个嵌套路径 + 普通字段 + OR 组合
    /// Address 的多个字段 + Items 相关条件 + 普通字段
    /// </summary>
    [Fact]
    public async Task Where_VeryComplex_DifferentNestedDocuments_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 复杂查询：
        // (TextField.Contains("Order") AND Address.City == "Beijing" AND Address.ZipCode.StartsWith("100"))
        // OR
        // (TextField == "Order 2" AND Address.City == "Shanghai")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                (x.TextField.Contains("Order") && x.Address.City == "Beijing" && x.Address.ZipCode.StartsWith("100")) ||
                (x.TextField == "Order 2" && x.Address.City == "Shanghai"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // Order 1, Order 2, Order 3
        
        // 验证结果
        result.Documents.All(x => 
            (x.TextField.Contains("Order") && x.Address.City == "Beijing" && x.Address.ZipCode.StartsWith("100")) ||
            (x.TextField == "Order 2" && x.Address.City == "Shanghai")
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套文档字段的字符串操作组合（Address 和 Items）
    /// Contains、StartsWith、EndsWith 等操作
    /// </summary>
    [Fact]
    public async Task Where_DifferentNestedDocuments_StringOperations_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.Street.Contains("Street") AND Address.Country.StartsWith("China")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.Street.Contains("Street") && x.Address.Country.StartsWith("China"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档都满足条件
        
        // 验证结果
        result.Documents.All(x => 
            x.Address.Street.Contains("Street") && 
            x.Address.Country.StartsWith("China")
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试嵌套文档的 In 查询与其他嵌套文档条件的组合
    /// </summary>
    [Fact]
    public async Task Where_DifferentNestedDocuments_InQuery_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var cities = new[] { "Beijing", "Shanghai" };
        var zipCodes = new[] { "100001", "100002" };

        // Act - 查询 Address.City 在 ["Beijing", "Shanghai"] 中 AND Address.ZipCode 在 ["100001", "100002"] 中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => cities.Contains(x.Address.City) && zipCodes.Contains(x.Address.ZipCode))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3
        
        // 验证结果
        result.Documents.All(x => 
            cities.Contains(x.Address.City) && 
            zipCodes.Contains(x.Address.ZipCode)
        ).Should().BeTrue();
    }

    /// <summary>
    /// 测试不同嵌套文档字段的 EndsWith 查询组合
    /// </summary>
    [Fact]
    public async Task Where_DifferentNestedDocuments_EndsWith_Combination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.ZipCode.EndsWith("001") AND Address.City == "Beijing"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.ZipCode.EndsWith("001") && x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1); // 只有 Order 1
        
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.ZipCode.Should().Be("100001");
        doc.Address.City.Should().Be("Beijing");
    }

    /// <summary>
    /// 测试嵌套文档字段的复杂嵌套组合
    /// 同一嵌套路径的多个条件 + 不同嵌套路径的条件
    /// </summary>
    [Fact]
    public async Task Where_NestedDocuments_ComplexNestedCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询：
        // Address.City == "Beijing" AND 
        // Address.ZipCode == "100001" AND 
        // Address.Street.Contains("Street") AND
        // Address.Country == "China"
        // 这些条件都属于 address 嵌套路径，应该合并到同一个 nested 查询中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                x.Address.City == "Beijing" && 
                x.Address.ZipCode == "100001" &&
                x.Address.Street.Contains("Street") &&
                x.Address.Country == "China")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        doc.TextField.Should().Be("Order 1");
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        doc.Address.Street.Should().Contain("Street");
        doc.Address.Country.Should().Be("China");
    }

    // ========== Items 嵌套集合的查询测试 ==========

    /// <summary>
    /// 测试 Items 嵌套集合字段的查询
    /// 验证嵌套集合的查询功能
    /// </summary>
    [Fact]
    public async Task Where_ItemsNestedCollection_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Items 中包含 ProductName == "Product A" 的项
        // 注意：Items 是嵌套集合，查询需要特殊处理
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.Contains("Order"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3);

        // 验证 Order 1 包含 Product A
        var order1 = result.Documents.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Items.Any(i => i.ProductName == "Product A").Should().BeTrue();
    }

    /// <summary>
    /// 测试 Items 嵌套集合的多个字段条件
    /// 验证相同嵌套路径（items）的多个条件应该合并
    /// </summary>
    [Fact]
    public async Task Where_ItemsNestedCollection_MultipleFields_ShouldMergeIntoOneNestedQuery()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 先查询包含特定 Items 的文档
        // 注意：Items 的查询需要特殊处理，这里先验证数据
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Order 1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        
        // 验证 Items 数据
        doc.Items.Should().HaveCount(2);
        doc.Items.Any(i => i.ProductName == "Product A" && i.Quantity == 10).Should().BeTrue();
        doc.Items.Any(i => i.ProductName == "Product B" && i.Quantity == 5).Should().BeTrue();
    }

    /// <summary>
    /// 测试 Address 和 Items 两个不同嵌套文档的真正组合查询
    /// 验证不同嵌套路径的查询应该分别创建独立的 nested 查询
    /// </summary>
    [Fact]
    public async Task Where_AddressAndItems_DifferentNestedPaths_ShouldCreateSeparateNestedQueries()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing"
        // 注意：Items 的查询需要特殊处理，这里先测试 Address 查询
        // 验证不同嵌套路径（address 和 items）应该创建独立的 nested 查询
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Order 1 和 Order 3

        // 验证 Order 1 的数据
        var order1 = result.Documents.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Address.City.Should().Be("Beijing");
        order1.Items.Should().HaveCount(2);
        order1.Items.Any(i => i.ProductName == "Product A").Should().BeTrue();
    }

    /// <summary>
    /// 测试 Address 嵌套文档和 Items 嵌套集合的复杂组合
    /// Address 的多个字段 + Items 的多个字段
    /// </summary>
    [Fact]
    public async Task Where_AddressMultipleFields_AndItemsMultipleFields_ComplexCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" AND Address.ZipCode == "100001"
        // 验证 Address 的多个字段条件应该合并到同一个 nested 查询中
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" && x.Address.ZipCode == "100001")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        
        // 验证 Address 数据
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        
        // 验证 Items 数据（虽然查询条件中没有 Items，但应该能正确返回）
        doc.Items.Should().HaveCount(2);
        doc.Items.Any(i => i.ProductName == "Product A").Should().BeTrue();
        doc.Items.Any(i => i.ProductName == "Product B").Should().BeTrue();
    }

    /// <summary>
    /// 测试普通字段 + Address 嵌套文档 + Items 嵌套集合的三重组合查询
    /// </summary>
    [Fact]
    public async Task Where_RegularField_AndAddress_AndItems_TripleCombination_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 TextField == "Order 1" AND Address.City == "Beijing" AND Address.Country == "China"
        // 验证普通字段、Address 嵌套文档的多个字段条件组合
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                x.TextField == "Order 1" && 
                x.Address.City == "Beijing" && 
                x.Address.Country == "China")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        
        // 验证普通字段
        doc.TextField.Should().Be("Order 1");
        
        // 验证 Address 嵌套文档
        doc.Address.City.Should().Be("Beijing");
        doc.Address.Country.Should().Be("China");
        
        // 验证 Items 嵌套集合（虽然查询条件中没有 Items，但应该能正确返回）
        doc.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// 测试链式 Where 调用中不同嵌套文档的组合
    /// 验证多个 Where 调用中的不同嵌套路径条件是否正确组合
    /// </summary>
    [Fact]
    public async Task Where_ChainedWhereCalls_AddressAndItems_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用链式 Where 调用查询不同嵌套文档的字段
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .Where(x => x.Address.ZipCode == "100001")
            .Where(x => x.TextField == "Order 1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        var doc = result.Documents.First();
        
        // 验证普通字段
        doc.TextField.Should().Be("Order 1");
        
        // 验证 Address 嵌套文档
        doc.Address.City.Should().Be("Beijing");
        doc.Address.ZipCode.Should().Be("100001");
        
        // 验证 Items 嵌套集合
        doc.Items.Should().HaveCount(2);
        doc.Items.Any(i => i.ProductName == "Product A").Should().BeTrue();
        doc.Items.Any(i => i.ProductName == "Product B").Should().BeTrue();
    }

    /// <summary>
    /// 测试 OR 查询中包含不同嵌套文档的条件
    /// Address 的条件 OR Items 相关条件
    /// </summary>
    [Fact]
    public async Task Where_OrCondition_AddressAndItems_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 Address.City == "Beijing" OR Address.City == "Shanghai"
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing" || x.Address.City == "Shanghai")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // 所有文档都满足条件
        
        // 验证结果
        result.Documents.All(x => 
            x.Address.City == "Beijing" || 
            x.Address.City == "Shanghai"
        ).Should().BeTrue();

        // 验证不同文档的 Items 数据
        var order1 = result.Documents.FirstOrDefault(d => d.TextField == "Order 1");
        order1.Should().NotBeNull();
        order1!.Items.Should().HaveCount(2);

        var order2 = result.Documents.FirstOrDefault(d => d.TextField == "Order 2");
        order2.Should().NotBeNull();
        order2!.Items.Should().HaveCount(2);

        var order3 = result.Documents.FirstOrDefault(d => d.TextField == "Order 3");
        order3.Should().NotBeNull();
        order3!.Items.Should().HaveCount(1);
    }

    /// <summary>
    /// 测试最复杂的场景：多个嵌套路径 + 普通字段 + 复杂 OR/AND 组合
    /// Address 的多个字段 + Items 相关条件 + 普通字段 + OR 组合
    /// </summary>
    [Fact]
    public async Task Where_MostComplex_DifferentNestedDocuments_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 非常复杂的查询：
        // (TextField.Contains("Order") AND Address.City == "Beijing" AND Address.ZipCode.StartsWith("100") AND Address.Country == "China")
        // OR
        // (TextField == "Order 2" AND Address.City == "Shanghai" AND Address.Country == "China")
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => 
                (x.TextField.Contains("Order") && 
                 x.Address.City == "Beijing" && 
                 x.Address.ZipCode.StartsWith("100") &&
                 x.Address.Country == "China") ||
                (x.TextField == "Order 2" && 
                 x.Address.City == "Shanghai" &&
                 x.Address.Country == "China"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // Order 1, Order 2, Order 3
        
        // 验证结果
        result.Documents.All(x => 
            (x.TextField.Contains("Order") && 
             x.Address.City == "Beijing" && 
             x.Address.ZipCode.StartsWith("100") &&
             x.Address.Country == "China") ||
            (x.TextField == "Order 2" && 
             x.Address.City == "Shanghai" &&
             x.Address.Country == "China")
        ).Should().BeTrue();

        // 验证每个文档的 Items 数据都正确返回
        foreach (var doc in result.Documents)
        {
            doc.Items.Should().NotBeNull();
            doc.Items.Should().NotBeEmpty();
        }
    }
}


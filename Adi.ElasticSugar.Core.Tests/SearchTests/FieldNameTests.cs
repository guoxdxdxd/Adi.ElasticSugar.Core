using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 字段名处理测试
/// 测试自定义字段名（FieldName）和默认字段名转换功能
/// </summary>
public class FieldNameTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据
        _testDocuments.AddRange(new[]
        {
            // TextField 配置了 FieldName = "textField"
            new TestDocument { Id = 1, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test Value 1", KeywordField = "KEYWORD-1" },
            // KeywordField 配置了 FieldName = "keywordField"
            new TestDocument { Id = 2, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test Value 2", KeywordField = "KEYWORD-2" },
            // NullableStringField 没有配置 FieldName，应该使用 camelCase: "nullableStringField"
            new TestDocument { Id = 3, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test Value 3", NullableStringField = "Nullable Value" },
            // TextOnlyField 配置了 NeedKeyword = false
            new TestDocument { Id = 4, EsDateTime = new DateTime(2024, 1, 15), TextField = "Test Value 4", TextOnlyField = "Text Only Value" },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试自定义字段名（FieldName）在查询中是否正确使用
    /// TextField 配置了 FieldName = "textField"，查询时应该使用 "textField.keyword"
    /// </summary>
    [Fact]
    public async Task Where_CustomFieldName_TextField_ShouldUseCustomName()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用自定义字段名进行查询
        // TextField 属性配置了 FieldName = "textField"
        // 查询时应该使用 "textField.keyword"（因为 TextField 是 text 类型，需要 .keyword 子字段）
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField == "Test Value 1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().TextField.Should().Be("Test Value 1");
    }

    /// <summary>
    /// 测试自定义字段名（FieldName）在 keyword 字段查询中是否正确使用
    /// KeywordField 配置了 FieldName = "keywordField"，查询时应该直接使用 "keywordField"
    /// </summary>
    [Fact]
    public async Task Where_CustomFieldName_KeywordField_ShouldUseCustomName()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用自定义字段名进行查询
        // KeywordField 属性配置了 FieldName = "keywordField"，且 FieldType = "keyword"
        // 查询时应该直接使用 "keywordField"（不需要 .keyword 后缀）
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.KeywordField == "KEYWORD-1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().KeywordField.Should().Be("KEYWORD-1");
    }

    /// <summary>
    /// 测试没有配置 FieldName 的字段是否使用了 camelCase
    /// NullableStringField 没有配置 FieldName，应该使用 "nullableStringField"
    /// </summary>
    [Fact]
    public async Task Where_DefaultFieldName_ShouldUseCamelCase()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用没有配置 FieldName 的字段进行查询
        // NullableStringField 没有配置 FieldName，应该自动转换为 camelCase: "nullableStringField"
        // 查询时应该使用 "nullableStringField.keyword"（因为 string 类型默认是 text，需要 .keyword 子字段）
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableStringField == "Nullable Value")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().NullableStringField.Should().Be("Nullable Value");
    }

    /// <summary>
    /// 测试 NeedKeyword = false 的 text 字段查询
    /// TextOnlyField 配置了 NeedKeyword = false，精确匹配时不应该使用 .keyword 子字段
    /// 注意：由于 NeedKeyword = false，该字段没有 .keyword 子字段，精确匹配可能无法正常工作
    /// 这个测试主要用于验证字段名转换是否正确
    /// </summary>
    [Fact]
    public async Task Where_TextOnlyField_WithoutKeyword_ShouldUseCorrectFieldName()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用 NeedKeyword = false 的字段进行查询
        // TextOnlyField 配置了 NeedKeyword = false，没有 .keyword 子字段
        // 查询时应该使用 "textOnlyField"（不使用 .keyword 后缀）
        // 注意：由于是 text 类型且没有 keyword 子字段，精确匹配可能无法正常工作
        // 这里主要测试字段名是否正确
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextOnlyField.Contains("Text Only"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        // Contains 查询使用 match 查询，应该能正常工作
        result.Documents.Should().HaveCount(1);
        result.Documents.First().TextOnlyField.Should().Contain("Text Only");
    }

    /// <summary>
    /// 测试排序时 text 字段是否正确使用 .keyword 子字段
    /// TextField 是 text 类型，排序时应该使用 "textField.keyword"
    /// </summary>
    [Fact]
    public async Task OrderBy_TextField_ShouldUseKeyword()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 对 text 字段进行排序
        // TextField 是 text 类型，排序时应该使用 "textField.keyword"
        var result = await Client.Search<TestDocument>(indexName)
            .OrderBy(x => x.TextField)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(4);
        // 验证排序是否正确（按字母顺序）
        var documents = result.Documents.ToList();
        documents[0].TextField.Should().Be("Test Value 1");
        documents[1].TextField.Should().Be("Test Value 2");
        documents[2].TextField.Should().Be("Test Value 3");
        documents[3].TextField.Should().Be("Test Value 4");
    }

    /// <summary>
    /// 测试排序时 keyword 字段是否正确直接使用字段名
    /// KeywordField 是 keyword 类型，排序时应该直接使用 "keywordField"（不需要 .keyword 后缀）
    /// </summary>
    [Fact]
    public async Task OrderBy_KeywordField_ShouldNotUseKeyword()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 对 keyword 字段进行排序
        // KeywordField 是 keyword 类型，排序时应该直接使用 "keywordField"（不需要 .keyword 后缀）
        var result = await Client.Search<TestDocument>(indexName)
            .OrderBy(x => x.KeywordField)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        // 验证排序是否正确
        var documents = result.Documents.ToList();
        // 应该能正常排序（keyword 类型字段支持排序）
        documents.Should().HaveCount(4);
    }

    /// <summary>
    /// 测试嵌套字段的自定义字段名
    /// Address 配置了 FieldName = "address"，查询嵌套字段时应该使用 "address.city" 等路径
    /// </summary>
    [Fact]
    public async Task Where_NestedField_WithCustomFieldName_ShouldUseCustomName()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument
            {
                Id = 10,
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Nested Test 1",
                Address = new NestedAddress
                {
                    Street = "Street 1",
                    City = "Beijing",
                    ZipCode = "100001",
                    Country = "China"
                }
            },
            new TestDocument
            {
                Id = 11,
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Nested Test 2",
                Address = new NestedAddress
                {
                    Street = "Street 2",
                    City = "Shanghai",
                    ZipCode = "200001",
                    Country = "China"
                }
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act - 查询嵌套字段
        // Address 配置了 FieldName = "address"
        // 查询时应该使用 "address.city" 路径（嵌套路径使用自定义字段名）
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.Address.City == "Beijing")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().Address.City.Should().Be("Beijing");
    }

    /// <summary>
    /// 测试自定义字段名在 Contains 查询中的使用
    /// </summary>
    [Fact]
    public async Task Where_CustomFieldName_Contains_ShouldUseCustomName()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 使用自定义字段名进行 Contains 查询
        // TextField 配置了 FieldName = "textField"
        // Contains 查询应该使用 "textField"（text 类型字段使用 match 查询）
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.TextField.Contains("Test"))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(4);
        result.Documents.All(x => x.TextField.Contains("Test")).Should().BeTrue();
    }

    /// <summary>
    /// 测试自定义字段名在 In 查询中的使用
    /// </summary>
    [Fact]
    public async Task Where_CustomFieldName_In_ShouldUseCustomName()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var values = new[] { "Test Value 1", "Test Value 2" };

        // Act - 使用自定义字段名进行 In 查询
        // TextField 配置了 FieldName = "textField"
        // In 查询应该使用 "textField.keyword"（精确匹配需要 .keyword 子字段）
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => values.Contains(x.TextField))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => values.Contains(x.TextField)).Should().BeTrue();
    }
}


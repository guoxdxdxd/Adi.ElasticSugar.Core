using Adi.ElasticSugar.Core.Index;
using Adi.ElasticSugar.Core.Models;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.IndexTests;

/// <summary>
/// 索引创建测试
/// 测试索引的创建、存在性检查等功能
/// </summary>
public class IndexCreationTests : TestBase
{
    /// <summary>
    /// 测试根据文档创建索引
    /// </summary>
    [Fact]
    public async Task CreateIndexForDocumentAsync_ShouldCreateIndex()
    {
        // Arrange
        var document = new TestDocument
        {
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = "Test"
        };

        // Act
        var indexName = await Client.CreateIndexForDocumentAsync(document);

        // Assert
        indexName.Should().NotBeNullOrEmpty();
        indexName.Should().Be("test-documents-2024-01");

        // 验证索引是否存在
        var exists = await Client.IndexManager().IndexExistsAsync(indexName);
        exists.Should().BeTrue();
    }

    /// <summary>
    /// 测试重复创建索引（应该不会报错）
    /// </summary>
    [Fact]
    public async Task CreateIndexForDocumentAsync_WhenIndexExists_ShouldNotThrow()
    {
        // Arrange
        var document = new TestDocument
        {
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = "Test"
        };

        // Act
        var indexName1 = await Client.CreateIndexForDocumentAsync(document);
        var indexName2 = await Client.CreateIndexForDocumentAsync(document);

        // Assert
        indexName1.Should().Be(indexName2);
        
        var exists = await Client.IndexManager().IndexExistsAsync(indexName1);
        exists.Should().BeTrue();
    }

    /// <summary>
    /// 测试批量创建索引
    /// </summary>
    [Fact]
    public async Task CreateIndexesForDocumentsAsync_ShouldCreateMultipleIndexes()
    {
        // Arrange
        var documents = new List<TestDocument>
        {
            new() { EsDateTime = new DateTime(2024, 1, 15), TextField = "Test1" },
            new() { EsDateTime = new DateTime(2024, 1, 15), TextField = "Test2" }, // 同一个月
            new() { EsDateTime = new DateTime(2024, 2, 15), TextField = "Test3" }, // 不同月
        };

        // Act
        var documentsByIndex = await Client.CreateIndexesForDocumentsAsync(documents);

        // Assert
        documentsByIndex.Should().NotBeNull();
        documentsByIndex.Count.Should().Be(2); // 应该创建两个索引（1月和2月）

        // 验证索引名称
        documentsByIndex.Keys.Should().Contain("test-documents-2024-01");
        documentsByIndex.Keys.Should().Contain("test-documents-2024-02");

        // 验证文档分组
        documentsByIndex["test-documents-2024-01"].Count.Should().Be(2);
        documentsByIndex["test-documents-2024-02"].Count.Should().Be(1);

        // 验证索引是否存在
        foreach (var indexName in documentsByIndex.Keys)
        {
            var exists = await Client.IndexManager().IndexExistsAsync(indexName);
            exists.Should().BeTrue();
        }
    }

    /// <summary>
    /// 测试索引存在性检查
    /// </summary>
    [Fact]
    public async Task IndexExistsAsync_WhenIndexExists_ShouldReturnTrue()
    {
        // Arrange
        var document = new TestDocument
        {
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = "Test"
        };
        var indexName = await Client.CreateIndexForDocumentAsync(document);

        // Act
        var exists = await Client.IndexManager().IndexExistsAsync(indexName);

        // Assert
        exists.Should().BeTrue();
    }

    /// <summary>
    /// 测试索引存在性检查（索引不存在）
    /// </summary>
    [Fact]
    public async Task IndexExistsAsync_WhenIndexNotExists_ShouldReturnFalse()
    {
        // Arrange
        var nonExistentIndex = "non-existent-index-2024-01";

        // Act
        var exists = await Client.IndexManager().IndexExistsAsync(nonExistentIndex);

        // Assert
        exists.Should().BeFalse();
    }

    /// <summary>
    /// 测试索引删除
    /// </summary>
    [Fact]
    public async Task DeleteIndexAsync_ShouldDeleteIndex()
    {
        // Arrange
        var document = new TestDocument
        {
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = "Test"
        };
        var indexName = await Client.CreateIndexForDocumentAsync(document);

        // 验证索引存在
        var existsBefore = await Client.IndexManager().IndexExistsAsync(indexName);
        existsBefore.Should().BeTrue();

        // Act
        var deleted = await Client.IndexManager().DeleteIndexAsync(indexName);

        // Assert
        deleted.Should().BeTrue();

        // 验证索引已删除
        var existsAfter = await Client.IndexManager().IndexExistsAsync(indexName);
        existsAfter.Should().BeFalse();
    }

    /// <summary>
    /// 测试索引映射是否正确创建（包含各种数据类型）
    /// </summary>
    [Fact]
    public async Task CreateIndex_ShouldCreateCorrectMapping()
    {
        // Arrange
        var document = new TestDocument
        {
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = "Test"
        };

        // Act
        var indexName = await Client.CreateIndexForDocumentAsync(document);

        // Assert - 获取索引映射并验证
        var mappingResponse = await Client.Indices.GetMappingAsync(idx => idx.Indices(indexName));
        // GetMappingResponse 没有 IsSuccess 方法，直接检查 Indices 是否包含索引
        mappingResponse.Indices.Should().NotBeNull();
        mappingResponse.Indices!.Should().ContainKey(indexName);

        var mapping = mappingResponse.Indices[indexName];
        mapping.Mappings.Should().NotBeNull();
        mapping.Mappings!.Properties.Should().NotBeNull();

        // 验证字符串字段映射
        mapping.Mappings.Properties.Should().ContainKey("textField");
        mapping.Mappings.Properties.Should().ContainKey("keywordField");

        // 验证数值字段映射
        mapping.Mappings.Properties.Should().ContainKey("intField");
        mapping.Mappings.Properties.Should().ContainKey("longField");
        mapping.Mappings.Properties.Should().ContainKey("doubleField");

        // 验证日期字段映射
        mapping.Mappings.Properties.Should().ContainKey("dateTimeField");

        // 验证布尔字段映射
        mapping.Mappings.Properties.Should().ContainKey("boolField");

        // 验证嵌套文档映射
        mapping.Mappings.Properties.Should().ContainKey("address");
    }
}


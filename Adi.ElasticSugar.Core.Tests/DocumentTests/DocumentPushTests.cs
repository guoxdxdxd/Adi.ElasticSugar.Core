using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Tests.Models;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.DocumentTests;

/// <summary>
/// 文档推送测试
/// 测试单个文档和批量文档的推送功能
/// </summary>
public class DocumentPushTests : TestBase
{
    /// <summary>
    /// 测试推送单个文档
    /// </summary>
    [Fact]
    public async Task PushDocumentAsync_ShouldPushDocument()
    {
        // Arrange
        var document = CreateTestDocument(1);

        // Act
        var response = await Client.PushDocumentAsync(document);

        // Assert
        response.IsSuccess().Should().BeTrue();
        response.Id.Should().NotBeNullOrEmpty();

        // 等待索引刷新
        var indexName = document.GetIndexNameFromAttribute();
        await RefreshIndexAsync(indexName);

        // 验证文档是否存在
        var getResponse = await Client.GetAsync<TestDocument>(response.Id, idx => idx.Index(indexName));
        getResponse.IsSuccess().Should().BeTrue();
        getResponse.Source.Should().NotBeNull();
        getResponse.Source!.TextField.Should().Be(document.TextField);
    }

    /// <summary>
    /// 测试推送包含所有数据类型的文档
    /// </summary>
    [Fact]
    public async Task PushDocumentAsync_WithAllDataTypes_ShouldPushSuccessfully()
    {
        // Arrange
        var document = CreateCompleteTestDocument(1);

        // Act
        var response = await Client.PushDocumentAsync(document);

        // Assert
        response.IsSuccess().Should().BeTrue();

        // 等待索引刷新
        var indexName = document.GetIndexNameFromAttribute();
        await RefreshIndexAsync(indexName);

        // 验证文档内容
        var getResponse = await Client.GetAsync<TestDocument>(response.Id, idx => idx.Index(indexName));
        getResponse.IsSuccess().Should().BeTrue();
        var retrieved = getResponse.Source!;

        retrieved.TextField.Should().Be(document.TextField);
        retrieved.KeywordField.Should().Be(document.KeywordField);
        retrieved.IntField.Should().Be(document.IntField);
        retrieved.LongField.Should().Be(document.LongField);
        retrieved.DoubleField.Should().Be(document.DoubleField);
        retrieved.DecimalField.Should().Be(document.DecimalField);
        retrieved.DateTimeField.Should().BeCloseTo(document.DateTimeField, TimeSpan.FromSeconds(1));
        retrieved.BoolField.Should().Be(document.BoolField);
        retrieved.GuidField.Should().Be(document.GuidField);
        retrieved.Address.Should().NotBeNull();
        retrieved.Address.City.Should().Be(document.Address.City);
        retrieved.Items.Should().HaveCount(document.Items.Count);
    }

    /// <summary>
    /// 测试批量推送文档
    /// </summary>
    [Fact]
    public async Task PushDocumentsAsync_ShouldPushMultipleDocuments()
    {
        // Arrange
        var documents = Enumerable.Range(1, 10)
            .Select(i => CreateTestDocument(i))
            .ToList();

        // Act
        var response = await Client.PushDocumentsAsync(documents);

        // Assert
        response.IsSuccess().Should().BeTrue();
        response.Errors.Should().BeFalse();

        // 等待索引刷新
        var indexName = documents[0].GetIndexNameFromAttribute();
        await RefreshIndexAsync(indexName);

        // 验证文档数量
        var searchResponse = await Client.SearchAsync<TestDocument>(s => s
            .Index(indexName)
            .Query(q => q.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery()))
        );

        searchResponse.IsSuccess().Should().BeTrue();
        searchResponse.Total.Should().BeGreaterOrEqualTo(documents.Count);
    }

    /// <summary>
    /// 测试批量推送大量文档（测试分批处理）
    /// </summary>
    [Fact]
    public async Task PushDocumentsAsync_WithLargeBatch_ShouldHandleBatching()
    {
        // Arrange
        var documents = Enumerable.Range(1, 1500) // 超过默认批次大小 1000
            .Select(i => CreateTestDocument(i))
            .ToList();

        // Act
        var response = await Client.PushDocumentsAsync(documents, batchSize: 500);

        // Assert
        response.IsSuccess().Should().BeTrue();
        response.Errors.Should().BeFalse();

        // 等待索引刷新
        var indexName = documents[0].GetIndexNameFromAttribute();
        await RefreshIndexAsync(indexName);

        // 验证文档数量
        var searchResponse = await Client.SearchAsync<TestDocument>(s => s
            .Index(indexName)
            .Query(q => q.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery()))
            .Size(0) // 只获取总数
        );

        searchResponse.IsSuccess().Should().BeTrue();
        searchResponse.Total.Should().BeGreaterOrEqualTo(documents.Count);
    }

    /// <summary>
    /// 测试批量推送跨多个索引的文档
    /// </summary>
    [Fact]
    public async Task PushDocumentsAsync_WithMultipleIndexes_ShouldPushToCorrectIndexes()
    {
        // Arrange
        var documents = new List<TestDocument>
        {
            CreateTestDocument(1, new DateTime(2024, 1, 15)),
            CreateTestDocument(2, new DateTime(2024, 1, 20)),
            CreateTestDocument(3, new DateTime(2024, 2, 15)),
            CreateTestDocument(4, new DateTime(2024, 2, 20)),
        };

        // Act
        var response = await Client.PushDocumentsAsync(documents);

        // Assert
        response.IsSuccess().Should().BeTrue();

        // 等待索引刷新
        await RefreshIndexAsync("test-documents-2024-01");
        await RefreshIndexAsync("test-documents-2024-02");

        // 验证每个索引的文档数量
        var janResponse = await Client.SearchAsync<TestDocument>(s => s
            .Index("test-documents-2024-01")
            .Query(q => q.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery()))
            .Size(0)
        );
        janResponse.Total.Should().Be(2);

        var febResponse = await Client.SearchAsync<TestDocument>(s => s
            .Index("test-documents-2024-02")
            .Query(q => q.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery()))
            .Size(0)
        );
        febResponse.Total.Should().Be(2);
    }

    /// <summary>
    /// 测试推送文档时自动创建索引
    /// </summary>
    [Fact]
    public async Task PushDocumentAsync_ShouldAutoCreateIndex()
    {
        // Arrange
        var document = CreateTestDocument(1);
        var indexName = document.GetIndexNameFromAttribute();

        // 确保索引不存在
        var manager = Client.IndexManager();
        if (await manager.IndexExistsAsync(indexName))
        {
            await manager.DeleteIndexAsync(indexName);
        }

        // Act
        var response = await Client.PushDocumentAsync(document);

        // Assert
        response.IsSuccess().Should().BeTrue();

        // 验证索引已创建
        var exists = await manager.IndexExistsAsync(indexName);
        exists.Should().BeTrue();
    }

    /// <summary>
    /// 创建测试文档（简化版）
    /// </summary>
    private TestDocument CreateTestDocument(int id, DateTime? esDateTime = null)
    {
        return new TestDocument
        {
            Id = id,
            EsDateTime = esDateTime ?? new DateTime(2024, 1, 15),
            TextField = $"Test Text {id}",
            KeywordField = $"KEYWORD-{id}",
            IntField = id * 10,
            LongField = id * 100L,
            DoubleField = id * 1.5,
            DecimalField = id * 2.5m,
            DateTimeField = DateTime.Now.AddDays(-id),
            BoolField = id % 2 == 0,
            GuidField = Guid.NewGuid(),
        };
    }

    /// <summary>
    /// 创建完整的测试文档（包含所有数据类型）
    /// </summary>
    private TestDocument CreateCompleteTestDocument(int id)
    {
        return new TestDocument
        {
            Id = id,
            EsDateTime = new DateTime(2024, 1, 15),
            TextField = $"Test Text {id}",
            KeywordField = $"KEYWORD-{id}",
            TextOnlyField = $"Text Only {id}",
            NullableStringField = id % 2 == 0 ? $"Nullable {id}" : null,
            IntField = id * 10,
            NullableIntField = id % 2 == 0 ? id * 20 : null,
            LongField = id * 100L,
            NullableLongField = id % 2 == 0 ? id * 200L : null,
            ShortField = (short)(id * 5),
            ByteField = (byte)(id % 256),
            DoubleField = id * 1.5,
            NullableDoubleField = id % 2 == 0 ? id * 2.5 : null,
            FloatField = id * 1.1f,
            DecimalField = id * 2.5m,
            DateTimeField = DateTime.Now.AddDays(-id),
            NullableDateTimeField = id % 2 == 0 ? DateTime.Now.AddDays(-id) : null,
            DateTimeOffsetField = DateTimeOffset.Now.AddDays(-id),
            BoolField = id % 2 == 0,
            NullableBoolField = id % 2 == 0 ? (bool?)(id % 4 == 0) : null,
            GuidField = Guid.NewGuid(),
            NullableGuidField = id % 2 == 0 ? Guid.NewGuid() : null,
            StringListField = new List<string> { $"Item1-{id}", $"Item2-{id}" },
            IntListField = new List<int> { id, id * 2, id * 3 },
            Address = new NestedAddress
            {
                Street = $"Street {id}",
                City = $"City {id}",
                ZipCode = $"ZIP{id:D5}",
                Country = "China"
            },
            Items = new List<NestedItem>
            {
                new() { ProductName = $"Product1-{id}", Quantity = id, Price = id * 10.5m, IsAvailable = true },
                new() { ProductName = $"Product2-{id}", Quantity = id * 2, Price = id * 20.5m, IsAvailable = false }
            },
            AnalyzedTextField = $"Analyzed Text {id}",
            StoredOnlyField = $"Stored Only {id}",
            IgnoredField = $"Ignored {id}" // 这个字段应该被忽略
        };
    }
}


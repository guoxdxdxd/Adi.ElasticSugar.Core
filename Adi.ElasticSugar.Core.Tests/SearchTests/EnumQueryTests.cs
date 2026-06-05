using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 枚举类型查询测试
/// 测试枚举类型字段的各种查询方式，包括等值查询、不等于查询、In 查询等
/// 验证枚举类型在 Elasticsearch 中存储为字符串（枚举名称）的处理逻辑
/// </summary>
public class EnumQueryTests : TestBase
{
    private readonly List<TestDocument> _testDocuments = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 准备测试数据 - 包含各种枚举值
        _testDocuments.AddRange(new[]
        {
            new TestDocument 
            { 
                Id = "1", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test1",
                OrderStatus = OrderStatus.Pending,
                UserRole = UserRole.Admin
            },
            new TestDocument 
            { 
                Id = "2", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test2",
                OrderStatus = OrderStatus.Processing,
                UserRole = UserRole.User
            },
            new TestDocument 
            { 
                Id = "3", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test3",
                OrderStatus = OrderStatus.Completed,
                UserRole = UserRole.User
            },
            new TestDocument 
            { 
                Id = "4", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test4",
                OrderStatus = OrderStatus.Cancelled,
                UserRole = UserRole.Guest
            },
            new TestDocument 
            { 
                Id = "5", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test5",
                OrderStatus = OrderStatus.Pending,
                UserRole = UserRole.Admin
            },
        });

        // 推送测试数据
        await Client.PushDocumentsAsync(_testDocuments);
        await RefreshIndexAsync("test-documents-2024-01");
    }

    /// <summary>
    /// 测试枚举类型的等值查询（使用枚举值）
    /// 验证枚举值会被转换为枚举名称（字符串）进行查询
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 OrderStatus == OrderStatus.Pending 的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.OrderStatus == OrderStatus.Pending)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Id = 1 和 Id = "5"
        result.Documents.All(x => x.OrderStatus == OrderStatus.Pending).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "5" });
    }

    /// <summary>
    /// 测试枚举类型的等值查询（使用整数值）
    /// 验证当查询值使用整数时，能正确转换为枚举名称进行查询
    /// 这是 ExpressionParser 中的特殊处理：如果值是整数类型，但字段是枚举类型，则需要将整数转换为枚举，然后使用枚举的名称
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_Equals_Integer_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        // OrderStatus.Pending = 0
        int pendingValue = 0;

        // Act - 查询 OrderStatus == 0（即 OrderStatus.Pending）的记录
        // 注意：这里使用整数 0 而不是枚举值，验证 ExpressionParser 能正确转换
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.OrderStatus == (OrderStatus)pendingValue)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Id = 1 和 Id = "5"
        result.Documents.All(x => x.OrderStatus == OrderStatus.Pending).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "5" });
    }

    /// <summary>
    /// 测试枚举类型的等值查询（使用不同的枚举值）
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_Equals_Processing_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.OrderStatus == OrderStatus.Processing)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().OrderStatus.Should().Be(OrderStatus.Processing);
        result.Documents.First().Id.Should().Be("2");
    }

    /// <summary>
    /// 测试枚举类型的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_NotEquals_ShouldReturnNonMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 OrderStatus != OrderStatus.Pending 的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.OrderStatus != OrderStatus.Pending)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // Id = "2", 3, 4
        result.Documents.All(x => x.OrderStatus != OrderStatus.Pending).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "2", "3", "4" });
    }

    /// <summary>
    /// 测试枚举类型的 In 查询（使用 Contains 方法）
    /// 验证多个枚举值可以正确转换为字符串数组进行 Terms 查询
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var statuses = new[] { OrderStatus.Pending, OrderStatus.Completed };

        // Act - 查询 OrderStatus 在指定枚举值列表中的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => statuses.Contains(x.OrderStatus))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // Id = "1", 3, 5 (Pending 和 Completed)
        result.Documents.All(x => statuses.Contains(x.OrderStatus)).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "3", "5" });
    }

    /// <summary>
    /// 测试枚举类型的 In 查询（多个枚举值）
    /// </summary>
    [Fact]
    public async Task Where_UserRole_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";
        var roles = new[] { UserRole.Admin, UserRole.User };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => roles.Contains(x.UserRole))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(4); // Id = "1", 2, 3, 5 (Admin 和 User)
        result.Documents.All(x => roles.Contains(x.UserRole)).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "2", "3", "5" });
    }

    /// <summary>
    /// 测试可空枚举类型的查询
    /// 验证可空枚举类型可以正常查询
    /// </summary>
    [Fact]
    public async Task Where_NullableOrderStatus_Equals_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument 
            { 
                Id = "10", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test10",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Processing
            },
            new TestDocument 
            { 
                Id = "11", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test11",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = null
            },
            new TestDocument 
            { 
                Id = "12", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test12",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Processing
            },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act - 查询 NullableOrderStatus == OrderStatus.Processing 的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableOrderStatus == OrderStatus.Processing)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2); // Id = 10 和 Id = "12"
        result.Documents.All(x => x.NullableOrderStatus == OrderStatus.Processing).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "10", "12" });
    }

    /// <summary>
    /// 测试可空枚举类型的不等于查询
    /// </summary>
    [Fact]
    public async Task Where_NullableOrderStatus_NotEquals_ShouldReturnNonMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument 
            { 
                Id = "20", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test20",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Processing
            },
            new TestDocument 
            { 
                Id = "21", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test21",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Completed
            },
            new TestDocument 
            { 
                Id = "22", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test22",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = null
            },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act - 查询 NullableOrderStatus != OrderStatus.Processing 的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableOrderStatus != OrderStatus.Processing)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        // 查询 NullableOrderStatus != OrderStatus.Processing 应该返回：
        // - InitializeAsync 中插入的 5 条文档（Id = "1", "2", "3", "4", "5"），它们的 NullableOrderStatus 都是 null
        // - 本测试中插入的 Id = "21"（NullableOrderStatus = Completed）
        // - 本测试中插入的 Id = "22"（NullableOrderStatus = null）
        // 总共 7 条记录（排除 Id = "20"，因为它的 NullableOrderStatus = Processing）
        // 注意：在 C# 中，null != OrderStatus.Processing 返回 true，所以 null 值应该被包含在结果中
        result.Documents.Should().HaveCount(7);
        result.Documents.All(x => x.NullableOrderStatus != OrderStatus.Processing).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "2", "3", "4", "5", "21", "22" });
    }

    /// <summary>
    /// 测试可空枚举类型的 In 查询
    /// </summary>
    [Fact]
    public async Task Where_NullableOrderStatus_In_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument 
            { 
                Id = "30", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test30",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Processing
            },
            new TestDocument 
            { 
                Id = "31", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test31",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Completed
            },
            new TestDocument 
            { 
                Id = "32", 
                EsDateTime = new DateTime(2024, 1, 15), 
                TextField = "Test32",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = null
            },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";
        // 使用可空枚举类型数组，以便可以直接使用 Contains 查询可空枚举字段
        var statuses = new OrderStatus?[] { OrderStatus.Processing, OrderStatus.Completed };

        // Act
        // 对于可空枚举类型，可以直接使用 Contains 查询
        // 如果 NullableOrderStatus 是 null，Contains(null) 会返回 false（因为 statuses 中不包含 null）
        // 如果 NullableOrderStatus 是 Processing 或 Completed，Contains 会返回 true
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => statuses.Contains(x.NullableOrderStatus))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        // 查询 NullableOrderStatus 在 [Processing, Completed] 中的记录应该返回：
        // - 本测试中插入的 Id = "30"（NullableOrderStatus = Processing）
        // - 本测试中插入的 Id = "31"（NullableOrderStatus = Completed）
        // 总共 2 条记录（排除 Id = "32"，因为它的 NullableOrderStatus = null，不在列表中）
        // 注意：InitializeAsync 中插入的文档（Id = "1", "2", "3", "4", "5"）的 NullableOrderStatus 都是 null，不在列表中，所以不会被包含
        result.Documents.Should().HaveCount(2);
        result.Documents.All(x => statuses.Contains(x.NullableOrderStatus)).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "30", "31" });
    }

    /// <summary>
    /// 测试可空枚举字段的非空判断与 In 查询组合。
    /// 该场景对应 `x.NullableOrderStatus != null && statuses.Contains(x.NullableOrderStatus)`，
    /// 应翻译为 exists + terms，而不是错误地查询 `nullableOrderStatus.hasValue`。
    /// </summary>
    [Fact]
    public async Task Where_NullableOrderStatus_NotNull_AndIn_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument
            {
                Id = "40",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test40",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Processing
            },
            new TestDocument
            {
                Id = "41",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test41",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Completed
            },
            new TestDocument
            {
                Id = "42",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test42",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = null
            },
            new TestDocument
            {
                Id = "43",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test43",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Cancelled
            },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";
        var statuses = new OrderStatus?[] { OrderStatus.Processing, OrderStatus.Completed };

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableOrderStatus != null && statuses.Contains(x.NullableOrderStatus))
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(2);
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "40", "41" });
        result.Documents.All(x => x.NullableOrderStatus.HasValue).Should().BeTrue();
        result.Documents.All(x => statuses.Contains(x.NullableOrderStatus)).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空枚举字段的空值判断应翻译为 must_not exists。
    /// </summary>
    [Fact]
    public async Task Where_NullableOrderStatus_EqualsNull_ShouldReturnDocumentsWithoutValue()
    {
        // Arrange
        var documents = new[]
        {
            new TestDocument
            {
                Id = "50",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test50",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = OrderStatus.Processing
            },
            new TestDocument
            {
                Id = "51",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test51",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = null
            },
            new TestDocument
            {
                Id = "52",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test52",
                OrderStatus = OrderStatus.Pending,
                NullableOrderStatus = null
            },
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-documents-2024-01");

        var indexName = "test-documents-2024-01";

        // Act
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.NullableOrderStatus == null)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(7);
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "2", "3", "4", "5", "51", "52" });
        result.Documents.All(x => x.NullableOrderStatus == null).Should().BeTrue();
    }

    /// <summary>
    /// 测试枚举类型不支持范围查询（应该抛出异常）
    /// 根据 ExpressionParser 的逻辑，枚举类型不支持 >, <, >=, <= 等范围查询
    /// 注意：异常会被 Elasticsearch 客户端包装为 UnexpectedTransportException
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_GreaterThan_ShouldThrowException()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act & Assert
        // 枚举类型不支持范围查询，异常会被包装为 UnexpectedTransportException
        var exception = await Assert.ThrowsAsync<UnexpectedTransportException>(async () =>
        {
            await Client.Search<TestDocument>(indexName)
                .Where(x => x.OrderStatus > OrderStatus.Pending)
                .ToListAsync();
        });

        // 验证内部异常是 ArgumentException 且包含正确的错误消息
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Contain("枚举类型不支持范围查询");
    }

    /// <summary>
    /// 测试枚举类型不支持小于查询（应该抛出异常）
    /// 注意：异常会被 Elasticsearch 客户端包装为 UnexpectedTransportException
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_LessThan_ShouldThrowException()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnexpectedTransportException>(async () =>
        {
            await Client.Search<TestDocument>(indexName)
                .Where(x => x.OrderStatus < OrderStatus.Completed)
                .ToListAsync();
        });

        // 验证内部异常是 ArgumentException 且包含正确的错误消息
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Contain("枚举类型不支持范围查询");
    }

    /// <summary>
    /// 测试枚举类型不支持大于等于查询（应该抛出异常）
    /// 注意：异常会被 Elasticsearch 客户端包装为 UnexpectedTransportException
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_GreaterThanOrEqual_ShouldThrowException()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnexpectedTransportException>(async () =>
        {
            await Client.Search<TestDocument>(indexName)
                .Where(x => x.OrderStatus >= OrderStatus.Processing)
                .ToListAsync();
        });

        // 验证内部异常是 ArgumentException 且包含正确的错误消息
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Contain("枚举类型不支持范围查询");
    }

    /// <summary>
    /// 测试枚举类型不支持小于等于查询（应该抛出异常）
    /// 注意：异常会被 Elasticsearch 客户端包装为 UnexpectedTransportException
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_LessThanOrEqual_ShouldThrowException()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnexpectedTransportException>(async () =>
        {
            await Client.Search<TestDocument>(indexName)
                .Where(x => x.OrderStatus <= OrderStatus.Processing)
                .ToListAsync();
        });

        // 验证内部异常是 ArgumentException 且包含正确的错误消息
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Contain("枚举类型不支持范围查询");
    }

    /// <summary>
    /// 测试可空枚举类型不支持范围查询（应该抛出异常）
    /// 注意：异常会被 Elasticsearch 客户端包装为 UnexpectedTransportException
    /// </summary>
    [Fact]
    public async Task Where_NullableOrderStatus_GreaterThan_ShouldThrowException()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnexpectedTransportException>(async () =>
        {
            await Client.Search<TestDocument>(indexName)
                .Where(x => x.NullableOrderStatus > OrderStatus.Pending)
                .ToListAsync();
        });

        // 验证内部异常是 ArgumentException 且包含正确的错误消息
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Contain("枚举类型不支持范围查询");
    }

    /// <summary>
    /// 测试组合查询：枚举类型与其他类型的组合
    /// 验证枚举类型可以与其他条件进行 AND 组合查询
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_And_TextField_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 OrderStatus == Pending 且 TextField == "Test1" 的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.OrderStatus == OrderStatus.Pending && x.TextField == "Test1")
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(1);
        result.Documents.First().Id.Should().Be("1");
        result.Documents.First().OrderStatus.Should().Be(OrderStatus.Pending);
        result.Documents.First().TextField.Should().Be("Test1");
    }

    /// <summary>
    /// 测试组合查询：枚举类型的 OR 查询
    /// 验证多个枚举值可以通过 OR 组合查询
    /// </summary>
    [Fact]
    public async Task Where_OrderStatus_Or_ShouldReturnMatchingDocuments()
    {
        // Arrange
        var indexName = "test-documents-2024-01";

        // Act - 查询 OrderStatus == Pending 或 OrderStatus == Completed 的记录
        var result = await Client.Search<TestDocument>(indexName)
            .Where(x => x.OrderStatus == OrderStatus.Pending || x.OrderStatus == OrderStatus.Completed)
            .ToListAsync();

        // Assert
        result.IsSuccess().Should().BeTrue();
        result.Documents.Should().HaveCount(3); // Id = "1", 3, 5
        result.Documents.All(x => x.OrderStatus == OrderStatus.Pending || x.OrderStatus == OrderStatus.Completed).Should().BeTrue();
        result.Documents.Select(x => x.Id).Should().Contain(new[] { "1", "3", "5" });
    }
}


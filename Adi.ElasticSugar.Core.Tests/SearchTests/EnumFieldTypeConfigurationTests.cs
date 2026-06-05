using Adi.ElasticSugar.Core.Document;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Tests.Models;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Xunit;

namespace Adi.ElasticSugar.Core.Tests.SearchTests;

/// <summary>
/// 枚举字段类型配置测试
/// 测试枚举字段配置为不同 FieldType 时的索引创建、文档推送和查询行为
/// </summary>
public class EnumFieldTypeConfigurationTests : TestBase
{
    /// <summary>
    /// 测试枚举字段配置为 integer 类型时的索引创建、文档推送和查询
    /// 验证枚举值会被存储为数值而不是名称
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsInteger_ShouldStoreAsNumeric()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "1",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test1",
                OrderStatusAsInt = OrderStatus.Pending, // 值为 0
                UserRoleAsLong = UserRole.Admin // 值为 10
            },
            new EnumFieldTypeTestDocument
            {
                Id = "2",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test2",
                OrderStatusAsInt = OrderStatus.Processing, // 值为 1
                UserRoleAsLong = UserRole.User // 值为 20
            },
            new EnumFieldTypeTestDocument
            {
                Id = "3",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test3",
                OrderStatusAsInt = OrderStatus.Completed, // 值为 2
                UserRoleAsLong = UserRole.Guest // 值为 30
            }
        };

        // Act - 推送文档（会自动创建索引）
        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Assert - 验证查询时使用数值而不是名称
        var result1 = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsInt == OrderStatus.Pending)
            .ToListAsync();

        result1.Should().HaveCount(1);
        result1.First().Id.Should().Be("1");
        result1.First().OrderStatusAsInt.Should().Be(OrderStatus.Pending);

        // 验证使用整数值查询也能正常工作
        var result2 = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsInt == (OrderStatus)1) // Processing = 1
            .ToListAsync();

        result2.Should().HaveCount(1);
        result2.First().Id.Should().Be("2");
        result2.First().OrderStatusAsInt.Should().Be(OrderStatus.Processing);
    }

    /// <summary>
    /// 测试枚举字段配置为 integer 类型时的 In 查询
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsInteger_InQuery_ShouldWork()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "10",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test10",
                OrderStatusAsInt = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "11",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test11",
                OrderStatusAsInt = OrderStatus.Processing
            },
            new EnumFieldTypeTestDocument
            {
                Id = "12",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test12",
                OrderStatusAsInt = OrderStatus.Completed
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        var statuses = new[] { OrderStatus.Pending, OrderStatus.Completed };

        // Act
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => statuses.Contains(x.OrderStatusAsInt))
            .ToListAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Select(x => x.Id).Should().Contain(new[] { "10", "12" });
        result.All(x => statuses.Contains(x.OrderStatusAsInt)).Should().BeTrue();
    }

    /// <summary>
    /// 测试枚举字段配置为 integer 类型时支持范围查询
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsInteger_RangeQuery_ShouldWork()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "20",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test20",
                OrderStatusAsInt = OrderStatus.Pending // 0
            },
            new EnumFieldTypeTestDocument
            {
                Id = "21",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test21",
                OrderStatusAsInt = OrderStatus.Processing // 1
            },
            new EnumFieldTypeTestDocument
            {
                Id = "22",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test22",
                OrderStatusAsInt = OrderStatus.Completed // 2
            },
            new EnumFieldTypeTestDocument
            {
                Id = "23",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test23",
                OrderStatusAsInt = OrderStatus.Cancelled // 3
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 OrderStatusAsInt >= Processing (1) 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsInt >= OrderStatus.Processing)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(3); // Processing, Completed, Cancelled
        result.Select(x => x.Id).Should().Contain(new[] { "21", "22", "23" });
        result.All(x => x.OrderStatusAsInt >= OrderStatus.Processing).Should().BeTrue();
    }

    /// <summary>
    /// 测试可空枚举字段配置为 integer 类型时的查询
    /// </summary>
    [Fact]
    public async Task NullableEnumField_ConfiguredAsInteger_ShouldWork()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "30",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test30",
                NullableOrderStatusAsInt = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "31",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test31",
                NullableOrderStatusAsInt = OrderStatus.Processing
            },
            new EnumFieldTypeTestDocument
            {
                Id = "32",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test32",
                NullableOrderStatusAsInt = null
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 NullableOrderStatusAsInt == Processing 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.NullableOrderStatusAsInt == OrderStatus.Processing)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be("31");
        result.First().NullableOrderStatusAsInt.Should().Be(OrderStatus.Processing);
    }

    /// <summary>
    /// 测试枚举字段配置为 keyword 类型时的查询（向后兼容性）
    /// 验证枚举值会被存储为名称，查询时使用名称
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsKeyword_ShouldStoreAsName()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "40",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test40",
                OrderStatusAsKeyword = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "41",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test41",
                OrderStatusAsKeyword = OrderStatus.Processing
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 OrderStatusAsKeyword == Pending 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsKeyword == OrderStatus.Pending)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be("40");
        result.First().OrderStatusAsKeyword.Should().Be(OrderStatus.Pending);
    }

    /// <summary>
    /// 测试枚举字段配置为 text 类型时的查询（向后兼容性）
    /// 验证枚举值会被存储为名称，查询时使用名称（需要 .keyword 后缀）
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsText_ShouldStoreAsName()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "50",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test50",
                OrderStatusAsText = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "51",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test51",
                OrderStatusAsText = OrderStatus.Processing
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 OrderStatusAsText == Pending 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsText == OrderStatus.Pending)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be("50");
        result.First().OrderStatusAsText.Should().Be(OrderStatus.Pending);
    }

    /// <summary>
    /// 测试枚举字段未配置 FieldType 时的默认行为（向后兼容性）
    /// 验证枚举值会被存储为名称，查询时使用名称
    /// </summary>
    [Fact]
    public async Task EnumField_DefaultConfiguration_ShouldStoreAsName()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "60",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test60",
                OrderStatusDefault = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "61",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test61",
                OrderStatusDefault = OrderStatus.Processing
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 OrderStatusDefault == Pending 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusDefault == OrderStatus.Pending)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be("60");
        result.First().OrderStatusDefault.Should().Be(OrderStatus.Pending);
    }

    /// <summary>
    /// 测试枚举字段配置为 integer 类型时，不支持范围查询的枚举字段应该抛出异常
    /// 验证未配置为数值类型的枚举字段不支持范围查询
    /// </summary>
    [Fact]
    public async Task EnumField_NotConfiguredAsNumeric_RangeQuery_ShouldThrowException()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "70",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test70",
                OrderStatusAsKeyword = OrderStatus.Pending
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act & Assert - 枚举字段配置为 keyword，不支持范围查询
        var exception = await Assert.ThrowsAsync<Elastic.Transport.UnexpectedTransportException>(async () =>
        {
            await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
                .Where(x => x.OrderStatusAsKeyword > OrderStatus.Pending)
                .ToListAsync();
        });

        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Contain("枚举类型不支持范围查询");
    }

    /// <summary>
    /// 测试组合查询：数值类型枚举字段与其他条件的组合
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsInteger_CombinedWithOtherConditions_ShouldWork()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "80",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test80",
                OrderStatusAsInt = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "81",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test81",
                OrderStatusAsInt = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "82",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test82",
                OrderStatusAsInt = OrderStatus.Processing
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 OrderStatusAsInt == Pending 且 TextField == "Test80" 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsInt == OrderStatus.Pending && x.TextField == "Test80")
            .ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be("80");
        result.First().OrderStatusAsInt.Should().Be(OrderStatus.Pending);
        result.First().TextField.Should().Be("Test80");
    }

    /// <summary>
    /// 测试枚举字段配置为 long 类型时的查询
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsLong_ShouldWork()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "90",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test90",
                UserRoleAsLong = UserRole.Admin // 值为 10
            },
            new EnumFieldTypeTestDocument
            {
                Id = "91",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test91",
                UserRoleAsLong = UserRole.User // 值为 20
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 UserRoleAsLong == Admin 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.UserRoleAsLong == UserRole.Admin)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Id.Should().Be("90");
        result.First().UserRoleAsLong.Should().Be(UserRole.Admin);
    }

    /// <summary>
    /// 测试枚举字段配置为 integer 类型时的 OR 查询
    /// </summary>
    [Fact]
    public async Task EnumField_ConfiguredAsInteger_OrQuery_ShouldWork()
    {
        // Arrange
        var documents = new[]
        {
            new EnumFieldTypeTestDocument
            {
                Id = "100",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test100",
                OrderStatusAsInt = OrderStatus.Pending
            },
            new EnumFieldTypeTestDocument
            {
                Id = "101",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test101",
                OrderStatusAsInt = OrderStatus.Processing
            },
            new EnumFieldTypeTestDocument
            {
                Id = "102",
                EsDateTime = new DateTime(2024, 1, 15),
                TextField = "Test102",
                OrderStatusAsInt = OrderStatus.Completed
            }
        };

        await Client.PushDocumentsAsync(documents);
        await RefreshIndexAsync("test-enum-field-type-2024-01");

        // Act - 查询 OrderStatusAsInt == Pending 或 OrderStatusAsInt == Completed 的记录
        var result = await Client.Search<EnumFieldTypeTestDocument>("test-enum-field-type-2024-01")
            .Where(x => x.OrderStatusAsInt == OrderStatus.Pending || x.OrderStatusAsInt == OrderStatus.Completed)
            .ToListAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Select(x => x.Id).Should().Contain(new[] { "100", "102" });
        result.All(x => x.OrderStatusAsInt == OrderStatus.Pending || x.OrderStatusAsInt == OrderStatus.Completed).Should().BeTrue();
    }
}


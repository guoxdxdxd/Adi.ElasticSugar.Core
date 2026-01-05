# ElasticSugar.Core - ElasticSearch ORM 风格查询构建器

一个功能完整的 ElasticSearch .NET 客户端库，提供类似数据库 ORM（如 SqlSugar）的使用体验。支持自动索引管理、文档推送和强大的 LINQ 风格查询构建。

## 特性

### 核心功能

- ✅ **ORM 风格查询**：使用 `Where` 方法构建查询条件，类似 Entity Framework 或 SqlSugar
- ✅ **自动索引管理**：推送文档时自动检查并创建索引，无需手动管理
- ✅ **智能索引命名**：支持基于年、年月的自动索引命名，支持自定义生成器
- ✅ **Lambda 表达式**：通过 Lambda 表达式自动构建字段路径，无需手动指定字符串
- ✅ **逻辑操作符**：使用 `||` 操作符实现 OR 逻辑，使用 `&&` 操作符实现 AND 逻辑
- ✅ **条件判断**：支持 `WhereIf` 方法，根据条件动态添加查询条件
- ✅ **类型安全**：编译时检查，减少运行时错误
- ✅ **批量操作**：支持批量推送文档，自动分批处理，提高性能
- ✅ **自动映射**：根据 C# 类型自动生成 Elasticsearch 字段映射

### 查询功能

- ✅ **丰富的操作符**：支持 `>`, `<`, `>=`, `<=`, `==`, `!=` 等比较操作符
- ✅ **字符串扩展**：支持 `Contains`, `StartsWith`, `EndsWith` 等字符串方法
- ✅ **集合查询**：支持 `In` 查询（通过 `Contains` 方法）
- ✅ **排序和分页**：支持 `OrderBy`、`OrderByDesc`、`Skip`、`Take` 等方法
- ✅ **总记录数**：支持 `TrackTotalHits` 获取准确的分页总数

## 安装

### 使用 .NET CLI

```bash
dotnet add package Adi.ElasticSugar.Core
```

### 使用 Package Manager

```powershell
Install-Package Adi.ElasticSugar.Core
```

### 使用 PackageReference

在 `.csproj` 文件中添加：

```xml
<ItemGroup>
  <PackageReference Include="Adi.ElasticSugar.Core" Version="1.0.0" />
</ItemGroup>
```

## 快速开始

### 1. 定义文档模型

```csharp
using Adi.ElasticSugar.Core.Models;

// 使用特性配置索引
[EsIndex(IndexPrefix = "orders", Format = IndexFormat.YearMonth)]
public class OrderDto : BaseEsModel
{
    public string OrderNo { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedDate { get; set; }
    public int Status { get; set; }
}
```

### 2. 推送文档

```csharp
var order = new OrderDto
{
    Id = Guid.NewGuid(),
    EsDateTime = DateTime.Now,
    OrderNo = "ORD-001",
    Amount = 1000.00m,
    CreatedDate = DateTime.Now,
    Status = 1
};

// 推送单个文档（自动创建索引）
await _elasticsearchClient.PushDocumentAsync(order);

// 批量推送文档（自动创建索引，自动分批处理）
var orders = new List<OrderDto> { /* ... */ };
await _elasticsearchClient.PushDocumentsAsync(orders, batchSize: 1000);
```

### 3. 查询文档

```csharp
// 从 ElasticsearchClient 开始链式调用
var result = await _elasticsearchClient.Search<OrderDto>("orders*")
    .Where(x => x.Status == 1)
    .Where(x => x.CreatedDate >= DateTime.Now.AddDays(-7))
    .OrderByDesc(x => x.CreatedDate)
    .Skip(0)
    .Take(20)
    .TrackTotalHits()
    .ToListAsync();
```

## 详细功能说明

## 一、文档模型定义

### 1.1 基础模型

所有需要存储到 Elasticsearch 的文档类型都应该继承 `BaseEsModel`：

```csharp
public abstract class BaseEsModel
{
    /// <summary>
    /// 文档 ID
    /// </summary>
    public object? Id { get; set; }

    /// <summary>
    /// ElasticSearch 时间字段
    /// 用于索引名称自动生成（基于年月）
    /// </summary>
    public DateTime EsDateTime { get; set; }
}
```

### 1.2 索引配置特性

使用 `EsIndexAttribute` 特性配置索引：

```csharp
[EsIndex(IndexPrefix = "orders", Format = IndexFormat.YearMonth)]
public class OrderDto : BaseEsModel
{
    // ...
}
```

**特性参数说明：**
- `IndexPrefix`：索引前缀，如果未设置则使用类名的小写形式
- `Format`：索引格式，支持 `YearMonth`（年月，如 `orders-2024-01`）和 `Year`（年，如 `orders-2024`）
- `CustomGeneratorType`：自定义索引名称生成器类型

### 1.3 字段映射特性

使用 `EsFieldAttribute` 特性配置字段映射：

```csharp
public class OrderDto : BaseEsModel
{
    [EsField(FieldType = "text", Analyzer = "ik_max_word")]
    public string Description { get; set; }

    [EsField(FieldType = "keyword")]
    public string OrderNo { get; set; }

    [EsField(Ignore = true)]
    public string InternalField { get; set; }
}
```

**特性参数说明：**
- `FieldType`：字段类型（text, keyword, long, integer, date, boolean 等）
- `IsNested`：是否为嵌套文档
- `NeedKeyword`：是否需要 keyword 子字段（用于 text 类型）
- `Ignore`：是否忽略该字段
- `Analyzer`：字段分析器
- `SearchAnalyzer`：搜索分析器
- `Index`：是否启用索引
- `Store`：是否存储字段值

**重要说明：** 
- **字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持在运行时手动配置**
- 所有字段映射都会根据特性自动生成，确保配置的一致性和可维护性
- 如果字段没有特性，系统会根据字段类型自动推断映射配置

## 二、文档推送

### 2.1 推送单个文档

```csharp
var order = new OrderDto
{
    Id = Guid.NewGuid(),
    EsDateTime = DateTime.Now,
    // ... 其他属性
};

// 推送文档（自动检查并创建索引）
// 字段映射配置通过 EsFieldAttribute 特性完成，无需手动配置
var response = await _elasticsearchClient.PushDocumentAsync(order);
```

**方法签名：**
```csharp
Task<IndexResponse> PushDocumentAsync<T>(
    this ElasticsearchClient client,
    T document,
    int numberOfShards = 3,
    int numberOfReplicas = 1) where T : BaseEsModel
```

**参数说明：**
- `document`：要推送的文档
- `numberOfShards`：分片数量，仅在创建索引时使用，默认 3
- `numberOfReplicas`：副本数量，仅在创建索引时使用，默认 1

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

### 2.2 批量推送文档

```csharp
var orders = new List<OrderDto> { /* ... */ };

// 批量推送（自动检查并创建索引，自动分批处理）
// 字段映射配置通过 EsFieldAttribute 特性完成，无需手动配置
var response = await _elasticsearchClient.PushDocumentsAsync(
    orders,
    batchSize: 1000  // 每批处理 1000 条
);
```

**方法签名：**
```csharp
Task<BulkResponse> PushDocumentsAsync<T>(
    this ElasticsearchClient client,
    IEnumerable<T> documents,
    int numberOfShards = 3,
    int numberOfReplicas = 1,
    int batchSize = 1000) where T : BaseEsModel
```

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

**特性：**
- 自动按索引名称分组文档
- 相同的索引名称只会创建一次
- 超过 `batchSize` 的文档会自动分批处理
- 使用 Bulk API 提高性能

## 三、索引管理

### 3.1 根据文档创建索引

```csharp
var order = new OrderDto { EsDateTime = DateTime.Now };

// 根据文档创建索引（如果已存在则不创建）
// 字段映射配置通过 EsFieldAttribute 特性完成，无需手动配置
var indexName = await _elasticsearchClient.CreateIndexForDocumentAsync(order);
```

### 3.2 批量创建索引

```csharp
var orders = new List<OrderDto> { /* ... */ };

// 批量创建索引（自动去重）
var documentsByIndex = await _elasticsearchClient.CreateIndexesForDocumentsAsync(orders);

// 返回字典：索引名称 -> 文档列表
foreach (var kvp in documentsByIndex)
{
    Console.WriteLine($"索引: {kvp.Key}, 文档数量: {kvp.Value.Count}");
}
```

### 3.3 索引管理器

```csharp
var manager = _elasticsearchClient.IndexManager();

// 检查索引是否存在
bool exists = await manager.IndexExistsAsync("orders-2024-01");

// 删除索引
await manager.DeleteIndexAsync("orders-2024-01");

// 清除缓存
manager.ClearCache();
manager.ClearCache("orders-2024-01");
```

**索引管理器特性：**
- 内置缓存机制，减少重复检查
- 线程安全的索引创建
- 支持并行创建多个索引

## 四、文档查询

### 4.1 创建查询构建器

```csharp
// 单个索引
var query = _elasticsearchClient.Search<OrderDto>("orders-2024-01");

// 多个索引（使用通配符）
var query = _elasticsearchClient.Search<OrderDto>("orders*");

// 多个索引（使用逗号分隔）
var query = _elasticsearchClient.Search<OrderDto>("orders-2024-01,orders-2024-02");
```

### 4.2 Where 条件查询

#### 4.2.1 基本比较操作

```csharp
// 等于查询
query.Where(x => x.Status == 1);

// 不等于查询
query.Where(x => x.Status != 0);

// 大于查询
query.Where(x => x.CreatedDate > DateTime.Now.AddDays(-30));

// 小于等于查询
query.Where(x => x.Amount <= 1000);

// 大于等于查询
query.Where(x => x.Amount >= 100);

// 小于查询
query.Where(x => x.Amount < 5000);
```

#### 4.2.2 空值判断

```csharp
// 判断字段为 null
query.Where(x => x.OrderNo == null);

// 判断字段不为 null
query.Where(x => x.OrderNo != null);

// 判断字符串为空或空字符串
query.Where(x => x.OrderNo == null || x.OrderNo == "");
```

#### 4.2.3 条件判断（WhereIf）

```csharp
// 当 Status 不为空时才添加条件
query.WhereIf(status.HasValue, x => x.Status == status.Value);

// 当 StartDate 有值时才添加范围查询
query.WhereIf(req.StartDate.HasValue, 
    x => x.CreatedDate >= req.StartDate.Value);

// 当 EndDate 有值时才添加范围查询
query.WhereIf(req.EndDate.HasValue, 
    x => x.CreatedDate <= req.EndDate.Value);
```

### 4.3 逻辑操作符

#### 4.3.1 AND 逻辑

多个 `Where` 方法链式调用时，它们之间是 AND 关系。

```csharp
// 多个 Where 之间是 AND 关系
query
    .Where(x => x.Status == 1)
    .Where(x => x.CreatedDate > DateTime.Now.AddDays(-7))
    .Where(x => x.Amount > 100);
// 等价于：Status == 1 AND CreatedDate > ... AND Amount > 100
```

在同一个 `Where` 中使用 `&&` 操作符也可以实现 AND 逻辑。

```csharp
// 在同一个 Where 中使用 && 实现 AND 逻辑
query.Where(x => 
    x.Status == 1 
    && x.CreatedDate > DateTime.Now.AddDays(-7)
    && x.Amount > 100);
```

#### 4.3.2 OR 逻辑

在同一个 `Where` 中使用 `||` 操作符实现 OR 逻辑。

```csharp
// 使用 || 操作符实现 OR 逻辑
query.Where(x => 
    x.Status == 1 || x.Status == 2);
// 等价于：Status == 1 OR Status == 2
```

#### 4.3.3 复杂逻辑组合

可以组合使用 `&&` 和 `||` 操作符，使用括号控制优先级。

```csharp
// 复杂组合：使用括号控制优先级
query.Where(x => 
    (x.Status == 1 || x.Status == 2) 
    && x.CreatedDate > DateTime.Now.AddDays(-7)
    && x.Amount > 100);
// 等价于：(Status == 1 OR Status == 2) AND CreatedDate > ... AND Amount > 100
```

### 4.4 字符串查询

#### 4.4.1 Contains - 包含查询

```csharp
// 查询订单号包含 "ORD" 的订单
query.Where(x => x.OrderNo.Contains("ORD"));

// 查询描述包含关键词的订单
query.Where(x => x.Description.Contains("urgent"));
```

#### 4.4.2 StartsWith - 以...开头

```csharp
// 查询订单号以 "ORD" 开头的订单
query.Where(x => x.OrderNo.StartsWith("ORD"));
```

#### 4.4.3 EndsWith - 以...结尾

```csharp
// 查询订单号以 "001" 结尾的订单
query.Where(x => x.OrderNo.EndsWith("001"));
```

### 4.5 集合查询（In 查询）

使用集合的 `Contains` 方法实现 `In` 查询。

```csharp
// 查询状态在指定列表中的订单
var statusList = new[] { 1, 2, 3 };
query.Where(x => statusList.Contains(x.Status));

// 查询销售组在指定列表中的订单
var salesGroups = new[] { "GROUP_A", "GROUP_B", "GROUP_C" };
query.Where(x => salesGroups.Contains(x.SalesGroup));
```

### 4.6 排序

#### 4.6.1 升序排序

```csharp
// 按创建日期升序排序
query.OrderBy(x => x.CreatedDate);

// 按金额升序排序
query.OrderBy(x => x.Amount);
```

#### 4.6.2 降序排序

```csharp
// 按创建日期降序排序
query.OrderByDesc(x => x.CreatedDate);

// 按金额降序排序
query.OrderByDesc(x => x.Amount);
```

#### 4.6.3 多字段排序

```csharp
// 先按创建日期降序，再按金额升序
query
    .OrderByDesc(x => x.CreatedDate)
    .OrderBy(x => x.Amount);
```

### 4.7 分页

#### 4.7.1 Skip 和 Take

```csharp
// 跳过前 10 条，获取 20 条
query
    .Skip(10)
    .Take(20);

// 分页计算示例
int pageIndex = 1;
int pageSize = 20;
query
    .Skip((pageIndex - 1) * pageSize)
    .Take(pageSize);
```

#### 4.7.2 TrackTotalHits

`TrackTotalHits` 方法用于启用跟踪总命中数，这对于分页查询非常重要。

```csharp
query
    .Skip(0)
    .Take(20)
    .TrackTotalHits();  // 启用跟踪总命中数，可以获取总记录数
```

**注意：** 如果不调用此方法，Elasticsearch 默认只返回前 10,000 条记录的总数。

### 4.8 执行查询

使用 `ToListAsync()` 方法执行查询并返回结果。

```csharp
// 执行查询并返回结果
var response = await query.ToListAsync();

// 获取文档列表
var documents = response.Documents;

// 获取总记录数（需要调用 TrackTotalHits）
var total = response.Total;

// 完整示例
var response = await _elasticsearchClient.Search<OrderDto>("orders*")
    .Where(x => x.Status == 1)
    .Where(x => x.CreatedDate >= DateTime.Now.AddDays(-7))
    .OrderByDesc(x => x.CreatedDate)
    .Skip(0)
    .Take(20)
    .TrackTotalHits()
    .ToListAsync();

var orders = response.Documents.ToList();
var totalCount = response.Total;
```

### 4.9 分页查询

使用 `ToPageAsync` 方法进行分页查询：

```csharp
var response = await _elasticsearchClient.Search<OrderDto>("orders*")
    .Where(x => x.Status == 1)
    .TrackTotalHits()
    .ToPageAsync(pageIndex: 1, pageSize: 20);

var orders = response.Documents.ToList();
var totalCount = response.Total;
```

## 五、索引名称生成

### 5.1 自动生成索引名称

索引名称会根据文档类型的 `EsIndexAttribute` 特性和文档的 `EsDateTime` 字段自动生成：

```csharp
[EsIndex(IndexPrefix = "orders", Format = IndexFormat.YearMonth)]
public class OrderDto : BaseEsModel
{
    public DateTime EsDateTime { get; set; }
}

var order = new OrderDto { EsDateTime = new DateTime(2024, 1, 15) };
// 索引名称：orders-2024-01
```

### 5.2 索引格式

支持两种索引格式：

- **YearMonth**（年月格式）：`{prefix}-{yyyy-MM}`，例如 `orders-2024-01`
- **Year**（年格式）：`{prefix}-{yyyy}`，例如 `orders-2024`

```csharp
// 年月格式（默认）
[EsIndex(IndexPrefix = "orders", Format = IndexFormat.YearMonth)]
// 索引名称：orders-2024-01

// 年格式
[EsIndex(IndexPrefix = "orders", Format = IndexFormat.Year)]
// 索引名称：orders-2024
```

### 5.3 自定义索引名称生成器

实现 `IIndexNameGenerator<T>` 接口可以完全自定义索引名称生成逻辑：

```csharp
public class CustomOrderIndexNameGenerator : IIndexNameGenerator<OrderDto>
{
    public string GenerateIndexName(OrderDto document)
    {
        // 自定义生成逻辑
        return $"orders-{document.EsDateTime:yyyy-MM-dd}";
    }

    public string GenerateIndexName(DateTime dateTime)
    {
        return $"orders-{dateTime:yyyy-MM-dd}";
    }

    public string GenerateIndexPattern()
    {
        return "orders-*";
    }
}
```

**使用自定义生成器：**

```csharp
// 方式1：在特性中指定
[EsIndex(CustomGeneratorType = typeof(CustomOrderIndexNameGenerator))]
public class OrderDto : BaseEsModel { }

// 方式2：运行时注册
IndexNameGenerator.RegisterGenerator<OrderDto>(new CustomOrderIndexNameGenerator());
```

### 5.4 手动获取索引名称

```csharp
var order = new OrderDto { EsDateTime = DateTime.Now };

// 从文档实例获取索引名称
string indexName = order.GetIndexNameFromAttribute();

// 从类型获取索引名称（基于当前时间）
string indexName = IndexNameGenerator.GenerateIndexNameFromAttribute<OrderDto>();

// 从类型获取索引名称（基于指定时间）
string indexName = IndexNameGenerator.GenerateIndexNameFromAttribute<OrderDto>(DateTime.Now);

// 生成索引通配符模式
string pattern = IndexNameGenerator.GenerateIndexPatternFromAttribute<OrderDto>();
// 返回：orders-*
```

## 六、完整示例

以下是一个完整的实际使用示例，展示了如何组合使用各种功能：

```csharp
using Adi.ElasticSugar.Core;
using Adi.ElasticSugar.Core.Models;

// 1. 定义文档模型
[EsIndex(IndexPrefix = "orders", Format = IndexFormat.YearMonth)]
public class OrderDto : BaseEsModel
{
    public string OrderNo { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedDate { get; set; }
    public int Status { get; set; }
    public string SalesGroup { get; set; }
}

// 2. 推送文档
public async Task PushOrderAsync(OrderDto order)
{
    // 单个推送
    await _elasticsearchClient.PushDocumentAsync(order);
    
    // 批量推送
    var orders = new List<OrderDto> { /* ... */ };
    await _elasticsearchClient.PushDocumentsAsync(orders, batchSize: 1000);
}

// 3. 查询文档
public async Task<List<OrderDto>> SearchOrdersAsync(SearchRequest req)
{
    var response = await _elasticsearchClient.Search<OrderDto>("orders*")
        // 条件判断：SalesGroup 不为空时添加等于条件
        .WhereIf(!string.IsNullOrEmpty(req.SalesGroup), 
            x => x.SalesGroup == req.SalesGroup)
        
        // OR 逻辑：Status 为 2 时，Status=2 或 SalesGroup="DEFAULT"
        .WhereIf(req.Status == 2,
            x => x.Status == 2 || x.SalesGroup == "DEFAULT")
        
        // 条件判断：Status 有值且不等于 2 时添加等于条件
        .WhereIf(req.Status.HasValue && req.Status != 2,
            x => x.Status == req.Status.Value)
        
        // 范围查询：创建日期大于等于某个值
        .WhereIf(req.StartDate.HasValue, 
            x => x.CreatedDate >= req.StartDate.Value)
        
        // 范围查询：创建日期小于等于某个值
        .WhereIf(req.EndDate.HasValue, 
            x => x.CreatedDate <= req.EndDate.Value)
        
        // 字符串查询：订单号包含关键词
        .WhereIf(!string.IsNullOrEmpty(req.OrderNoKeyword),
            x => x.OrderNo.Contains(req.OrderNoKeyword))
        
        // In 查询：状态在指定列表中
        .WhereIf(req.StatusList != null && req.StatusList.Any(),
            x => req.StatusList.Contains(x.Status))
        
        // 排序：按创建日期降序
        .OrderByDesc(x => x.CreatedDate)
        
        // 分页
        .Skip((req.PageIndex - 1) * req.PageSize)
        .Take(req.PageSize)
        
        // 跟踪总命中数
        .TrackTotalHits()
        
        // 执行查询
        .ToListAsync();
    
    return response.Documents.ToList();
}
```

## 七、支持的表达式和操作符

### 比较操作符

| 操作符 | 说明 | 示例 |
|--------|------|------|
| `==` | 等于 | `x.Status == 1` |
| `!=` | 不等于 | `x.Status != 0` |
| `>` | 大于 | `x.Amount > 100` |
| `<` | 小于 | `x.Amount < 1000` |
| `>=` | 大于等于 | `x.CreatedDate >= DateTime.Now` |
| `<=` | 小于等于 | `x.CreatedDate <= DateTime.Now` |

### 逻辑操作符

| 操作符 | 说明 | 使用场景 |
|--------|------|----------|
| `&&` | AND 逻辑 | 在同一个 `Where` 中使用，连接多个条件 |
| `||` | OR 逻辑 | 在同一个 `Where` 中使用，表示或的关系 |
| 多个 `Where` | AND 逻辑 | 链式调用多个 `Where` 方法，它们之间是 AND 关系 |

### 字符串方法

| 方法 | 说明 | 示例 |
|------|------|------|
| `Contains(value)` | 包含指定字符串 | `x.OrderNo.Contains("ORD")` |
| `StartsWith(value)` | 以指定字符串开头 | `x.OrderNo.StartsWith("ORD")` |
| `EndsWith(value)` | 以指定字符串结尾 | `x.OrderNo.EndsWith("001")` |

### 集合方法

| 方法 | 说明 | 示例 |
|------|------|------|
| `collection.Contains(field)` | In 查询，字段值在集合中 | `statusList.Contains(x.Status)` |

## 八、类型支持

支持以下数据类型的查询：

- **数字类型**：`int`, `long`, `double`, `decimal`, `float`, `short`, `byte` 等
- **字符串类型**：`string`
- **日期时间类型**：`DateTime`, `DateTimeOffset`
- **布尔类型**：`bool`
- **GUID 类型**：`Guid`
- **可空类型**：`int?`, `DateTime?`, `bool?` 等所有可空值类型

## 九、重要说明

### 9.1 字段路径自动构建

通过 Lambda 表达式自动提取字段路径，无需手动指定字符串，避免拼写错误。

```csharp
// 自动将 x.Order.PaymentStatus 转换为 "Order.PaymentStatus"
query.Where(x => x.Order.PaymentStatus == 2);

// 自动将 x.CreatedDate 转换为 "CreatedDate"
query.Where(x => x.CreatedDate > DateTime.Now);
```

### 9.2 嵌套字段查询

如果字段路径包含多个部分（如 `Order.PaymentStatus`），系统会自动处理嵌套路径。第一个部分（如 `Order`）会被识别为可能的嵌套对象。

```csharp
// 嵌套字段查询
query.Where(x => x.Order.PaymentStatus == 2);
query.Where(x => x.Customer.Address.City == "Beijing");
```

### 9.3 自动索引创建

推送文档时会自动检查索引是否存在，不存在则自动创建。索引的映射配置会根据文档类型的特性自动生成。

### 9.4 性能考虑

- 表达式树解析会有一定的性能开销，但对于大多数场景来说是可以接受的
- 查询构建器会缓存解析结果，提高重复查询的性能
- 批量推送时使用 Bulk API，并支持自动分批处理
- 索引管理器内置缓存机制，减少重复检查

### 9.5 错误处理

- 所有查询方法都支持链式调用，如果某个步骤出错，会在执行 `ToListAsync()` 时抛出异常
- 建议使用 try-catch 包裹查询执行代码

```csharp
try
{
    var result = await query.ToListAsync();
    // 处理结果
}
catch (Exception ex)
{
    // 处理异常
    Console.WriteLine($"查询失败: {ex.Message}");
}
```

## 十、最佳实践

### 10.1 使用 WhereIf 处理可选参数

使用 `WhereIf` 方法可以优雅地处理可选查询参数，避免构建复杂的条件判断逻辑。

```csharp
// 推荐：使用 WhereIf
query
    .WhereIf(!string.IsNullOrEmpty(keyword), x => x.OrderNo.Contains(keyword))
    .WhereIf(startDate.HasValue, x => x.CreatedDate >= startDate.Value)
    .WhereIf(endDate.HasValue, x => x.CreatedDate <= endDate.Value);

// 不推荐：使用 if 语句
if (!string.IsNullOrEmpty(keyword))
{
    query = query.Where(x => x.OrderNo.Contains(keyword));
}
```

### 10.2 合理使用 OR 逻辑

在同一个 `Where` 中使用 `||` 操作符实现 OR 逻辑，多个 `Where` 之间是 AND 关系。

```csharp
// 推荐：在同一个 Where 中使用 || 实现 OR 逻辑
query.Where(x => x.Status == 1 || x.Status == 2);

// 不推荐：使用多个 Where（这样是 AND 关系，不是 OR）
query.Where(x => x.Status == 1)
     .Where(x => x.Status == 2);  // 错误！这样永远查不到结果
```

### 10.3 使用 TrackTotalHits 获取准确总数

对于需要分页的查询，务必调用 `TrackTotalHits()` 方法以获取准确的总记录数。

```csharp
// 推荐：分页查询时使用 TrackTotalHits
var response = await query
    .Skip((pageIndex - 1) * pageSize)
    .Take(pageSize)
    .TrackTotalHits()
    .ToListAsync();
```

### 10.4 合理使用索引名称

使用索引别名或通配符可以简化索引管理。

```csharp
// 使用通配符查询多个索引
var query = _elasticsearchClient.Search<OrderDto>("orders-2024-*");

// 使用索引别名
var query = _elasticsearchClient.Search<OrderDto>("orders-current");
```

### 10.5 批量推送优化

对于大量文档的推送，建议：

- 使用 `PushDocumentsAsync` 进行批量推送
- 根据实际情况调整 `batchSize` 参数（默认 1000）
- 对于超大数据量，考虑分批调用

```csharp
// 推荐：批量推送，自动分批处理
await _elasticsearchClient.PushDocumentsAsync(orders, batchSize: 2000);

// 不推荐：循环单个推送
foreach (var order in orders)
{
    await _elasticsearchClient.PushDocumentAsync(order);  // 性能差
}
```

## 十一、API 参考

### ElasticsearchClientDocumentExtensions

#### PushDocumentAsync<T>

推送单个文档到 Elasticsearch，推送前会自动检查索引是否存在，不存在则自动创建。

**参数：**
- `document` (T): 要推送的文档，必须继承 `BaseEsModel`
- `numberOfShards` (int): 分片数量，默认 3
- `numberOfReplicas` (int): 副本数量，默认 1

**返回：**
- `Task<IndexResponse>`: 推送结果

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

#### PushDocumentsAsync<T>

批量推送文档到 Elasticsearch，推送前会自动检查索引是否存在，不存在则自动创建。

**参数：**
- `documents` (IEnumerable<T>): 要推送的文档列表
- `numberOfShards` (int): 分片数量，默认 3
- `numberOfReplicas` (int): 副本数量，默认 1
- `batchSize` (int): 批量操作的大小，默认 1000

**返回：**
- `Task<BulkResponse>`: 批量推送结果

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

### ElasticsearchClientIndexExtensions

#### CreateIndexForDocumentAsync<T>

根据文档对象创建索引，如果索引已存在则不创建。

**参数：**
- `document` (T): 文档实例
- `numberOfShards` (int): 分片数量，默认 3
- `numberOfReplicas` (int): 副本数量，默认 1

**返回：**
- `Task<string>`: 创建的索引名称

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

#### CreateIndexesForDocumentsAsync<T>

批量创建索引，会自动去重，相同的索引名称只会创建一次。

**参数：**
- `documents` (IEnumerable<T>): 文档实例列表
- `numberOfShards` (int): 分片数量，默认 3
- `numberOfReplicas` (int): 副本数量，默认 1

**返回：**
- `Task<Dictionary<string, List<T>>>`: 索引名称和对应文档列表的字典

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

#### IndexManager()

创建索引管理器实例。

**返回：**
- `ElasticsearchIndexManager`: 索引管理器实例

### ElasticsearchIndexManager

#### CreateIndexIfNotExistsAsync<T>

创建索引，如果存在则不创建。

**参数：**
- `indexName` (string): 索引名称
- `numberOfShards` (int): 分片数量，默认 3
- `numberOfReplicas` (int): 副本数量，默认 1

**返回：**
- `Task<bool>`: 如果索引已存在或创建成功返回 true

**注意：** 字段映射配置只能通过 `EsFieldAttribute` 特性完成，不支持手动配置。

#### IndexExistsAsync

检查索引是否存在。

**参数：**
- `indexName` (string): 索引名称

**返回：**
- `Task<bool>`: 如果索引存在返回 true

#### DeleteIndexAsync

删除索引。

**参数：**
- `indexName` (string): 索引名称

**返回：**
- `Task<bool>`: 删除成功返回 true

### ElasticsearchClientExtensions

#### Search<T>(string index)

创建搜索查询构建器，返回 `EsSearchQueryable<T>` 实例。

**参数：**
- `index` (string): 索引名称，支持通配符（如 `"orders*"`）和多个索引（如 `"orders-2024-01,orders-2024-02"`）

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例

### EsSearchQueryable<T>

查询构建器类，提供链式调用的查询方法。

#### Where(Expression<Func<T, bool>> predicate)

添加 Where 条件，多个 `Where` 方法之间是 AND 关系。

**参数：**
- `predicate` (Expression<Func<T, bool>>): Lambda 表达式，定义查询条件

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### WhereIf(bool condition, Expression<Func<T, bool>> predicate)

根据条件判断是否添加 Where 条件。

**参数：**
- `condition` (bool): 判断条件
- `predicate` (Expression<Func<T, bool>>): Lambda 表达式，定义查询条件

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### OrderBy(Expression<Func<T, object>> field)

按指定字段升序排序。

**参数：**
- `field` (Expression<Func<T, object>>): Lambda 表达式，指定排序字段

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### OrderByDesc(Expression<Func<T, object>> field)

按指定字段降序排序。

**参数：**
- `field` (Expression<Func<T, object>>): Lambda 表达式，指定排序字段

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### Skip(int count)

跳过指定数量的文档，用于分页。

**参数：**
- `count` (int): 跳过的文档数量

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### Take(int count)

获取指定数量的文档，用于分页。

**参数：**
- `count` (int): 获取的文档数量

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### TrackTotalHits()

启用跟踪总命中数。调用此方法后，查询结果会包含总记录数信息。

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

#### ToListAsync()

执行查询并返回结果。

**返回：**
- `Task<SearchResponse<T>>`: 异步返回查询结果

#### ToPageAsync(int pageIndex, int pageSize)

执行分页查询并返回结果。

**参数：**
- `pageIndex` (int): 页码（从1开始）
- `pageSize` (int): 每页数量

**返回：**
- `Task<SearchResponse<T>>`: 异步返回查询结果

## 十二、常见问题

### 1. 如何获取查询的总记录数？

调用 `TrackTotalHits()` 方法后，查询结果会包含总记录数信息。

```csharp
var response = await query
    .TrackTotalHits()
    .ToListAsync();

var total = response.Total;  // 总记录数
```

### 2. 如何实现模糊查询？

使用 `Contains` 方法可以实现模糊查询。

```csharp
query.Where(x => x.OrderNo.Contains("keyword"));
```

### 3. 如何实现范围查询？

使用比较操作符实现范围查询。

```csharp
// 日期范围
query.Where(x => x.CreatedDate >= startDate && x.CreatedDate <= endDate);

// 数值范围
query.Where(x => x.Amount >= minAmount && x.Amount <= maxAmount);
```

### 4. 如何实现多字段排序？

链式调用多个排序方法，先调用的优先级更高。

```csharp
query
    .OrderByDesc(x => x.CreatedDate)  // 先按创建日期降序
    .OrderBy(x => x.Amount);           // 再按金额升序
```

### 5. 如何处理可空类型？

直接使用可空类型的比较操作即可。

```csharp
// 判断可空类型是否有值
query.WhereIf(status.HasValue, x => x.Status == status.Value);

// 判断可空类型是否为 null
query.Where(x => x.OrderNo == null);
```

### 6. 如何查询嵌套对象？

直接使用点号访问嵌套对象的属性即可，系统会自动处理嵌套路径。

```csharp
query.Where(x => x.Order.PaymentStatus == 2);
query.Where(x => x.Customer.Address.City == "Beijing");
```

### 7. 如何自定义索引名称生成逻辑？

实现 `IIndexNameGenerator<T>` 接口，并在特性中指定或运行时注册。

```csharp
[EsIndex(CustomGeneratorType = typeof(CustomOrderIndexNameGenerator))]
public class OrderDto : BaseEsModel { }
```

### 8. 批量推送时如何控制批次大小？

通过 `batchSize` 参数控制，默认 1000。

```csharp
await _elasticsearchClient.PushDocumentsAsync(orders, batchSize: 2000);
```

## 目标框架

- .NET 8.0
- .NET 9.0
- .NET 10.0

## 依赖项

- **Elastic.Clients.Elasticsearch** (>= 8.12.0)

## 许可证

[在此添加许可证信息]

## 贡献

欢迎提交 Issue 和 Pull Request！

## 更新日志

### Version 1.0.0
- 初始版本
- 支持文档推送（单个和批量）
- 支持自动索引创建和管理
- 支持索引名称自动生成（年、年月格式）
- 支持自定义索引名称生成器
- 支持基本的 Where 查询
- 支持逻辑操作符（&&, ||）
- 支持字符串扩展方法（Contains, StartsWith, EndsWith）
- 支持排序和分页
- 支持条件判断（WhereIf）
- 支持自动字段映射

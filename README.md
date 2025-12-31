# ElasticSearch LINQ 查询构建器（ORM 风格）

这是一个类似数据库 ORM 的 ElasticSearch 查询构建器，支持 Lambda 表达式自动构建字段路径，使用 `Where` 方法和 `||`/`&&` 操作符。

## 特性

- ✅ 使用 `Where` 方法代替 `Must`
- ✅ 使用 `||` 操作符代替 `Should`（OR 逻辑）
- ✅ 使用 `&&` 操作符代替多个 `Where`（AND 逻辑）
- ✅ 通过 Lambda 表达式自动构建字段路径，无需手动指定字符串
- ✅ 支持操作符：`>`, `<`, `>=`, `<=`, `==`, `!=`
- ✅ 支持扩展方法：`Contains`, `StartsWith`, `EndsWith`
- ✅ 支持条件判断：`WhereIf`
- ✅ 类型安全，编译时检查

## 基本使用

### 1. 最简单的使用方式（推荐）

```csharp
using Adi.ElasticSugar.Core;

// 直接从 ElasticsearchClient 开始链式调用，类似 SqlSugar
var resp = await _elasticsearchClient.Search($"{MonitorIndexName.SrcOdcB2CShopOrders}*")
    // 条件判断：SalesGroup 不为空时添加等于条件
    .WhereIf(!req.SalesGroup.IsNullOrEmpty(), 
        x => x.Order.SalesGroup == req.SalesGroup)
    // OR 逻辑：PaymentStatus 为 2 时，PaymentStatus=2 或 PaymentGateway="terms"
    .WhereIf(req.PaymentStatus == 2,
        x => x.Order.PaymentStatus == 2 || x.Order.PaymentGateway == "terms")
    // 条件判断：PaymentStatus 有值且不等于 2 时添加等于条件
    .WhereIf(req.PaymentStatus.HasValue && req.PaymentStatus != 2,
        x => x.Order.PaymentStatus == req.PaymentStatus.Value)
    // OR 逻辑：DistributorCode 为 "-1" 时，DistributorCode 为空或空字符串
    .WhereIf(!req.DistributorCode.IsNullOrEmpty() && req.DistributorCode == "-1",
        x => x.Order.DistributorCode == null || x.Order.DistributorCode == "")
    // 范围查询：创建日期大于某个值
    .WhereIf(req.StartDate.HasValue, x => x.CreatedDate >= req.StartDate.Value)
    // 排序
    .OrderByDesc(x => x.CreatedDate)  // 或 .OrderBy(x => x.CreatedDate)
    // 分页
    .Skip((req.PageIndex - 1) * req.PageSize)
    .Take(req.PageSize)
    // 跟踪总命中数
    .TrackTotalHits()
    // 执行查询
    .ToListAsync();
```

### 2. 使用 Where 方法

```csharp
// 等于查询
query.Where(x => x.Order.SalesGroup == req.SalesGroup);

// 大于查询
query.Where(x => x.CreatedDate > req.StartDate);

// 小于等于查询
query.Where(x => x.Order.PaymentStatus <= 2);
```

### 3. 使用操作符组合条件

```csharp
// AND 逻辑：多个 Where 之间是 AND 关系
query
    .Where(x => x.Order.SalesGroup == req.SalesGroup)
    .Where(x => x.CreatedDate > req.StartDate);

// OR 逻辑：使用 || 操作符
query.Where(x => x.Order.PaymentStatus == 2 || x.Order.PaymentGateway == "terms");

// 复杂组合
query.Where(x => 
    (x.Order.SalesGroup == req.SalesGroup || x.Order.SalesGroup == "DEFAULT") 
    && x.CreatedDate > req.StartDate
    && x.Order.PaymentStatus != 0);
```

### 4. 使用扩展方法

```csharp
// Contains - 包含查询
query.Where(x => x.Order.OrderNo.Contains("ORD"));

// StartsWith - 以...开头
query.Where(x => x.Order.OrderNo.StartsWith("ORD"));

// EndsWith - 以...结尾
query.Where(x => x.Order.OrderNo.EndsWith("001"));

// In 查询（通过 Contains 方法）
var statusList = new[] { 1, 2, 3 };
query.Where(x => statusList.Contains(x.Order.PaymentStatus));
```

### 5. 排序和分页

```csharp
query
    .OrderBy(x => x.CreatedDate)        // 升序
    .OrderByDesc(x => x.CreatedDate)    // 降序
    .Skip(10)                            // 跳过 10 条
    .Take(20)                            // 获取 20 条
    .TrackTotalHits();                   // 跟踪总命中数
```

## 对比原有方式

### 原有方式
```csharp
var orderParams = new List<EsQueryParams>()
    .AddIF(!req.SalesGroup.IsNullOrEmpty(), "order", salesOrgCode, EsQueryType.Equals, req.SalesGroup)
    .AddChildIF(req.PaymentStatus is 2, EsAndOr.Or, new List<EsQueryParams>()
        .AddRt("order", paymentStatus, EsQueryType.Equals, req.PaymentStatus)
        .AddRt("order", paymentGateway, EsQueryType.Equals, "terms")
    );

var orderMusts = new List<Action<QueryDescriptor<RespSrcOdcB2CShopOrdersEsDto>>>()
    .CustomAndQuery(orderParams);
```

### 新的 ORM 方式
```csharp
var resp = await _elasticsearchClient.Search($"{MonitorIndexName.SrcOdcB2CShopOrders}*")
    .WhereIf(!req.SalesGroup.IsNullOrEmpty(), 
        x => x.Order.SalesGroup == req.SalesGroup)
    .WhereIf(req.PaymentStatus == 2,
        x => x.Order.PaymentStatus == 2 || x.Order.PaymentGateway == "terms")
    .OrderByDesc(x => x.CreatedDate)
    .Skip((req.PageIndex - 1) * req.PageSize)
    .Take(req.PageSize)
    .ToListAsync();
```

## 支持的表达式

### 比较操作符
- `==` - 等于
- `!=` - 不等于
- `>` - 大于
- `<` - 小于
- `>=` - 大于等于
- `<=` - 小于等于

### 逻辑操作符
- `&&` - AND 逻辑（在同一个 Where 中使用）
- `||` - OR 逻辑（在同一个 Where 中使用）
- 多个 `Where` - AND 逻辑（链式调用）

### 字符串方法
- `Contains(value)` - 包含
- `StartsWith(value)` - 以...开头
- `EndsWith(value)` - 以...结尾

### 集合方法
- `collection.Contains(field)` - In 查询

## 注意事项

1. **字段路径自动构建**：通过 Lambda 表达式自动提取字段路径，例如 `x.Order.PaymentStatus` 会自动转换为 `"Order.PaymentStatus"`

2. **嵌套查询**：如果字段路径包含多个部分（如 `Order.PaymentStatus`），系统会自动判断第一个部分（`Order`）是否为嵌套路径。如果需要更精确的控制，可以通过特性或配置来标记。

3. **类型支持**：
   - 数字类型：`int`, `long`, `double`, `decimal` 等
   - 字符串类型：`string`
   - 日期时间类型：`DateTime`
   - 布尔类型：`bool`
   - GUID 类型：`Guid`

4. **性能考虑**：表达式树解析会有一定的性能开销，但对于大多数场景来说是可以接受的。

## API 参考

### ElasticsearchClientExtensions

- `Search<T>(string index)` - 创建搜索查询构建器（类似 SqlSugar 的 AsQueryable）

### EsSearchQueryable<T>

- `Where(Expression<Func<T, bool>> predicate)` - 添加 Where 条件（AND 逻辑）
- `WhereIf(bool condition, Expression<Func<T, bool>> predicate)` - 条件判断添加 Where 条件
- `OrderBy(Expression<Func<T, object>> field)` - 升序排序
- `OrderByDesc(Expression<Func<T, object>> field)` - 降序排序
- `Skip(int count)` - 跳过指定数量的文档（分页）
- `Take(int count)` - 获取指定数量的文档（分页）
- `TrackTotalHits()` - 启用跟踪总命中数
- `ToListAsync()` - 执行查询并返回结果列表

## 安装

```bash
dotnet add package Adi.ElasticSugar.Core
```

## 目标框架

- .NET 8.0
- .NET 9.0
- .NET 10.0

## 依赖项

- Elastic.Clients.Elasticsearch (>= 8.15.0)


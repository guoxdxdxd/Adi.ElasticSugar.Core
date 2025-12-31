# ElasticSearch LINQ 查询构建器（ORM 风格）

这是一个类似数据库 ORM 的 ElasticSearch 查询构建器，支持 Lambda 表达式自动构建字段路径，使用 `Where` 方法和 `||`/`&&` 操作符。提供类型安全、编译时检查的查询构建体验，让 ElasticSearch 查询像使用 LINQ 一样简单。

## 特性

- ✅ **ORM 风格**：使用 `Where` 方法构建查询条件，类似 Entity Framework 或 SqlSugar
- ✅ **Lambda 表达式**：通过 Lambda 表达式自动构建字段路径，无需手动指定字符串，避免拼写错误
- ✅ **逻辑操作符**：使用 `||` 操作符实现 OR 逻辑，使用 `&&` 操作符实现 AND 逻辑
- ✅ **条件判断**：支持 `WhereIf` 方法，根据条件动态添加查询条件
- ✅ **类型安全**：编译时检查，减少运行时错误
- ✅ **丰富的操作符**：支持 `>`, `<`, `>=`, `<=`, `==`, `!=` 等比较操作符
- ✅ **字符串扩展**：支持 `Contains`, `StartsWith`, `EndsWith` 等字符串方法
- ✅ **集合查询**：支持 `In` 查询（通过 `Contains` 方法）
- ✅ **排序和分页**：支持 `OrderBy`、`OrderByDesc`、`Skip`、`Take` 等方法

## 快速开始

### 1. 安装包

```bash
dotnet add package Adi.ElasticSugar.Core
```

### 2. 基本使用示例

```csharp
using Adi.ElasticSugar.Core;

// 从 ElasticsearchClient 开始链式调用
var result = await _elasticsearchClient.Search<OrderDto>("orders*")
    .Where(x => x.Status == "active")
    .Where(x => x.CreatedDate >= DateTime.Now.AddDays(-7))
    .OrderByDesc(x => x.CreatedDate)
    .Skip(0)
    .Take(20)
    .TrackTotalHits()
    .ToListAsync();
```

## 详细使用说明

### 1. 创建查询构建器

通过 `ElasticsearchClient` 的 `Search<T>` 方法创建查询构建器，其中 `T` 是索引文档的 DTO 类型。

```csharp
// 单个索引
var query = _elasticsearchClient.Search<OrderDto>("orders");

// 多个索引（使用通配符）
var query = _elasticsearchClient.Search<OrderDto>("orders*");

// 多个索引（使用逗号分隔）
var query = _elasticsearchClient.Search<OrderDto>("orders-2024-01,orders-2024-02");
```

### 2. Where 条件查询

#### 2.1 基本比较操作

```csharp
// 等于查询
query.Where(x => x.Order.SalesGroup == "GROUP_A");

// 不等于查询
query.Where(x => x.Order.PaymentStatus != 0);

// 大于查询
query.Where(x => x.CreatedDate > DateTime.Now.AddDays(-30));

// 小于等于查询
query.Where(x => x.Order.Amount <= 1000);

// 大于等于查询
query.Where(x => x.Order.Amount >= 100);

// 小于查询
query.Where(x => x.Order.Amount < 5000);
```

#### 2.2 空值判断

```csharp
// 判断字段为 null
query.Where(x => x.Order.DistributorCode == null);

// 判断字段不为 null
query.Where(x => x.Order.DistributorCode != null);

// 判断字符串为空或空字符串
query.Where(x => x.Order.DistributorCode == null || x.Order.DistributorCode == "");
```

#### 2.3 条件判断（WhereIf）

`WhereIf` 方法允许根据条件动态添加查询条件，非常适合处理可选参数。

```csharp
// 当 SalesGroup 不为空时才添加条件
query.WhereIf(!string.IsNullOrEmpty(req.SalesGroup), 
    x => x.Order.SalesGroup == req.SalesGroup);

// 当 PaymentStatus 有值且不等于 2 时才添加条件
query.WhereIf(req.PaymentStatus.HasValue && req.PaymentStatus != 2,
    x => x.Order.PaymentStatus == req.PaymentStatus.Value);

// 当 StartDate 有值时才添加范围查询
query.WhereIf(req.StartDate.HasValue, 
    x => x.CreatedDate >= req.StartDate.Value);

// 当 EndDate 有值时才添加范围查询
query.WhereIf(req.EndDate.HasValue, 
    x => x.CreatedDate <= req.EndDate.Value);
```

### 3. 逻辑操作符

#### 3.1 AND 逻辑

多个 `Where` 方法链式调用时，它们之间是 AND 关系。

```csharp
// 多个 Where 之间是 AND 关系
query
    .Where(x => x.Order.SalesGroup == "GROUP_A")
    .Where(x => x.CreatedDate > DateTime.Now.AddDays(-7))
    .Where(x => x.Order.PaymentStatus != 0);
// 等价于：SalesGroup == "GROUP_A" AND CreatedDate > ... AND PaymentStatus != 0
```

在同一个 `Where` 中使用 `&&` 操作符也可以实现 AND 逻辑。

```csharp
// 在同一个 Where 中使用 && 实现 AND 逻辑
query.Where(x => 
    x.Order.SalesGroup == "GROUP_A" 
    && x.CreatedDate > DateTime.Now.AddDays(-7)
    && x.Order.PaymentStatus != 0);
```

#### 3.2 OR 逻辑

在同一个 `Where` 中使用 `||` 操作符实现 OR 逻辑。

```csharp
// 使用 || 操作符实现 OR 逻辑
query.Where(x => 
    x.Order.PaymentStatus == 2 || x.Order.PaymentGateway == "terms");
// 等价于：PaymentStatus == 2 OR PaymentGateway == "terms"
```

#### 3.3 复杂逻辑组合

可以组合使用 `&&` 和 `||` 操作符，使用括号控制优先级。

```csharp
// 复杂组合：使用括号控制优先级
query.Where(x => 
    (x.Order.SalesGroup == "GROUP_A" || x.Order.SalesGroup == "DEFAULT") 
    && x.CreatedDate > DateTime.Now.AddDays(-7)
    && x.Order.PaymentStatus != 0);
// 等价于：(SalesGroup == "GROUP_A" OR SalesGroup == "DEFAULT") 
//         AND CreatedDate > ... AND PaymentStatus != 0

// 更复杂的组合
query.Where(x => 
    (x.Order.PaymentStatus == 2 || x.Order.PaymentGateway == "terms")
    && (x.Order.Amount > 100 || x.Order.Amount < 50)
    && x.CreatedDate >= req.StartDate);
```

### 4. 字符串查询

#### 4.1 Contains - 包含查询

```csharp
// 查询订单号包含 "ORD" 的订单
query.Where(x => x.Order.OrderNo.Contains("ORD"));

// 查询描述包含关键词的订单
query.Where(x => x.Order.Description.Contains("urgent"));
```

#### 4.2 StartsWith - 以...开头

```csharp
// 查询订单号以 "ORD" 开头的订单
query.Where(x => x.Order.OrderNo.StartsWith("ORD"));

// 查询客户名称以 "VIP" 开头的订单
query.Where(x => x.Customer.Name.StartsWith("VIP"));
```

#### 4.3 EndsWith - 以...结尾

```csharp
// 查询订单号以 "001" 结尾的订单
query.Where(x => x.Order.OrderNo.EndsWith("001"));
```

### 5. 集合查询（In 查询）

使用集合的 `Contains` 方法实现 `In` 查询。

```csharp
// 查询状态在指定列表中的订单
var statusList = new[] { 1, 2, 3 };
query.Where(x => statusList.Contains(x.Order.PaymentStatus));

// 查询销售组在指定列表中的订单
var salesGroups = new[] { "GROUP_A", "GROUP_B", "GROUP_C" };
query.Where(x => salesGroups.Contains(x.Order.SalesGroup));

// 查询金额在指定范围内的订单
var amounts = new[] { 100, 200, 300, 500 };
query.Where(x => amounts.Contains(x.Order.Amount));
```

### 6. 排序

#### 6.1 升序排序

```csharp
// 按创建日期升序排序
query.OrderBy(x => x.CreatedDate);

// 按金额升序排序
query.OrderBy(x => x.Order.Amount);
```

#### 6.2 降序排序

```csharp
// 按创建日期降序排序
query.OrderByDesc(x => x.CreatedDate);

// 按金额降序排序
query.OrderByDesc(x => x.Order.Amount);
```

#### 6.3 多字段排序

```csharp
// 先按创建日期降序，再按金额升序
query
    .OrderByDesc(x => x.CreatedDate)
    .OrderBy(x => x.Order.Amount);
```

### 7. 分页

#### 7.1 Skip 和 Take

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

#### 7.2 TrackTotalHits

`TrackTotalHits` 方法用于启用跟踪总命中数，这对于分页查询非常重要。

```csharp
query
    .Skip(0)
    .Take(20)
    .TrackTotalHits();  // 启用跟踪总命中数，可以获取总记录数
```

### 8. 执行查询

使用 `ToListAsync()` 方法执行查询并返回结果列表。

```csharp
// 执行查询并返回列表
var result = await query.ToListAsync();

// 完整示例
var result = await _elasticsearchClient.Search<OrderDto>("orders*")
    .Where(x => x.Order.SalesGroup == "GROUP_A")
    .Where(x => x.CreatedDate >= DateTime.Now.AddDays(-7))
    .OrderByDesc(x => x.CreatedDate)
    .Skip(0)
    .Take(20)
    .TrackTotalHits()
    .ToListAsync();
```

### 9. 完整示例

以下是一个完整的实际使用示例，展示了如何组合使用各种功能：

```csharp
using Adi.ElasticSugar.Core;

// 完整的查询示例
var result = await _elasticsearchClient.Search<OrderDto>($"{MonitorIndexName.SrcOdcB2CShopOrders}*")
    // 条件判断：SalesGroup 不为空时添加等于条件
    .WhereIf(!string.IsNullOrEmpty(req.SalesGroup), 
        x => x.Order.SalesGroup == req.SalesGroup)
    
    // OR 逻辑：PaymentStatus 为 2 时，PaymentStatus=2 或 PaymentGateway="terms"
    .WhereIf(req.PaymentStatus == 2,
        x => x.Order.PaymentStatus == 2 || x.Order.PaymentGateway == "terms")
    
    // 条件判断：PaymentStatus 有值且不等于 2 时添加等于条件
    .WhereIf(req.PaymentStatus.HasValue && req.PaymentStatus != 2,
        x => x.Order.PaymentStatus == req.PaymentStatus.Value)
    
    // OR 逻辑：DistributorCode 为 "-1" 时，DistributorCode 为空或空字符串
    .WhereIf(!string.IsNullOrEmpty(req.DistributorCode) && req.DistributorCode == "-1",
        x => x.Order.DistributorCode == null || x.Order.DistributorCode == "")
    
    // 范围查询：创建日期大于等于某个值
    .WhereIf(req.StartDate.HasValue, 
        x => x.CreatedDate >= req.StartDate.Value)
    
    // 范围查询：创建日期小于等于某个值
    .WhereIf(req.EndDate.HasValue, 
        x => x.CreatedDate <= req.EndDate.Value)
    
    // 字符串查询：订单号包含关键词
    .WhereIf(!string.IsNullOrEmpty(req.OrderNoKeyword),
        x => x.Order.OrderNo.Contains(req.OrderNoKeyword))
    
    // In 查询：状态在指定列表中
    .WhereIf(req.StatusList != null && req.StatusList.Any(),
        x => req.StatusList.Contains(x.Order.PaymentStatus))
    
    // 排序：按创建日期降序
    .OrderByDesc(x => x.CreatedDate)
    
    // 分页
    .Skip((req.PageIndex - 1) * req.PageSize)
    .Take(req.PageSize)
    
    // 跟踪总命中数
    .TrackTotalHits()
    
    // 执行查询
    .ToListAsync();
```

## 支持的表达式和操作符

### 比较操作符

| 操作符 | 说明 | 示例 |
|--------|------|------|
| `==` | 等于 | `x.Order.Status == "active"` |
| `!=` | 不等于 | `x.Order.Status != "inactive"` |
| `>` | 大于 | `x.Order.Amount > 100` |
| `<` | 小于 | `x.Order.Amount < 1000` |
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
| `Contains(value)` | 包含指定字符串 | `x.Order.OrderNo.Contains("ORD")` |
| `StartsWith(value)` | 以指定字符串开头 | `x.Order.OrderNo.StartsWith("ORD")` |
| `EndsWith(value)` | 以指定字符串结尾 | `x.Order.OrderNo.EndsWith("001")` |

### 集合方法

| 方法 | 说明 | 示例 |
|------|------|------|
| `collection.Contains(field)` | In 查询，字段值在集合中 | `statusList.Contains(x.Order.PaymentStatus)` |

## 类型支持

支持以下数据类型的查询：

- **数字类型**：`int`, `long`, `double`, `decimal`, `float`, `short`, `byte` 等
- **字符串类型**：`string`
- **日期时间类型**：`DateTime`, `DateTimeOffset`
- **布尔类型**：`bool`
- **GUID 类型**：`Guid`
- **可空类型**：`int?`, `DateTime?`, `bool?` 等所有可空值类型

## 重要说明

### 1. 字段路径自动构建

通过 Lambda 表达式自动提取字段路径，无需手动指定字符串，避免拼写错误。

```csharp
// 自动将 x.Order.PaymentStatus 转换为 "Order.PaymentStatus"
query.Where(x => x.Order.PaymentStatus == 2);

// 自动将 x.CreatedDate 转换为 "CreatedDate"
query.Where(x => x.CreatedDate > DateTime.Now);
```

### 2. 嵌套字段查询

如果字段路径包含多个部分（如 `Order.PaymentStatus`），系统会自动处理嵌套路径。第一个部分（如 `Order`）会被识别为可能的嵌套对象。

```csharp
// 嵌套字段查询
query.Where(x => x.Order.PaymentStatus == 2);
query.Where(x => x.Customer.Address.City == "Beijing");
```

### 3. 性能考虑

- 表达式树解析会有一定的性能开销，但对于大多数场景来说是可以接受的
- 查询构建器会缓存解析结果，提高重复查询的性能
- 对于高频查询场景，建议使用原生 Elasticsearch 客户端以获得最佳性能

### 4. 错误处理

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

## API 参考

### ElasticsearchClientExtensions

#### Search<T>(string index)

创建搜索查询构建器，返回 `EsSearchQueryable<T>` 实例。

**参数：**
- `index` (string): 索引名称，支持通配符（如 `"orders*"`）和多个索引（如 `"orders-2024-01,orders-2024-02"`）

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例

**示例：**
```csharp
var query = _elasticsearchClient.Search<OrderDto>("orders*");
```

### EsSearchQueryable<T>

查询构建器类，提供链式调用的查询方法。

#### Where(Expression<Func<T, bool>> predicate)

添加 Where 条件，多个 `Where` 方法之间是 AND 关系。

**参数：**
- `predicate` (Expression<Func<T, bool>>): Lambda 表达式，定义查询条件

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.Where(x => x.Order.Status == "active");
query.Where(x => x.CreatedDate > DateTime.Now.AddDays(-7));
```

#### WhereIf(bool condition, Expression<Func<T, bool>> predicate)

根据条件判断是否添加 Where 条件。当 `condition` 为 `true` 时，添加条件；为 `false` 时，忽略该条件。

**参数：**
- `condition` (bool): 判断条件
- `predicate` (Expression<Func<T, bool>>): Lambda 表达式，定义查询条件

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.WhereIf(!string.IsNullOrEmpty(keyword), 
    x => x.Order.OrderNo.Contains(keyword));
query.WhereIf(startDate.HasValue, 
    x => x.CreatedDate >= startDate.Value);
```

#### OrderBy(Expression<Func<T, object>> field)

按指定字段升序排序。

**参数：**
- `field` (Expression<Func<T, object>>): Lambda 表达式，指定排序字段

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.OrderBy(x => x.CreatedDate);
query.OrderBy(x => x.Order.Amount);
```

#### OrderByDesc(Expression<Func<T, object>> field)

按指定字段降序排序。

**参数：**
- `field` (Expression<Func<T, object>>): Lambda 表达式，指定排序字段

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.OrderByDesc(x => x.CreatedDate);
query.OrderByDesc(x => x.Order.Amount);
```

#### Skip(int count)

跳过指定数量的文档，用于分页。

**参数：**
- `count` (int): 跳过的文档数量

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.Skip(10);  // 跳过前 10 条
query.Skip((pageIndex - 1) * pageSize);  // 分页计算
```

#### Take(int count)

获取指定数量的文档，用于分页。

**参数：**
- `count` (int): 获取的文档数量

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.Take(20);  // 获取 20 条
query.Take(pageSize);  // 分页大小
```

#### TrackTotalHits()

启用跟踪总命中数。调用此方法后，查询结果会包含总记录数信息，这对于分页查询非常重要。

**返回：**
- `EsSearchQueryable<T>`: 查询构建器实例，支持链式调用

**示例：**
```csharp
query.TrackTotalHits();  // 启用跟踪总命中数
```

**注意：**
- 如果不调用此方法，Elasticsearch 默认只返回前 10,000 条记录的总数
- 对于需要精确总数的分页查询，必须调用此方法

#### ToListAsync()

执行查询并返回结果列表。

**返回：**
- `Task<List<T>>`: 异步返回查询结果列表

**示例：**
```csharp
var result = await query.ToListAsync();
```

**异常：**
- 如果查询构建有误或 Elasticsearch 连接失败，会抛出相应的异常

## 常见问题

### 1. 如何获取查询的总记录数？

调用 `TrackTotalHits()` 方法后，查询结果会包含总记录数信息。具体获取方式取决于返回结果的类型。

```csharp
var result = await query
    .TrackTotalHits()
    .ToListAsync();
// 总记录数信息在 Elasticsearch 响应中
```

### 2. 如何实现模糊查询？

使用 `Contains` 方法可以实现模糊查询。

```csharp
query.Where(x => x.Order.OrderNo.Contains("keyword"));
```

### 3. 如何实现范围查询？

使用比较操作符实现范围查询。

```csharp
// 日期范围
query.Where(x => x.CreatedDate >= startDate && x.CreatedDate <= endDate);

// 数值范围
query.Where(x => x.Order.Amount >= minAmount && x.Order.Amount <= maxAmount);
```

### 4. 如何实现多字段排序？

链式调用多个排序方法，先调用的优先级更高。

```csharp
query
    .OrderByDesc(x => x.CreatedDate)  // 先按创建日期降序
    .OrderBy(x => x.Order.Amount);     // 再按金额升序
```

### 5. 如何处理可空类型？

直接使用可空类型的比较操作即可。

```csharp
// 判断可空类型是否有值
query.WhereIf(status.HasValue, x => x.Order.Status == status.Value);

// 判断可空类型是否为 null
query.Where(x => x.Order.DistributorCode == null);
```

### 6. 如何查询嵌套对象？

直接使用点号访问嵌套对象的属性即可，系统会自动处理嵌套路径。

```csharp
query.Where(x => x.Order.PaymentStatus == 2);
query.Where(x => x.Customer.Address.City == "Beijing");
```

## 最佳实践

### 1. 使用 WhereIf 处理可选参数

使用 `WhereIf` 方法可以优雅地处理可选查询参数，避免构建复杂的条件判断逻辑。

```csharp
// 推荐：使用 WhereIf
query
    .WhereIf(!string.IsNullOrEmpty(keyword), x => x.Order.OrderNo.Contains(keyword))
    .WhereIf(startDate.HasValue, x => x.CreatedDate >= startDate.Value)
    .WhereIf(endDate.HasValue, x => x.CreatedDate <= endDate.Value);

// 不推荐：使用 if 语句
if (!string.IsNullOrEmpty(keyword))
{
    query = query.Where(x => x.Order.OrderNo.Contains(keyword));
}
if (startDate.HasValue)
{
    query = query.Where(x => x.CreatedDate >= startDate.Value);
}
```

### 2. 合理使用 OR 逻辑

在同一个 `Where` 中使用 `||` 操作符实现 OR 逻辑，多个 `Where` 之间是 AND 关系。

```csharp
// 推荐：在同一个 Where 中使用 || 实现 OR 逻辑
query.Where(x => x.Order.Status == "active" || x.Order.Status == "pending");

// 不推荐：使用多个 Where（这样是 AND 关系，不是 OR）
query.Where(x => x.Order.Status == "active")
     .Where(x => x.Order.Status == "pending");  // 错误！这样永远查不到结果
```

### 3. 使用 TrackTotalHits 获取准确总数

对于需要分页的查询，务必调用 `TrackTotalHits()` 方法以获取准确的总记录数。

```csharp
// 推荐：分页查询时使用 TrackTotalHits
var result = await query
    .Skip((pageIndex - 1) * pageSize)
    .Take(pageSize)
    .TrackTotalHits()
    .ToListAsync();
```

### 4. 合理使用索引名称

使用索引别名或通配符可以简化索引管理。

```csharp
// 使用通配符查询多个索引
var query = _elasticsearchClient.Search<OrderDto>("orders-2024-*");

// 使用索引别名
var query = _elasticsearchClient.Search<OrderDto>("orders-current");
```

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

## 目标框架

- .NET 8.0
- .NET 9.0
- .NET 10.0

## 依赖项

- **Elastic.Clients.Elasticsearch** (>= 8.15.0)

## 许可证

[在此添加许可证信息]

## 贡献

欢迎提交 Issue 和 Pull Request！

## 更新日志

### Version 1.0.0
- 初始版本
- 支持基本的 Where 查询
- 支持逻辑操作符（&&, ||）
- 支持字符串扩展方法（Contains, StartsWith, EndsWith）
- 支持排序和分页
- 支持条件判断（WhereIf）


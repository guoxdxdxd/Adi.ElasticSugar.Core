# Adi.ElasticSugar.Core 单元测试

本项目包含 `Adi.ElasticSugar.Core` 的完整单元测试，涵盖了索引创建、文档推送和查询等所有功能。

## 测试结构

### 1. 测试模型 (`Models/TestDocument.cs`)
包含各种 Elasticsearch 支持的数据类型：
- **字符串类型**：text、keyword、可空字符串
- **整数类型**：int、long、short、byte 及其可空版本
- **浮点数类型**：double、float、decimal 及其可空版本
- **日期时间类型**：DateTime、DateTimeOffset 及其可空版本
- **布尔类型**：bool 及其可空版本
- **Guid 类型**：Guid 及其可空版本
- **集合类型**：List<string>、List<int>
- **嵌套文档类型**：NestedAddress、List<NestedItem>

### 2. 索引测试 (`IndexTests/IndexCreationTests.cs`)
- 测试根据文档创建索引
- 测试重复创建索引（幂等性）
- 测试批量创建索引
- 测试索引存在性检查
- 测试索引删除
- 测试索引映射的正确性

### 3. 文档推送测试 (`DocumentTests/DocumentPushTests.cs`)
- 测试推送单个文档
- 测试推送包含所有数据类型的完整文档
- 测试批量推送文档
- 测试大批量文档的分批处理
- 测试跨多个索引的批量推送
- 测试推送时自动创建索引

### 4. 查询测试 (`SearchTests/`)

#### 4.1 字符串查询测试 (`StringQueryTests.cs`)
- text 字段的精确匹配（使用 .keyword 子字段）
- keyword 字段的精确匹配
- text 字段的 Contains 查询（模糊匹配）
- text 字段的 StartsWith 查询
- text 字段的 EndsWith 查询
- 字符串字段的不等于查询
- 字符串字段的 In 查询
- 可空字符串字段的查询

#### 4.2 数值查询测试 (`NumericQueryTests.cs`)
- int 字段的等于、大于、小于、大于等于、小于等于、不等于查询
- long 字段的等于和范围查询
- double 字段的等于和范围查询
- decimal 字段的等于查询
- 可空数值字段的查询
- 数值字段的 In 查询

#### 4.3 日期时间查询测试 (`DateTimeQueryTests.cs`)
- DateTime 字段的等于、大于、小于、大于等于、小于等于查询
- DateTime 字段的范围查询
- DateTimeOffset 字段的等于和范围查询
- 可空 DateTime 字段的查询

#### 4.4 布尔查询测试 (`BooleanQueryTests.cs`)
- bool 字段的等于 true/false 查询
- bool 字段的简写查询
- bool 字段的不等于查询
- 可空 bool 字段的查询

#### 4.5 Guid 查询测试 (`GuidQueryTests.cs`)
- Guid 字段的等于查询
- Guid 字段的不等于查询
- Guid 字段的 In 查询
- 可空 Guid 字段的查询

#### 4.6 组合查询测试 (`CombinedQueryTests.cs`)
- 多个 Where 条件的 AND 组合（链式调用）
- 单个 Where 条件中的 AND 组合（使用 && 运算符）
- 单个 Where 条件中的 OR 组合（使用 || 运算符）
- 复杂组合查询（AND 和 OR 混合）
- 字符串和数值类型的组合查询
- 日期和数值类型的组合查询
- 多个字段的 In 查询组合
- 排序和分页的组合
- TrackTotalHits 功能测试
- 分页功能测试

#### 4.7 嵌套文档查询测试 (`NestedDocumentQueryTests.cs`)
- 嵌套文档字段的查询
- 嵌套文档的完整检索
- 嵌套文档集合的查询
- 包含嵌套文档的完整文档推送和检索

## 运行测试

### 前置条件

1. **配置 Elasticsearch 连接**
   
   测试项目支持从配置文件或环境变量读取 Elasticsearch 连接配置。

   **方式一：使用配置文件（推荐）**
   
   1. 复制示例配置文件：
      ```bash
      cp appsettings.json.example appsettings.json
      ```
   
   2. 编辑 `appsettings.json` 文件，填入真实的连接信息：
      ```json
      {
        "Elasticsearch": {
          "Name": "MonitorData",
          "Uris": [
            "https://your-elasticsearch-host:9200"
          ],
          "UserName": "your_username",
          "Password": "your_password"
        }
      }
      ```
   
   3. 或者创建 `appsettings.Development.json` 文件（已添加到 .gitignore）：
      ```json
      {
        "Elasticsearch": {
          "Uris": ["https://172.17.12.19:9200"],
          "UserName": "elastic",
          "Password": "your_password"
        }
      }
      ```

   **方式二：使用环境变量**
   
   设置以下环境变量（优先级高于配置文件）：
   ```bash
   export ELASTICSEARCH__URIS__0="https://172.17.12.19:9200"
   export ELASTICSEARCH__USERNAME="elastic"
   export ELASTICSEARCH__PASSWORD="your_password"
   ```

   **方式三：使用本地 Docker（开发测试）**
   
   如果使用本地 Docker 运行 Elasticsearch：
   ```bash
   docker run -d \
     --name elasticsearch \
     -p 9200:9200 \
     -p 9300:9300 \
     -e "discovery.type=single-node" \
     -e "xpack.security.enabled=false" \
     elasticsearch:8.12.0
   ```
   
   然后创建 `appsettings.json`（从示例文件复制）并配置为：
   ```json
   {
     "Elasticsearch": {
       "Uris": ["http://localhost:9200"]
     }
   }
   ```
   
   **注意**：`appsettings.json` 已添加到 .gitignore，不会被提交到仓库。

2. **验证 Elasticsearch 连接**
   
   访问配置的 Elasticsearch 地址应该返回集群信息。
   
   对于 HTTPS 连接，测试环境会自动跳过证书验证（仅用于测试）。

### 运行所有测试

```bash
dotnet test
```

### 运行特定测试类

```bash
# 运行索引测试
dotnet test --filter "FullyQualifiedName~IndexCreationTests"

# 运行文档推送测试
dotnet test --filter "FullyQualifiedName~DocumentPushTests"

# 运行字符串查询测试
dotnet test --filter "FullyQualifiedName~StringQueryTests"
```

### 运行特定测试方法

```bash
# 运行特定测试方法
dotnet test --filter "FullyQualifiedName~StringQueryTests.Where_TextField_Equals_ShouldReturnExactMatch"
```

### 查看详细输出

```bash
dotnet test --logger "console;verbosity=detailed"
```

## 测试数据说明

- 所有测试都使用 `TestDocument` 作为测试模型
- 测试索引使用 `test-documents-{yyyy-MM}` 格式（例如：`test-documents-2024-01`）
- 测试完成后会自动清理测试索引（在 `TestBase.DisposeAsync` 中）

## 注意事项

1. **测试环境隔离**：每个测试类都会创建自己的测试数据，测试之间相互独立
2. **索引清理**：测试基类会在测试完成后自动清理测试索引
3. **Elasticsearch 版本**：测试基于 Elasticsearch 8.12.0 版本
4. **连接配置**：
   - 默认从 `appsettings.json` 读取配置
   - 支持 `appsettings.Development.json` 覆盖配置（已添加到 .gitignore）
   - 支持环境变量覆盖（优先级最高）
   - HTTPS 连接在测试环境会自动跳过证书验证（仅用于测试，生产环境应使用有效证书）
5. **敏感信息保护**：
   - `appsettings.json`、`appsettings.Development.json` 和 `appsettings.Production.json` 已添加到 .gitignore
   - 仓库中只保留 `appsettings.json.example` 作为模板
   - 首次使用需要从 `appsettings.json.example` 复制创建 `appsettings.json`
   - 建议使用环境变量存储生产环境的密码

## 测试覆盖率

测试覆盖了以下功能：
- ✅ 索引创建和管理
- ✅ 单个文档推送
- ✅ 批量文档推送
- ✅ 所有数据类型的查询（单独测试）
- ✅ 组合查询（AND、OR）
- ✅ 排序和分页
- ✅ 嵌套文档的存储和检索

## 故障排除

### 问题：无法连接到 Elasticsearch

**解决方案**：
1. 确保 Elasticsearch 正在运行
2. 检查连接地址是否正确（默认：`http://localhost:9200`）
3. 检查防火墙设置

### 问题：测试失败，提示索引已存在

**解决方案**：
1. 手动删除测试索引：`curl -X DELETE "http://localhost:9200/test*"`
2. 或者修改 `TestBase.TestIndexPrefix` 使用不同的前缀

### 问题：测试超时

**解决方案**：
1. 检查 Elasticsearch 性能
2. 增加测试超时时间（在测试方法上添加 `[Fact(Timeout = 60000)]`）


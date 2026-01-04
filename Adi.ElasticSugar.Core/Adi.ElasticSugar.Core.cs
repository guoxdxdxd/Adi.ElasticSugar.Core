// 为了保持向后兼容性，重新导出所有公共类型
// 这样现有代码不需要修改 using 语句

using Adi.ElasticSugar.Core.Models;
using Adi.ElasticSugar.Core.Search;
using Adi.ElasticSugar.Core.Index;

// 重新导出搜索相关类型
using EsSearchQueryable = Adi.ElasticSugar.Core.Search.EsSearchQueryable<object>;
using EsQueryable = Adi.ElasticSugar.Core.Search.EsQueryable<object>;

namespace Adi.ElasticSugar.Core;

// 这个文件仅用于文档说明，实际类型在各自的命名空间中


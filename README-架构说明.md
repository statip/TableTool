# TableTool 项目架构

## 概览

```
┌──────────────────────────────────────────────────┐
│              构建工具 TableTool.Cli                │
│                                                  │
│  schema.yaml ──→ SchemaLoader                    │
│  Item.xlsx   ──→ ExcelReader   ──→ DataModel     │
│                        │                         │
│              ┌─────────┴─────────┐                │
│              ▼                   ▼                │
│        Validation           No errors?            │
│        ├ TypeValidator        → JsonExporter      │
│        ├ PrimaryKeyValidator  → CSharpClassGen    │
│        └ ForeignKeyValidator    TablesGenerator   │
│              │                                    │
│              ▼ errors?                            │
│         validation.log (不输出任何文件)              │
└──────────────────────────────────────────────────┘
                                                   
┌──────────────────────────────────────────────────┐
│             运行时库 TableTool.Runtime              │
│                                                  │
│  JSON files ──→ DataLoader ──→ DataTable<T>       │
│               (反序列化)      (O(1) 字典查询)      │
│                                                  │
│  用法: Tables.Item.Get(1001)                     │
└──────────────────────────────────────────────────┘
```

## 文件结构

```
src/
├── TableTool.Cli/                     # 构建工具
│   ├── Program.cs                     # 入口 + CLI参数解析
│   │
│   ├── Schema/                        # YAML 配置加载
│   │   ├── SchemaLoader.cs            #   YAML → SchemaDocument
│   │   └── Models/
│   │       ├── TableDefinition.cs     #   表结构 (name, file, PK, fields)
│   │       ├── FieldDefinition.cs     #   字段结构 (name, type, ref, struct)
│   │       ├── FieldType.cs           #   类型系统 (int/list/map/struct/enum/custom)
│   │       ├── EnumDefinition.cs      #   枚举定义
│   │       └── CustomTypeDefinition.cs #  自定义类型定义
│   │
│   ├── Excel/                         # Excel 解析
│   │   ├── ExcelReader.cs             #   读取 xlsx, 解析表头 (PK/FK/类型行)
│   │   └── CellConverter.cs           #   单元格值 → 强类型
│   │
│   ├── Model/                         # 内存数据模型
│   │   ├── DataModel.cs               #   所有表的集合
│   │   ├── DataTable.cs               #   单张表 + 主键构造
│   │   └── DataCell.cs                #   单元格
│   │
│   ├── Validation/                    # 校验管线(原子提交)
│   │   ├── SchemaValidator.cs         #   总调度
│   │   ├── TypeValidator.cs           #   类型检查
│   │   ├── PrimaryKeyValidator.cs     #   主键唯一性
│   │   └── ForeignKeyValidator.cs     #   外键引用完整性
│   │
│   ├── Export/                        # JSON 导出
│   │   └── JsonExporter.cs            #   DataModel → JSON 文件
│   │
│   ├── CodeGen/                       # C# 代码生成
│   │   ├── CSharpClassGenerator.cs    #   Record类 + Table类 + 复合Key
│   │   └── TablesGenerator.cs         #   Tables.cs + JsonConverter
│   │
│   ├── Commands/                      # 命令
│   │   └── BuildCommand.cs            #   build 管线调度
│   │
│   └── SampleBuilder.cs               # 样本 Excel 生成器
│
└── TableTool.Runtime/                 # 运行时库 (供项目引用)
    ├── IDataTable.cs                  #   接口: Get/TryGet/ContainsKey/GetAll
    ├── DataTable.cs                   #   基类: Dictionary<TKey,TRecord>
    └── DataLoader.cs                  #   JSON → Table 反序列化

schema/tables.yaml                     # 表定义
excel/*.xlsx                           # Excel 数据
output/
├── data/*.json                        # 构建输出: JSON
└── gen/*.cs                           # 构建输出: C# 代码
```

## 核心设计

### 两部分分离

| 项目 | 角色 | 依赖 | 使用场景 |
|------|------|------|---------|
| `TableTool.Cli` | 构建工具 | ClosedXML + YamlDotNet | 只在导表时用 |
| `TableTool.Runtime` | 运行时库 | System.Text.Json(内置) | 随项目发布 |

### 数据流

1. **加载** — YAML schema + Excel → `DataModel` (内存)
2. **校验** — 类型/PK/外键，**全部通过才输出，有错全停**
3. **导出** — `DataModel` → JSON 文件
4. **生成** — `DataModel` → C# 代码 (Record类 + Table类 + Tables.cs)

### Excel 解析规则

```
Row 1: #Id  Name  Category#ref=ItemCategory.Id  Tags  ##备注
Row 2: int  string  int  list<string>  ##注释行
Row 3: ⋮ 数据行
```

- `#` 前缀 = 主键
- `#ref=Table.Field` = 外键标注
- `##` 前缀 = 注释列，跳过

### 类型系统

```
FieldTypeKind
├── Bool / Int / Long / Float / String  ← 基本类型
├── List(ElementType)                    ← list<T>
├── Map(KeyType, ValueType)              ← map<K,V>
├── Struct(Fields)                       ← 内联结构体
├── Enum(name)                           ← 枚举
└── Custom(name, definition)             ← 自定义类型 (storage + csharp + parse)
```

- 基本类型直接映射
- `list<>` / `map<>` 递归解析内部类型
- 枚举在 YAML `enums` 里定义，Excel 写枚举名
- 自定义类型在 YAML `custom_types` 里定义，底层委托给 storage 类型解析，C# 通过 `JsonConverter` 转换

### 运行时查询

```csharp
// IDataTable<TKey, TRecord> 接口
public interface IDataTable<TKey, TRecord> {
    TRecord Get(TKey key);                  // O(1) 字典查询
    TRecord? TryGet(TKey key);              // 不存在返回 null
    bool ContainsKey(TKey key);
    IReadOnlyCollection<TRecord> GetAll();
    int Count { get; }
}
```

- 单主键: `TKey = int/string/...` 
- 复合主键: 自动生成 `XxxKey` struct (带 `Equals`/`GetHashCode`)
- 无主键: `TKey = string` (行号)

### 校验原子性

所有表全部校验通过才写输出文件，否则只写 `validation.log`：

```
Validation Pipeline:
├── TypeValidator       每个单元格类型匹配
├── PrimaryKeyValidator PK 唯一性 + 非空
└── ForeignKeyValidator 每个 FK 值在目标表存在
        │
        ▼ 有错误
  exit(1), 不输出任何文件
```

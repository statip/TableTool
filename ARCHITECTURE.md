# TableTool Architecture Plan — Excel-to-Config Data Tool

## 目次
1. [Overview & Design Goals](#1-overview--design-goals)
2. [Architecture Overview](#2-architecture-overview)
3. [Schema Definition Format](#3-schema-definition-format)
4. [Excel Data Format Convention](#4-excel-data-format-convention)
5. [Excel Parsing Approach](#5-excel-parsing-approach)
6. [JSON Export Format](#6-json-export-format)
7. [C# Code Generation](#7-c-code-generation)
8. [Foreign Key Validation](#8-foreign-key-validation)
9. [Data Access Layer (Runtime)](#9-data-access-layer-runtime)
10. [File Structure](#10-file-structure)
11. [Data Flow Pipeline](#11-data-flow-pipeline)
12. [Configuration](#12-configuration)
13. [Trade-offs & Decisions](#13-trade-offs--decisions)
14. [Implementation Roadmap](#14-implementation-roadmap)

---

## 1. Overview & Design Goals

### What this tool does
- Reads `.xlsx` spreadsheet files and a **schema definition** (YAML)
- Validates data types, primary keys, and foreign key references
- Exports clean **JSON** data files for runtime consumption
- Generates **C#** strongly-typed class definitions + a data access layer

### Design goals
| Goal | Priority |
|------|----------|
| **Deterministic output**: same Excel → same JSON & C# every time | 🟢 High |
| **Early error detection**: FK/type/duplicate errors caught at export time, not at runtime | 🟢 High |
| **Minimal runtime cost**: data access uses `Dictionary<TKey, TRecord>` — O(1) lookup, no LINQ overhead | 🟢 High |
| **Clean developer experience**: schema is YAML, Excel uses header rows for type annotations | 🟡 Medium |
| **Extensible type system**: supports `int`, `float`, `string`, `bool`, `list<T>`, `map<K,V>`, `enum`, `ref` (foreign key) | 🟢 High |
| **CI-friendly**: CLI tool, no GUI dependency, runs anywhere with Node.js | 🟡 Medium |

---

## 2. Architecture Overview

The system is split into **two distinct parts**:

```
┌──────────────────────────────────────────────────┐
│                   Build-Time Tool                 │
│              (Node.js CLI — excelsior)             │
│                                                    │
│  schema.yaml  ──► Schema Loader  ──► Validator    │
│  Item.xlsx    ──► Excel Parser   ──►   │          │
│  Char.xlsx    ──►                  ──►   ▼        │
│                                    DataModel      │
│                                       │           │
│                          ┌────────────┴───────┐   │
│                          ▼                    ▼   │
│                    JsonExporter         CodeGen   │
│                          │                    │   │
│                    output/data/        output/gen/ │
└──────────────────────────────────────────────────┘
                                                   
┌──────────────────────────────────────────────────┐
│                  Runtime Library (C#)              │
│                                                    │
│  JSON files ──► DataLoader ──► DataTable<T>       │
│              (deserialize)    (typed access)       │
│                                                    │
│  Usage:                                           │
│    var item = Tables.Get<Item>(1001);              │
│    var items = Tables.GetAll<Item>();              │
└──────────────────────────────────────────────────┘
```

### Why split build-time (Node.js) and runtime (C#)?

| Aspect | Build Tool (Node.js) | Runtime Library (C#) |
|--------|---------------------|---------------------|
| Language | TypeScript / JavaScript | C# (.NET Standard 2.1+) |
| Role | Excel → JSON + C# code gen | JSON deserialize + typed access |
| Dependencies | `xlsx` (SheetJS), `js-yaml`, `commander` | `System.Text.Json` (built-in) |
| Where it runs | Developer machine, CI pipeline | Game/application runtime |

**Trade-off note**: If the team prefers a single-language toolchain, the build tool could also be written in C# (using ClosedXML/EPPlus for Excel parsing). The Node.js choice follows Luban's separation pattern and matches the user's `xlsx` npm package preference. Both options are shown in [§13](#13-trade-offs--decisions).

---

## 3. Schema Definition Format

The schema is a **YAML** file that describes every table (sheet) to process.

### Schema file: `schema/tables.yaml`

```yaml
# ============================================================
# schema/tables.yaml — Table definitions
# ============================================================

tables:

  - name: Item
    description: "Item master data"
    file: Item.xlsx                  # Input Excel file name
    sheet: Sheet1                    # Optional; defaults to first sheet
    primaryKey: Id                   # Single-column PK
    fields:
      - name: Id
        type: int
        comment: "Item unique ID"
      - name: Name
        type: string
        comment: "Display name"
      - name: Price
        type: int
        comment: "Buy price"
      - name: Category
        type: int
        comment: "Item category ID"
        ref: ItemCategory.Id         # Foreign key → ItemCategory.Id
      - name: Tags
        type: list<string>           # Array type
        comment: "Search tags"
      - name: Attributes
        type: map<string,int>        # Dictionary type
        comment: "Attribute bonuses"

  - name: ItemCategory
    description: "Item category"
    file: ItemCategory.xlsx
    primaryKey: Id
    fields:
      - name: Id
        type: int
      - name: Name
        type: string

  - name: Skill
    description: "Skill definitions"
    file: Skill.xlsx
    primaryKey: [Id, Level]          # Composite PK (multi-column)
    fields:
      - name: Id
        type: int
      - name: Level
        type: int
      - name: Name
        type: string
      - name: Description
        type: string
      - name: Cost
        type: float

  - name: Reward
    description: "Reward table"
    file: Reward.xlsx
    primaryKey: Id
    fields:
      - name: Id
        type: int
      - name: Items
        type: list<RewardItem>       # Sub-object array
        struct:
          - name: ItemId
            type: int
            ref: Item.Id
          - name: Count
            type: int
          - name: Rate
            type: float
```

### Supported field types

| Schema Type | C# Type | JSON Type | Notes |
|-------------|---------|-----------|-------|
| `bool` | `bool` | `true`/`false` | |
| `int` | `int` | number | |
| `long` | `long` | number | |
| `float` | `float` | number | |
| `string` | `string` | string | |
| `list<T>` | `List<T>` | array | e.g. `list<int>`, `list<string>` |
| `map<K,V>` | `Dictionary<K,V>` | object | e.g. `map<string,int>` |
| `enum` | enum type | string/int | Defined inline or by `$enum` block |
| **(inline struct)** | nested class | nested object | See `RewardItem` example above |
| `ref: TableName.Field` | — (validation only) | same as type | FK validation marker |

### Enum definition block (optional)

```yaml
enums:
  - name: ElementType
    values:
      None: 0
      Fire: 1
      Water: 2
      Wind: 3
      Earth: 4
```

---

## 4. Excel Data Format Convention

Each Excel file follows a **3-header-row convention** (Luban-style):

```
|  A   |   B    |    C     |    D     |    E       |
|------|--------|----------|----------|------------|
| Id   | Name   | Price    | Category | Tags       |  ← Row 1: Field names
| int  | string | int      | int      | list<string>| ← Row 2: Types
| 唯一ID | 名称   | 价格(金) | 类别     | 标签       |  ← Row 3: Comments (optional)
|------|--------|----------|----------|------------|
| 1001 | 铁剑   | 500      | 1        | [武器,新手] |  ← Row 4+: Data
| 1002 | 铜盾   | 300      | 2        | [防具]      |
```

### Special column markers

- **`##` prefix**: Field name starts with `##` → **skip column** (ignore column entirely)
- **`#` prefix**: Field name starts with `#` → **field is primary key** (if not explicitly listed in schema `primaryKey`)
- **Empty column header**: Column is ignored

### Composite primary key example

```
|  Id   |  Level  |  Name   |  Cost   |
|  int  |  int    |  string |  float  |
|-------|---------|---------|---------|
|  101  |  1      | Fire    | 10.5    |
|  101  |  2      | Fire II | 20.0    |
|  101  |  3      | Fire III| 35.0    |
```

Schema: `primaryKey: [Id, Level]` → JSON key becomes composite string `"101|1"`, `"101|2"`, etc.

---

## 5. Excel Parsing Approach

### Library: `xlsx` (SheetJS) npm package

```json
{
  "dependencies": {
    "xlsx": "^0.18.5",
    "js-yaml": "^4.1.0",
    "commander": "^11.0.0",
    "chalk": "^5.3.0"
  },
  "devDependencies": {
    "@types/node": "^20.0.0",
    "typescript": "^5.3.0"
  }
}
```

### ExcelReader — Core parsing class

```
ExcelReader
├── readWorkbook(filePath)
│   └── workbook = XLSX.readFile(filePath)
├── readSheet(workbook, sheetName?)
│   ├── Detect range (non-empty)
│   ├── Row 1 → field names
│   ├── Row 2 → field types
│   ├── Row 3 → comments (optional, store for documentation)
│   ├── Rows 4+ → data records
│   └── Convert cell values based on type annotation
├── convertCell(rawValue, fieldType)
│   ├── "int"        → parseInt
│   ├── "float"      → parseFloat
│   ├── "string"     → String(rawValue)
│   ├── "bool"       → parseBool(rawValue)
│   ├── "list<T>"    → parseArray(rawValue, T)
│   └── "map<K,V>"   → parseMap(rawValue, K, V)
└── Result → ParsedSheet { name, fields[], rows[] }
```

### Type parsing helpers

The `xlsx` package returns cells as raw values (strings, numbers, booleans). We parse them:

```typescript
// examples
"list<int>"    → "[1, 2, 3]"                 // Excel string or JSON array
"map<string,int>" → "atk:10, def:5"          // key:value,key:value format
"[武器,新手]"    → parsed as ["武器", "新手"]  // Chinese bracket syntax
```

### Cell format conventions

| Type | Excel Cell Format | Parsed |
|------|------------------|--------|
| `int` | `1001` or `"1001"` | `1001` |
| `float` | `3.14` or `"3.14"` | `3.14` |
| `list<int>` | `[1, 2, 3]` | `[1, 2, 3]` |
| `list<string>` | `[武器, 新手]` or `["武器","新手"]` | `["武器","新手"]` |
| `map<string,int>` | `{atk:10, def:5}` | `{"atk":10,"def":5}` |
| `bool` | `true` / `false` or `1` / `0` | `true` / `false` |

---

## 6. JSON Export Format

### Per-table JSON files

One `.json` file per table, written to `{outputDir}/data/`.

### Format: Record map keyed by primary key

**Single PK** (`Item.json`):

```json
{
  "keyColumn": "Id",
  "primaryKeyType": "int",
  "records": {
    "1001": {
      "Id": 1001,
      "Name": "铁剑",
      "Price": 500,
      "Category": 1,
      "Tags": ["武器", "新手"]
    },
    "1002": {
      "Id": 1002,
      "Name": "铜盾",
      "Price": 300,
      "Category": 2,
      "Tags": ["防具"]
    }
  }
}
```

**Composite PK** (`Skill.json`):

```json
{
  "keyColumn": ["Id", "Level"],
  "primaryKeyType": "int|int",
  "records": {
    "101|1": { "Id": 101, "Level": 1, "Name": "Fire", "Cost": 10.5 },
    "101|2": { "Id": 101, "Level": 2, "Name": "Fire II", "Cost": 20.0 },
    "101|3": { "Id": 101, "Level": 3, "Name": "Fire III", "Cost": 35.0 }
  }
}
```

### Alternative: Array format (if no PK defined)

If a table has **no primary key** (or `primaryKey: null`), export as a flat array:

```json
{
  "isList": true,
  "records": [
    { "Id": 1001, "Name": "铁剑", ... },
    { "Id": 1002, "Name": "铜盾", ... }
  ]
}
```

### Nested structs (e.g., `Reward.json`)

```json
{
  "keyColumn": "Id",
  "records": {
    "R001": {
      "Id": "R001",
      "Items": [
        { "ItemId": 1001, "Count": 2, "Rate": 1.0 },
        { "ItemId": 1002, "Count": 1, "Rate": 0.5 }
      ]
    }
  }
}
```

### Output directory structure

```
outputDir/                     ← Configurable (default: ./output)
├── data/                      ← JSON data files
│   ├── Item.json
│   ├── Skill.json
│   ├── ItemCategory.json
│   └── Reward.json
├── gen/                       ← Generated C# source files
│   ├── Item.cs
│   ├── Skill.cs
│   ├── ItemCategory.cs
│   ├── Reward.cs
│   └── Tables.cs              ← Data access singleton
└── validation.log             ← Validation errors/warnings
```

---

## 7. C# Code Generation

The code generator produces **one `.cs` file per table** plus a **`Tables.cs`** access root. It uses vanilla string templating (no Roslyn source generators — simpler, more transparent).

### Generated class per table (`gen/Item.cs`)

```csharp
// ============================================================
// Auto-generated by TableTool. DO NOT EDIT.
// Source: Item.xlsx
// ============================================================

namespace GameConfig
{
    /// <summary>Item master data</summary>
    public sealed class ItemRecord
    {
        [JsonInclude]
        public int Id { get; private set; }

        [JsonInclude]
        public string Name { get; private set; } = string.Empty;

        [JsonInclude]
        public int Price { get; private set; }

        [JsonInclude]
        public int Category { get; private set; }

        [JsonInclude]
        public List<string> Tags { get; private set; } = new();
    }

    /// <summary>Typed data table for Item</summary>
    public sealed class ItemTable : DataTable<int, ItemRecord>
    {
        public ItemTable(DataTableJson<int, ItemRecord> json) : base(json) { }
    }
}
```

### Generated class with composite PK (`gen/Skill.cs`)

```csharp
public sealed class SkillRecord
{
    [JsonInclude] public int Id { get; private set; }
    [JsonInclude] public int Level { get; private set; }
    [JsonInclude] public string Name { get; private set; } = string.Empty;
    [JsonInclude] public float Cost { get; private set; }

    // Composite key helper
    public static string MakeKey(int id, int level) => $"{id}|{level}";
}
```

### Generated with inline struct (`gen/Reward.cs`)

```csharp
public sealed class RewardRecord
{
    [JsonInclude] public string Id { get; private set; } = string.Empty;

    [JsonInclude]
    public List<RewardItem> Items { get; private set; } = new();

    public sealed class RewardItem
    {
        [JsonInclude] public int ItemId { get; private set; }
        [JsonInclude] public int Count { get; private set; }
        [JsonInclude] public float Rate { get; private set; }
    }
}
```

### Generated enum

```csharp
public enum ElementType
{
    None = 0,
    Fire = 1,
    Water = 2,
    Wind = 3,
    Earth = 4,
}
```

### Generated data access root (`gen/Tables.cs`)

```csharp
// ============================================================
// Auto-generated by TableTool. DO NOT EDIT.
// ============================================================
namespace GameConfig
{
    public static class Tables
    {
        private static bool _loaded;

        // ---- Lazy-loaded table instances ----
        private static Lazy<ItemTable> _item = new(() => LoadTable<ItemTable, int, ItemRecord>("Item"));
        private static Lazy<SkillTable> _skill = new(() => LoadTable<SkillTable, string, SkillRecord>("Skill"));
        private static Lazy<RewardTable> _reward = new(() => LoadTable<RewardTable, string, RewardRecord>("Reward"));

        // ---- Public accessors ----
        public static ItemTable Item => _item.Value;
        public static SkillTable Skill => _skill.Value;
        public static RewardTable Reward => _reward.Value;

        // ---- Generic Load ----
        private static TTable LoadTable<TTable, TKey, TRecord>(string name)
            where TTable : DataTable<TKey, TRecord>
            where TRecord : class
        {
            var jsonPath = Path.Combine(DataPath, $"{name}.json");
            var json = File.ReadAllText(jsonPath);
            var data = JsonSerializer.Deserialize<DataTableJson<TKey, TRecord>>(json)!;
            return (TTable)Activator.CreateInstance(typeof(TTable), new object[] { data })!;
        }

        // ---- Initialization ----
        public static string DataPath { get; set; } = "./data";

        public static void LoadAll()
        {
            // Touch each Lazy to force load
            _ = _item.Value;
            _ = _skill.Value;
            _ = _reward.Value;
            _loaded = true;
        }

        public static void ReloadAll()
        {
            _item = new(() => LoadTable<ItemTable, int, ItemRecord>("Item"));
            _skill = new(() => LoadTable<SkillTable, string, SkillRecord>("Skill"));
            _reward = new(() => LoadTable<RewardTable, string, RewardRecord>("Reward"));
            _loaded = false;
            LoadAll();
        }
    }
}
```

---

## 8. Foreign Key Validation

Validation happens **after** all tables are parsed and before any files are written. This is a two-pass approach:

### Pass 1: Parse all tables
Parse all `.xlsx` files into an in-memory `DataModel` (a dictionary of `tableName → DataTable`).

### Pass 2: Validate foreign keys

```typescript
class ForeignKeyValidator {
    validate(model: DataModel): ValidationError[] {
        const errors: ValidationError[] = [];

        for (const [tableName, table] of model.tables) {
            for (const field of table.schema.fields) {
                if (!field.ref) continue;  // no FK constraint

                const [refTableName, refField] = field.ref.split('.');
                const refTable = model.tables.get(refTableName);
                if (!refTable) {
                    errors.push({ type: 'FK_TABLE_NOT_FOUND', tableName, field: field.name, ref: field.ref });
                    continue;
                }

                // Check every data row
                for (const [rowIdx, record] of table.records.entries()) {
                    const value = record[field.name];
                    const exists = refTable.hasValue(refField, value);
                    if (!exists) {
                        errors.push({
                            type: 'FK_VIOLATION',
                            tableName,
                            row: rowIdx + 4,  // row number in Excel (accounting for 3 header rows)
                            field: field.name,
                            value,
                            expected: `Exists in ${field.ref}`
                        });
                    }
                }
            }
        }

        return errors;
    }
}
```

### What gets validated

| Validation | When | Error type |
|-----------|------|-----------|
| Field type matches schema type | During parse | `TYPE_MISMATCH` |
| Primary key uniqueness | After parse | `DUPLICATE_PK` |
| Primary key not null | After parse | `NULL_PK` |
| Foreign key exists in target table | Pass 2 | `FK_VIOLATION` |
| Referenced table exists | Pass 2 | `FK_TABLE_NOT_FOUND` |
| Composite PK separator collision | After parse | `PK_SEPARATOR_CONFLICT` |
| Required field missing | During parse | `MISSING_FIELD` |

### Error reporting

Errors are printed to stderr with a formatted message AND written to `validation.log`:

```
[ERROR] Reward.Items[0].ItemId = 9999  (Row 7, Sheet "Reward")
  → Foreign key violation: Value 9999 not found in Item.Id

[ERROR] Skill (Row 12)
  → Duplicate primary key "101|2" already exists
```

**The tool exits with code 1 if any errors are found.** Files are NOT written on failure (atomicity).

---

## 9. Data Access Layer (Runtime)

### Core abstract classes (hand-written, shipped as a small library)

```csharp
// === DataTable.cs (part of runtime library) ===

namespace GameConfig.Data
{
    /// <summary>
    /// Strongly-typed table with Dictionary-based O(1) lookup.
    /// TKey: primary key type (string for composite, int/string for single).
    /// TRecord: the record type.
    /// </summary>
    public abstract class DataTable<TKey, TRecord>
        where TKey : notnull
        where TRecord : class
    {
        private readonly Dictionary<TKey, TRecord> _records;
        private readonly TKey[] _allKeys;

        protected DataTable(DataTableJson<TKey, TRecord> json)
        {
            _records = json.Records;
            _allKeys = _records.Keys.ToArray();
        }

        /// <summary>Get a record by primary key. Throws KeyNotFoundException if missing.</summary>
        public TRecord Get(TKey key) => _records[key];

        /// <summary>Try to get a record. Returns null if missing.</summary>
        public TRecord? TryGet(TKey key) => _records.TryGetValue(key, out var v) ? v : null;

        /// <summary>Check if a key exists.</summary>
        public bool ContainsKey(TKey key) => _records.ContainsKey(key);

        /// <summary>Get all records.</summary>
        public IReadOnlyCollection<TRecord> GetAll() => _records.Values;

        /// <summary>Get all keys.</summary>
        public IReadOnlyCollection<TKey> GetAllKeys() => _allKeys;

        /// <summary>Number of records.</summary>
        public int Count => _records.Count;

        /// <summary>Enumerate all records.</summary>
        public IEnumerator<TRecord> GetEnumerator() => _records.Values.GetEnumerator();
    }

    /// <summary>
    /// JSON deserialization target (matches export format).
    /// </summary>
    public sealed class DataTableJson<TKey, TRecord>
        where TKey : notnull
    {
        [JsonInclude]
        public string KeyColumn { get; private set; } = string.Empty;

        [JsonInclude]
        public string PrimaryKeyType { get; private set; } = string.Empty;

        [JsonInclude]
        public Dictionary<TKey, TRecord> Records { get; private set; } = new();
    }

    /// <summary>
    /// For list-mode tables (no PK).
    /// </summary>
    public sealed class DataListJson<TRecord>
    {
        [JsonInclude]
        public bool IsList { get; private set; }

        [JsonInclude]
        public List<TRecord> Records { get; private set; } = new();
    }
}
```

### Usage examples

```csharp
using GameConfig;

// 1. Set data directory (once at startup)
Tables.DataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConfigData");

// 2. Load all tables (optional — lazy-loaded on first access)
Tables.LoadAll();

// 3. Access data — O(1) dictionary lookup
ItemRecord sword = Tables.Item.Get(1001);
Console.WriteLine(sword.Name); // "铁剑"

// 4. TryGet — safe access
if (Tables.Item.TryGet(9999) is { } item)
{
    // ...
}

// 5. Iterate all records
foreach (var skill in Tables.Skill)
{
    Console.WriteLine($"{skill.Name} (Lv.{skill.Level})");
}

// 6. Composite key access
SkillRecord fire2 = Tables.Skill.Get(SkillRecord.MakeKey(101, 2));
```

### Why `T Get<T>(key)` is not the API

Instead of a generic `T Get<T>(key)` method on a single `Tables` object, we use **typed properties** (`Tables.Item`, `Tables.Skill`). This gives:

- **Compile-time safety** — no runtime cast, no magic string
- **IDE autocomplete** — Intellisense shows available tables
- **Per-table type** — each table has its own `Get` with the correct key type

The `T Get<T>(key)` pattern often seen in game config tools works via:

```csharp
// Alternative pattern (less type-safe):
public T Get<T>(string key) where T : class
{
    // Must look up table by type, uses reflection
    var table = _tableMap[typeof(T)];
    return (T)table.Get(key);
}
```

The generated property-based approach is **preferred** because game config is static at compile-time and benefits from full type safety.

---

## 10. File Structure

### Build-time tool (Node.js/TypeScript)

```
config-tool/                            ← The CLI tool (separate repo or folder)
├── package.json
├── tsconfig.json
├── src/
│   ├── main.ts                         ← CLI entry point (commander)
│   ├── schema/
│   │   ├── SchemaDefinition.ts         ← TS types for schema YAML
│   │   ├── SchemaLoader.ts             ← Parse & validate schema YAML
│   │   └── FieldType.ts                ← Type enum & parser
│   ├── excel/
│   │   ├── ExcelReader.ts              ← Read .xlsx file, return raw cells
│   │   ├── ExcelHeaderParser.ts        ← Parse header rows (names, types, comments)
│   │   └── ExcelCellConverter.ts       ← Convert raw cell → typed value
│   ├── model/
│   │   ├── DataModel.ts                ← In-memory model (all parsed tables)
│   │   ├── DataTable.ts                ← Single table in memory
│   │   └── DataValue.ts                ← Typed value wrapper
│   ├── validation/
│   │   ├── Validator.ts                ← Orchestrates all validations
│   │   ├── PrimaryKeyValidator.ts      ← PK uniqueness & null check
│   │   ├── TypeValidator.ts            ← Type/schema consistency
│   │   ├── ForeignKeyValidator.ts      ← FK reference validation
│   │   └── ValidationError.ts          ← Error types
│   ├── export/
│   │   ├── JsonExporter.ts             ← Serialize DataModel → JSON files
│   │   ├── ExportOptions.ts            ← Output path config
│   │   └── Formatters/
│   │       ├── SingleKeyFormatter.ts   ← Format for int/string PK
│   │       ├── CompositeKeyFormatter.ts← Format for multi-column PK
│   │       └── ListFormatter.ts        ← Format for no-PK tables
│   ├── codegen/
│   │   ├── CSharpClassGenerator.ts     ← Generate record class per table
│   │   ├── CSharpTablesGenerator.ts    ← Generate Tables.cs root
│   │   ├── CSharpEnumGenerator.ts      ← Generate enums
│   │   ├── CodeGenOptions.ts
│   │   └── templates/
│   │       ├── recordClass.ts          ← Template: record class
│   │       ├── tableClass.ts           ← Template: table class
│   │       ├── tablesRoot.ts           ← Template: Tables.cs
│   │       └── enum.ts                 ← Template: enum
│   └── utils/
│       ├── pathUtils.ts
│       ├── logger.ts                   ← Console output + log file
│       └── errorFormatter.ts           ← Pretty error messages
├── schema/
│   └── tables.yaml                     ← User's schema file
├── excel/                              ← User's .xlsx input files
│   ├── Item.xlsx
│   ├── Skill.xlsx
│   └── ...
├── output/                             ← Generated output (configurable)
│   ├── data/
│   └── gen/
└── tests/
    ├── schema.test.ts
    ├── excel.test.ts
    ├── validation.test.ts
    ├── export.test.ts
    └── codegen.test.ts
```

### Runtime library (C# — shipped with the game)

```
TableTool/                              ← Existing .NET project (or a separate lib)
├── GameConfig/                         ← Runtime library namespace
│   ├── DataTable.cs                    ← Abstract base class (hand-written)
│   ├── DataTableJson.cs                ← JSON DTO (hand-written)
│   ├── Generated/                      → Auto-generated by config-tool
│   │   ├── Item.cs
│   │   ├── Skill.cs
│   │   ├── ItemCategory.cs
│   │   ├── Reward.cs
│   │   └── Tables.cs
│   └── (optional) TypeConverters.cs    ← Custom JSON converters if needed
└── Program.cs                          ← Existing entry point
```

---

## 11. Data Flow Pipeline

### Detailed pipeline diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│  1. CLI START                                                       │
│     excelsior --schema schema/tables.yaml --input excel/ --output out/ │
└──────────────────┬──────────────────────────────────────────────────┘
                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  2. LOAD SCHEMA                                                     │
│     SchemaLoader.load(tables.yaml)                                  │
│     → Validates YAML structure                                      │
│     → Resolves enum definitions                                     │
│     → Resolves ref targets (checks referenced tables exist)         │
│     → Returns TableSchema[]                                         │
└──────────────────┬──────────────────────────────────────────────────┘
                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  3. PARSE EXCEL FILES                                               │
│     for each table in schema:                                       │
│       ExcelReader.read(schema.file)                                 │
│       → Read workbook & sheet                                       │
│       → Parse header rows (names[1], types[2], comments[3])         │
│       → Validate column count matches schema.field count            │
│       → Convert each data row into DataRecord using schema types    │
│       → Handle list/map/enum cell formats                           │
│       → Store into DataModel                                        │
└──────────────────┬──────────────────────────────────────────────────┘
                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  4. VALIDATE                                                        │
│     Validator.validate(dataModel)                                   │
│       ├── PrimaryKeyValidator                                        │
│       │   → Check PK column(s) non-null across all rows             │
│       │   → Check no duplicate PK values                            │
│       │   → For composite PK: check separator '|' not in values     │
│       ├── TypeValidator                                              │
│       │   → Check every field: typeof(value) matches schema type    │
│       │   → Check list<T> elements are all type T                   │
│       │   → Check map<K,V> keys/values are correct types            │
│       └── ForeignKeyValidator                                        │
│           → For each field with `ref:` annotation:                  │
│           → Look up value in target table's specified column        │
│           → Report FK_VIOLATION for missing references              │
│                                                                     │
│     IF errors.length > 0:                                           │
│       → Print all errors to stderr + validation.log                 │
│       → exit(1)                                                     │
│       → ❌ No files written (atomicity guarantee)                    │
└──────────────────┬──────────────────────────────────────────────────┘
                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  5. EXPORT JSON                                                     │
│     for each table in dataModel:                                    │
│       JsonExporter.export(table, outputDir/data/)                   │
│       → Serialize records to JSON                                   │
│       → Format as Record Map (PK → Record) or Array                 │
│       → Write to output/data/{TableName}.json                       │
│       → Pretty-print with indentation                               │
└──────────────────┬──────────────────────────────────────────────────┘
                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│  6. GENERATE C# CODE                                                │
│     CSharpClassGenerator.generate(tables, outputDir/gen/)           │
│       for each table:                                               │
│         → Write {TableName}.cs (record + table class)               │
│         → For inline structs: generate nested classes               │
│       → Write Tables.cs (access singleton)                          │
│       → Write enums to separate files (or inline)                   │
│                                                                     │
│  7. DONE                                                            │
│     → Print summary to stdout:                                      │
│       "✓ Exported 4 tables to output/data/ (12.3KB)"               │
│       "✓ Generated 5 C# files to output/gen/"                      │
│       "  Classes: Item, Skill, ItemCategory, Reward"                │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 12. Configuration

### CLI arguments (commander)

```bash
Usage: excelsior [options]

Options:
  -s, --schema <path>       Schema definition file (YAML/JSON)  [required]
  -i, --input <dir>         Input directory for .xlsx files      [default: "./excel"]
  -o, --output <dir>        Output directory                     [default: "./output"]
  -d, --data-dir <dir>      JSON data subdirectory               [default: "data"]
  -g, --gen-dir <dir>       C# code subdirectory                 [default: "gen"]
  -n, --namespace <name>    C# namespace for generated code      [default: "GameConfig"]
  --runtime-path <dir>      Path to DataTable.cs base class      [optional]
  -v, --verbose             Verbose logging
  --dry-run                 Parse & validate only (no output)
  --version                 Show version
  -h, --help                Show help

Examples:
  excelsior --schema schema/tables.yaml
  excelsior -s config/schema.yaml -i xlsx/ -o ../GameProject/Config
  excelsior --dry-run --verbose
```

### Schema can also be referenced via convention

If `--schema` is omitted, the tool looks for:
1. `schema/tables.yaml`
2. `config/tables.yaml`
3. `tables.yaml`

---

## 13. Trade-offs & Decisions

### Decision 1: Build tool language — Node.js vs C#

| Aspect | Node.js (TypeScript) ✅ Chosen | C# |
|--------|-------------------------------|-----|
| Excel parsing | `xlsx` (SheetJS) — mature, 15M+ weekly downloads | ClosedXML / EPPlus — also mature |
| String templating | Template literals, dedicated template engines | `string.Format`, Scriban, Razor |
| Ecosystem | npm for CLI, YAML, testing | NuGet equivalents |
| Consistency | Different language from runtime | Same language as runtime |
| CI setup | Requires Node.js | Requires .NET SDK (already present) |
| Luban alignment | Yes (Java/Node.js pattern) | Different |

**Chosen: Node.js/TypeScript** because the user explicitly mentioned `xlsx` npm package, and the tool follows the Luban convention of separating the build tool language from the runtime language.

### Decision 2: JSON format — Record Map vs Array

**Chosen: Record Map (Dictionary)** for tables with PK.

- O(1) lookup at runtime vs O(n) for array scan
- The JSON is slightly larger but the runtime cost is deterministic
- Composite PKs use `|` separator — guaranteed not to collide by separator validation

### Decision 3: Code generation — String templates vs Roslyn source generators

**Chosen: String templates.**

- Source generators are powerful but complex to debug
- String templates are transparent — you can see exactly what will be generated
- The generated output is stable (schema changes infrequently)
- Easy to preview generated code in PR reviews

### Decision 4: Schema definition — YAML vs embedded in Excel vs both

**Chosen: YAML schema + Excel header annotations.**

| Approach | Pros | Cons |
|----------|------|------|
| YAML only | Clean, version-controllable | Schema and Excel can drift |
| Excel-only (headers) | Self-contained files | Hard to review schema in PR |
| Both ✅ | Redundant but validated against each other | More files to maintain |

Both sources MUST agree (header types match schema types) — the tool cross-validates. This catches typos and drift early.

### Decision 5: Atomicity on failure

**Chosen: No partial output.**

If ANY validation error exists, NO files are written. This prevents the game from accidentally loading stale or incomplete config data.

### Decision 6: `Tables.Item` (property) vs `Tables.Get<Item>(key)` (generic method)

**Chosen: Typed properties.**

| Pattern | Example | Type safety | Cast needed |
|---------|---------|-------------|-------------|
| Typed property ✅ | `Tables.Item.Get(1001)` | Full compile-time | No |
| Generic method | `Tables.Get<Item>(1001)` | Full compile-time | No (but uses `typeof` lookup) |
| String key | `Tables.Get("Item", 1001)` | None (stringly-typed) | Yes |

The property approach is simplest, most IDE-friendly, and the generated code is trivial to understand.

---

## 14. Implementation Roadmap

### Phase 1: Core pipeline (MVP)
| Step | Task | Est. |
|------|------|------|
| 1.1 | Project scaffold: `package.json`, `tsconfig.json`, CLI skeleton with `commander` | 1h |
| 1.2 | Schema types & `SchemaLoader` (parse YAML → typed schema objects) | 2h |
| 1.3 | `ExcelReader` — read xlsx, extract raw rows and cells | 2h |
| 1.4 | `ExcelHeaderParser` + `ExcelCellConverter` — map headers + convert cells | 2h |
| 1.5 | `DataModel` in-memory representation | 1h |
| 1.6 | `TypeValidator` + `PrimaryKeyValidator` | 2h |
| 1.7 | `JsonExporter` — write JSON files in Record Map format | 1h |
| 1.8 | End-to-end test: one simple table (`Item.xlsx`) → valid JSON | 1h |

### Phase 2: Code generation
| Step | Task | Est. |
|------|------|------|
| 2.1 | `CSharpClassGenerator` — record class + table class + `[JsonInclude]` | 3h |
| 2.2 | `CSharpTablesGenerator` — `Tables.cs` with lazy-loading | 2h |
| 2.3 | `CSharpEnumGenerator` — enum code gen | 1h |
| 2.4 | End-to-end: simple table → valid .cs files that compile | 1h |

### Phase 3: Advanced features
| Step | Task | Est. |
|------|------|------|
| 3.1 | Composite PK support (both export + code gen) | 2h |
| 3.2 | `ForeignKeyValidator` — cross-table reference checking | 2h |
| 3.3 | `list<T>` / `map<K,V>` parsing and code gen | 2h |
| 3.4 | Inline struct (nested objects) support | 2h |
| 3.5 | No-PK table → array format | 1h |

### Phase 4: Polish & hardening
| Step | Task | Est. |
|------|------|------|
| 4.1 | `validation.log` — detailed error output with row numbers | 1h |
| 4.2 | `--dry-run` mode | 1h |
| 4.3 | Verbose logging with timestamps | 1h |
| 4.4 | Integration tests (sample Excel files + expected outputs) | 3h |
| 4.5 | README, example project, CI configuration | 2h |

### Total estimate: **~35 hours** for a production-ready tool.

---

## Appendix A: Minimal example to get started

### `schema/tables.yaml`
```yaml
tables:
  - name: Item
    file: Item.xlsx
    primaryKey: Id
    fields:
      - name: Id
        type: int
      - name: Name
        type: string
      - name: Price
        type: int
```

### `excel/Item.xlsx`
```
| Id | Name  | Price |
| int| string| int   |
|----|-------|-------|
| 1  | Sword | 500   |
| 2  | Shield| 300   |
```

### CLI invocation
```bash
cd config-tool/
npm install
npm run build
node dist/main.js --schema schema/tables.yaml --input excel/ --output output/
```

### Output
```
output/
├── data/Item.json
├── gen/Item.cs
└── gen/Tables.cs
```

---

## Appendix B: Why not Luban directly?

Luban is a mature solution, but this tool targets a **simpler scope**:

| Feature | Luban | This tool |
|---------|-------|-----------|
| Supported export formats | 10+ (JSON, Lua, Binary, etc.) | JSON only |
| Supported languages | 10+ (C#, Java, Go, Python, etc.) | C# only |
| Schema format | Luban-specific XML/JSON | Simple YAML |
| Dependency | Java runtime | Node.js |
| Learning curve | Medium (complex config) | Low |

The goal is a focused, understandable, single-purpose tool that a small team can own and extend.

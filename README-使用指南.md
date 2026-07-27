# TableTool 使用指南

## 目录

1. [Excel 表怎么建](#1-excel-表怎么建)
2. [字段类型对照表](#2-字段类型对照表)
3. [主键配置](#3-主键配置)
4. [外键配置](#4-外键配置)
5. [特殊类型详解](#5-特殊类型详解)
6. [YAML Schema 配置](#6-yaml-schema-配置)
7. [自定义类型](#7-自定义类型)
8. [构建命令](#8-构建命令)
9. [在项目里使用](#9-在项目里使用)
10. [完整示例](#10-完整示例)

---

## 1. Excel 表怎么建

Excel 文件采用 **四行表头 + 数据行** 的格式：

```
Row 1: 字段名     (标记主键、外键)
Row 2: 类型       (int, string, ...)
Row 3: 注释       (用 ## 开头，可选)
Row 4+: 数据行    (正式数据)
```

### 举个栗子

比如你要建一个 `Items.xlsx` 物品表：

| | **A** | **B** | **C** | **D** | **E** |
|---|-------|-------|-------|-------|-------|
| **Row 1** | `#Id` | `Name` | `Price` | `Category#ref=ItemCategory.Id` | `Tags` |
| **Row 2** | `int` | `string` | `int` | `int` | `list\<string\>` |
| **Row 3** | `## 物品ID` | `## 名称` | `## 价格` | `## FK引用` | `## 标签` |
| **Row 4** | `1001` | `Iron Sword` | `500` | `1` | `[weapon, sword]` |
| **Row 5** | `1002` | `Steel Shield` | `800` | `2` | `[armor, shield]` |

### 表头标注规则

| 写法 | 含义 |
|------|------|
| `Id` | 普通字段 |
| `#Id` | 该字段是 **主键** 的组成部分 |
| `Category#ref=ItemCategory.Id` | 外键引用，指向 `ItemCategory` 表的 `Id` 字段 |
| `## 注释内容` | 整列是注释，解析时跳过 |

---

## 2. 字段类型对照表

| Excel 类型 | C# 类型 | JSON 类型 | 说明 |
|-----------|---------|-----------|------|
| `bool` | `bool` | `true/false` | 也支持 `1/0`, `yes/no` |
| `int` | `int` | number | 32位整数 |
| `long` | `long` | number | 64位整数 |
| `float` | `float` | number | 浮点数 |
| `double` | `float` | number | 同 float |
| `string` | `string` | string | 字符串 |
| `list\<T\>` | `List\<T\>` | array | T 可以是 int/string/... |
| `map\<K,V\>` | `Dictionary\<K,V\>` | object | K 通常 string/int |
| `struct` | 生成内联类 | object | 必须配合 YAML schema 定义子字段 |
| 枚举名 | 枚举类型 | string | 需在 schema 的 enums 里定义 |

---

## 3. 主键配置

### 单主键

Excel 表头用 `#` 标记：
```
#Id
```

YAML 里写：
```yaml
primary_key: Id
```

### 复合主键（多字段组合）

Excel 里多个字段都加 `#`：
```
#Id    #Level
```

YAML 里用数组：
```yaml
primary_key: [Id, Level]
```

生成为：
```csharp
var key = new SkillKey(101, 2);
var skill = Tables.Skill.Get(key);
```

> 复合主键会生成一个 `XxxKey` struct，内置 `Equals`/`GetHashCode` 支持字典查找。

### 无主键（列表模式）

Excel 不加 `#`，YAML 不写 `primary_key`：
```yaml
primary_key: ~   # 或直接省略
```

JSON 输出为数组格式（用行号做 key），C# 侧用 `string` 做 key 类型。

---

## 4. 外键配置

外键用于保证 **引用完整性**。有两层配置：

### 方式一：在 Excel 表头标注（推荐）

```
Category#ref=ItemCategory.Id
```

### 方式二：在 YAML schema 的字段里写 ref

```yaml
- name: Category
  type: int
  ref: ItemCategory.Id
```

### 校验规则

- 构建时检查：每个 `Category` 的值，都必须在 `ItemCategory.Id` 列中存在
- 不满足 → 构建失败，输出 `validation.log`，不生成任何文件
- 交叉表引用：引用的表必须在同一个 schema 里定义，顺序无关

```
Item.Category ──FK──→ ItemCategory.Id

Reward.ItemId  ──FK──→ Item.Id        # inline struct 里的字段也能做 FK
```

---

## 5. 特殊类型详解

### list\<T\> (列表)

```
Excel 类型:  list<string>, list<int>, list<float>
C# 生成:    List<string>, List<int>, List<float>
Excel 值:   [weapon, sword, starter]   或  [1, 2, 3]
```

支持多种写法：
- JSON 数组：`["weapon","sword"]`
- 方括号逗号分隔：`[weapon, sword]`
- 纯逗号分隔：`weapon, sword`

### map\<K,V\> (字典)

```
Excel 类型:  map<string,int>, map<string,string>
C# 生成:    Dictionary<string,int>, Dictionary<string,string>
Excel 值:   {atk:10, def:5}  或  {"atk":10,"def":5}
```

支持多种写法：
- JSON 对象：`{"atk":10,"def":5}`
- 键值对：`atk:10, def:5`
- 中文括号：`{atk:10, def:5}`

### struct (内联结构体)

用于 Excel 一个单元格存一段结构化数据。

**YAML 定义子字段：**
```yaml
- name: Items
  type: struct
  struct:
    - name: ItemId
      type: int
      ref: Item.Id            # struct 内部字段也能做 FK！
    - name: Count
      type: int
    - name: Rate
      type: float
```

**Excel 值**（存 JSON 字符串）：
```
[{"ItemId":1001,"Count":1,"Rate":1.0},{"ItemId":1003,"Count":3,"Rate":1.0}]
```

**生成 C#：**
```csharp
public sealed class ItemsStruct {
    public int ItemId { get; set; }
    public int Count { get; set; }
    public float Rate { get; set; }
}

public sealed class RewardRecord {
    public int Id { get; set; }
    public List<ItemsStruct> Items { get; set; }
}
```

### 枚举 (Enum)

**YAML 定义：**
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

**字段引用枚举：**
```yaml
- name: Element
  type: ElementType
```

**Excel 值：** 直接用枚举名
```
Fire    Water    None
```

**生成 C#：**
```csharp
public enum ElementType { None = 0, Fire = 1, Water = 2, Wind = 3, Earth = 4 }
public sealed class SkillRecord {
    public ElementType Element { get; set; }
}
```

---

## 6. YAML Schema 配置

完整的 `schema/tables.yaml` 示例：

```yaml
# 枚举定义 (可选)
enums:
  - name: ElementType
    values:
      None: 0
      Fire: 1
      Water: 2

# 表定义
tables:

  - name: ItemCategory                    # C# 类名: ItemCategoryTable
    description: "Item category"          # 注释
    file: ItemCategory.xlsx               # Excel 文件名
    sheet: Sheet1                         # 工作表名 (默认 Sheet1)
    primary_key: Id                       # 主键
    fields:
      - name: Id
        type: int
        comment: "唯一ID"
      - name: Name
        type: string
        comment: "分类名称"

  - name: Item
    file: Item.xlsx
    primary_key: Id
    fields:
      - name: Id
        type: int
      - name: Name
        type: string
      - name: Category
        type: int
        ref: ItemCategory.Id              # ⬅ 外键引用
      - name: Tags
        type: list<string>                # 列表类型
      - name: Attrs
        type: map<string,int>             # 字典类型
      - name: Element
        type: ElementType                 # ⬅ 枚举类型

  - name: Skill
    file: Skill.xlsx
    primary_key: [Id, Level]              # ⬅ 复合主键
    fields:
      - name: Id
        type: int
      - name: Level
        type: int
      - name: Name
        type: string

  - name: Reward
    file: Reward.xlsx
    primary_key: Id
    fields:
      - name: Id
        type: int
      - name: Items
        type: struct                      # ⬅ 内联结构体
        struct:
          - name: ItemId
            type: int
            ref: Item.Id
          - name: Count
            type: int
```

### schema 字段选项速查

| 属性 | 必需 | 说明 |
|------|------|------|
| `name` | ✅ | 字段名，**必须**与 Excel 表头匹配 |
| `type` | ✅ | 字段类型 (int/string/float/...) |
| `comment` | ❌ | 注释说明 |
| `ref` | ❌ | 外键引用 (格式: `表名.字段`) |
| `struct` | ❌ | 内联结构体的子字段列表 |

---

## 7. 自定义类型

你可以定义自己的类型，在 Excel 和 YAML 里像基本类型一样用。

### 在 schema 里定义

```yaml
custom_types:
  - name: DateTime                    # 类型名，Excel 里写这个
    storage: string                   # Excel/JSON 里的存储类型
    csharp: System.DateTime           # C# 里对应的类型
    parse: System.DateTime.Parse({0}, System.Globalization.CultureInfo.InvariantCulture)
    import: [System]                  # 需要的 using

  - name: Vector3
    storage: string
    csharp: UnityEngine.Vector3
    parse: MyVector3Parser({0})
    import: [UnityEngine]
```

### 字段里直接用

```yaml
fields:
  - name: CreateTime
    type: DateTime           # ← 直接用自定义类型名
```

### Excel 里填什么

Excel 的类型行写 `DateTime`，数据行按 `storage` 类型填（这里是 string）：
```
| CreateTime                |
| DateTime                  |
| 2024-01-15T10:30:00       |
```

### 生成的 C#

```csharp
// Tables.cs 里自动生成 JsonConverter
internal class DateTimeConverter : JsonConverter<DateTime> {
    public override DateTime Read(ref Utf8JsonReader reader, ...) {
        var str = reader.GetString();
        return DateTime.Parse(str, CultureInfo.InvariantCulture);
    }
}

// Record 类里直接用自定义类型
public sealed class ItemRecord {
    [JsonConverter(typeof(DateTimeConverter))]
    public DateTime CreateTime { get; private set; }  // 直接是 DateTime，不是 string
}
```

### 常用自定义类型示例

| 你想要的类型 | storage | csharp | parse 表达式 |
|------------|---------|--------|-------------|
| `DateTime` | string | `System.DateTime` | `System.DateTime.Parse({0}, System.Globalization.CultureInfo.InvariantCulture)` |
| `Vector2` | string | `System.Numerics.Vector2` | `ParseVector2({0})` (需自己实现) |
| `Vector3` | string | `UnityEngine.Vector3` | `UnityEngine.Vector3.Parse({0})` |
| `Color` | string | `UnityEngine.Color` | `ParseColor({0})` |
| `long_timestamp` | long | `System.DateTime` | `System.DateTimeOffset.FromUnixTimeSeconds({0}).DateTime` |

---

## 8. 构建命令

### bat 文件快捷方式（推荐）

项目里已经带了 `build.bat`：

```bash
# 直接双击或运行
build.bat

# 传参
build.bat --output D:/MyGame/Config

# 生成本地样本 Excel
sample.bat
```

### 完整用法

```bash
# 生成本地样本 Excel
dotnet run --project src/TableTool.Cli -- sample

# 构建所有表
dotnet run --project src/TableTool.Cli -- build

# 指定路径
dotnet run --project src/TableTool.Cli -- build --schema config/schema.yaml --excel xlsx/ --output ../GameProject/Config

# 指定命名空间
dotnet run --project src/TableTool.Cli -- build --namespace MyGame.Config
```

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--schema, -s` | `schema/tables.yaml` | YAML 表定义文件 |
| `--excel, -e` | `excel/` | Excel 文件目录 |
| `--output, -o` | `output/` | 输出根目录 |
| `--data, -d` | `data` | JSON 子目录 |
| `--gen, -g` | `gen` | C# 代码子目录 |
| `--namespace, -n` | `GameConfig` | 生成的 C# 命名空间 |

---

## 9. 在项目里使用

### 引用方式

1. 把 `TableTool.Runtime` 的源码加进你的 C# 项目
2. 把 `output/gen/*.cs` 生成的代码也加进项目
3. 运行时确保 JSON 文件可访问

### 初始化

```csharp
using GameConfig;

// 设置 JSON 数据目录
Tables.DataPath = Application.streamingAssetsPath + "/ConfigData";

// 可选：预加载所有表（不调也行，用到时自动懒加载）
Tables.LoadAll();
```

### 数据访问 API

```csharp
// 单主键查询
var sword  = Tables.Item.Get(1001);           // 存在返回，不存在抛异常
var maybe  = Tables.Item.TryGet(9999);         // 不存在返回 null

// 复合主键查询
var skill  = Tables.Skill.Get(new SkillKey(101, 2));
var result = Tables.Skill.TryGet(new SkillKey(999, 1));

// 判断是否存在
bool has = Tables.Item.ContainsKey(1001);

// 遍历全部
foreach (var item in Tables.Item.GetAll())
    Console.WriteLine($"{item.Id}: {item.Name}");

// 数量
int count = Tables.Item.Count;

// 重新加载（热更后调用）
Tables.ReloadAll();
```

### 接口说明

| 方法 | 说明 |
|------|------|
| `Get(key)` | O(1) 查找，找到返回记录，没找到 `throw KeyNotFoundException` |
| `TryGet(key)` | O(1) 查找，找到返回记录，没找到返回 `null` |
| `ContainsKey(key)` | 判断 key 是否存在 |
| `GetAll()` | 返回所有记录的只读集合 |
| `Count` | 记录总数 |

---

## 10. 完整示例

### 步骤 1: 写 Excel

创建 `Character.xlsx`：

| #Id | Name | Class#ref=Class.Id | Level | Skills |
|-----|------|-------------------|-------|--------|
| `int` | `string` | `int` | `int` | `list<int>` |
| `## 角色ID` | `## 名称` | `## 职业` | `## 等级` | `## 已学技能` |
| 1001 | 阿呆 | 1 | 10 | `[101,102]` |
| 1002 | 阿瓜 | 2 | 5 | `[102]` |

### 步骤 2: 写 YAML

```yaml
tables:
  - name: Character
    file: Character.xlsx
    primary_key: Id
    fields:
      - name: Id
        type: int
      - name: Name
        type: string
      - name: Class
        type: int
        ref: Class.Id
      - name: Level
        type: int
      - name: Skills
        type: list<int>
```

### 步骤 3: 构建

```bash
dotnet run -- build
```

### 步骤 4: 在代码里用

```csharp
Tables.DataPath = "./output/data";

var hero = Tables.Character.Get(1001);
Console.WriteLine($"{hero.Name} Lv.{hero.Level}");

foreach (var skillId in hero.Skills)
{
    var skill = Tables.Skill.TryGet(new SkillKey(skillId, 1));
    if (skill != null) Console.WriteLine($"  已学: {skill.Name}");
}
```

---

**总结一条龙：**
写 Excel (+type row + #PK + #ref=FK) → 配 YAML → `dotnet run -- build` → 代码里 `Tables.表名.Get(主键)` 🎯

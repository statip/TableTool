# TableTool — Excel 导表工具

把 Excel 配置表转成 JSON + C# 代码，支持主键、外键、自定义类型。

## 一分钟上手

```bash
# 1. 生成样本 Excel
sample.bat

# 2. 构建 (Excel → JSON + C#)
build.bat

# 3. 在你的项目里引用 output/gen/*.cs
#    然后直接用:
#    Tables.DataPath = "./output/data";
#    var sword = Tables.Item.Get(1001);
```

## 目录

- [1. 项目结构](#1-项目结构)
- [2. Excel 怎么写](#2-excel-怎么写)
- [3. 字段类型](#3-字段类型)
- [4. 主键 & 外键](#4-主键--外键)
- [5. YAML 配置](#5-yaml-配置)
- [6. 自定义类型](#6-自定义类型)
- [7. 构建命令](#7-构建命令)
- [8. 在项目里使用](#8-在项目里使用)
- [9. 完整示例](#9-完整示例)

---

## 1. 项目结构

```
C:\Project\TableTool/
├── build.bat / sample.bat          # 快捷命令
├── schema/tables.yaml              # ★ 你编辑这个文件定义表结构
├── excel/*.xlsx                    #   你的 Excel 数据放这里
├── output/
│   ├── data/*.json                 #   生成的 JSON 数据
│   └── gen/*.cs                    #   生成的 C# 代码
├── src/
│   ├── TableTool.Cli/              #   构建工具
│   └── TableTool.Runtime/          #   运行时库 (引用到你的项目里)
└── README.md                       #   ← 本文件
```

**使用流程：**
1. 写 Excel + 配 YAML
2. 跑 `build.bat`
3. 把 `output/gen/` 的代码加进你的 C# 项目
4. 运行时 `Tables.Item.Get(1001)` 查数据

---

## 2. Excel 怎么写

### 格式

```
Row 1: 字段名     #Id=主键, Name#ref=Table.Field=外键
Row 2: 类型       int, string, list<int>, map<string,int>, DateTime...
Row 3: 注释       (## 开头，可选)
Row 4+: 数据
```

### 栗子

| | A | B | C | D | E | F |
|---|-------|-------|-------|-------|-------|-------|
| **Row 1** | `#Id` | `Name` | `Price` | `Category#ref=ItemCategory.Id` | `Tags` | `CreateTime` |
| **Row 2** | `int` | `string` | `int` | `int` | `list<string>` | `DateTime` |
| **Row 3** | `## 物品ID` | `## 名称` | `## 价格` | `## FK` | `## 标签` | `## 创建时间` |
| **Row 4** | `1001` | `Iron Sword` | `500` | `1` | `[weapon, starter]` | `2024-01-15` |

---

## 3. 字段类型

| Excel 类型 | C# 类型 | 说明 |
|-----------|---------|------|
| `bool` | `bool` | 支持 true/false, 1/0, yes/no |
| `int` | `int` | 32 位整数 |
| `long` | `long` | 64 位整数 |
| `float` / `double` | `float` | 浮点数 |
| `string` | `string` | 字符串 |
| `list<T>` | `List<T>` | Excel 写 `[a,b,c]` 或 `a,b,c` |
| `map<K,V>` | `Dictionary<K,V>` | Excel 写 `{k1:v1,k2:v2}` 或 `{"k":v}` |
| `struct` | 生成内联类 | Excel 写 JSON 字符串 `[{"k":v}]` |
| 枚举名 | 枚举类型 | 需在 YAML 的 `enums` 里定义 |
| 自定义类型 | 自定义 | 见第 6 节 |

---

## 4. 主键 & 外键

### 主键

| 类型 | Excel 写法 | YAML 写法 |
|------|-----------|-----------|
| 单主键 | `#Id` | `primary_key: Id` |
| 复合主键 | `#Id`, `#Level` | `primary_key: [Id, Level]` |

复合主键生成 `SkillKey(101, 2)` 这样的 key struct。

### 外键

Excel 表头标注 `Category#ref=ItemCategory.Id`，YAML 里也写上：

```yaml
- name: Category
  type: int
  ref: ItemCategory.Id      # ← 指向 ItemCategory 表的 Id 字段
```

构建时会检查外键完整性，有脏数据就不输出。

---

## 5. YAML 配置

根目录的 `schema/tables.yaml`：

```yaml
enums:
  - name: ElementType
    values: { None: 0, Fire: 1, Water: 2 }

custom_types:
  - name: DateTime
    storage: string
    csharp: System.DateTime
    parse: System.DateTime.Parse({0}, System.Globalization.CultureInfo.InvariantCulture)
    import: [System]

# 独立类型 (类型公民)
structs:
  - name: RewardItem
    generate_code: true               # 生成 C# 类
    fields:
      - name: ItemId;  type: int;     ref: Item.Id
      - name: Count;   type: int
      - name: Rate;    type: float

extern_types:
  - name: MyExternal
    generate_code: false              # 不生成，用你自己写的类
    fields:
      - name: ItemId;  type: int;  ref: Item.Id

tables:
  - name: Item
    file: Item.xlsx
    sheet: Sheet1
    primary_key: Id
    fields:
      - name: Id;           type: int;          comment: "物品ID"
      - name: Name;         type: string
      - name: Price;        type: int
      - name: Category;     type: int;          ref: ItemCategory.Id
      - name: Tags;         type: list<string>
      - name: Attrs;        type: map<string,int>
      - name: CreateTime;   type: DateTime

  - name: Skill
    file: Skill.xlsx
    primary_key: [Id, Level]
    fields:
      - name: Id;       type: int
      - name: Level;    type: int
      - name: Element;  type: ElementType

  - name: Reward
    file: Reward.xlsx
    primary_key: Id
    fields:
      - name: Id;           type: int
      - name: Description;  type: string
      - name: Items;        type: list<RewardItem>     # ← 引用独立类型
```
```

---

## 6. 自定义类型

像 Luban 一样，自己定义类型给字段用。

### 定义

```yaml
custom_types:
  - name: DateTime
    storage: string          # Excel/JSON 里存成 string
    csharp: System.DateTime  # C# 里转成这个类型
    parse: System.DateTime.Parse({0}, System.Globalization.CultureInfo.InvariantCulture)
    import: [System]         # 需要的 using 命名空间
```

### 字段里用

```yaml
fields:
  - name: CreateTime
    type: DateTime    # ← 直接用自定义类型名
```

### 独立类型 (structs)

类型独立于表，可以在多张表里引用。每一行是一个"类型公民"。

```yaml
structs:
  - name: RewardItem
    generate_code: true          # true=生成C#类, false=不生成你用自己写的
    fields:
      - name: ItemId
        type: int
        ref: Item.Id             # FK 校验照做
      - name: Count
        type: int

  - name: MyExternalDrop
    generate_code: false         # 不生成代码，你自己写 C# 类
    fields:
      - name: ItemId
        type: int
        ref: Item.Id

# 表里引用
tables:
  - name: Reward
    fields:
      - name: Items
        type: list<RewardItem>   # ← 引用独立类型
```

- `structs` → 工具生成 C# 类，表里随便引用
- `extern_types` → 只校验不生成，你自己写类
- 删表不影响 struct 定义，反过来 struct 也能被多张表共用

### 生成的 C#

```csharp
// 自动生成 JsonConverter
internal class DateTimeConverter : JsonConverter<DateTime> {
    public override DateTime Read(...) {
        var str = reader.GetString();
        return DateTime.Parse(str, CultureInfo.InvariantCulture);
    }
}

// 属性直接是 DateTime，不是 string
public sealed class ItemRecord {
    [JsonConverter(typeof(DateTimeConverter))]
    public DateTime CreateTime { get; private set; }
}
```

### 配置格式一览

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` | ✅ | 类型名，Excel/YAML 字段里用这个 |
| `storage` | ✅ | Excel 和 JSON 里存的底层类型：`string`/`int`/`long`/`float` |
| `csharp` | ✅ | C# 要转换成的完整类型名，如 `System.DateTime` |
| `parse` | ❌ | 解析表达式，`{0}` 代表原始值，默认调 `类型名.Parse` |
| `import` | ❌ | 需要 `using` 的命名空间 |

### 常用自定义类型例子

```yaml
custom_types:
  # 时间
  - name: DateTime
    storage: string
    csharp: System.DateTime
    parse: System.DateTime.Parse({0}, System.Globalization.CultureInfo.InvariantCulture)
    import: [System]

  # 时间戳 (long → DateTime)
  - name: UnixTime
    storage: long
    csharp: System.DateTime
    parse: System.DateTimeOffset.FromUnixTimeSeconds({0}).DateTime
    import: [System]

  # 二维向量
  - name: Vector2
    storage: string
    csharp: System.Numerics.Vector2
    parse: System.Numerics.Vector2.Parse({0})
    import: [System.Numerics]

  # Unity 三维向量
  - name: Vector3
    storage: string
    csharp: UnityEngine.Vector3
    parse: UnityEngine.Vector3.Parse({0})
    import: [UnityEngine]

  # 布尔值存成 int (0/1)
  - name: BoolInt
    storage: int
    csharp: bool
    parse: {0} != 0

  # 颜色
  - name: Color
    storage: string
    csharp: UnityEngine.Color
    parse: ParseColor({0})
    import: [UnityEngine]
```

---

## 7. 构建命令

| 方式 | 命令 |
|------|------|
| **bat 快捷** | `build.bat` |
| | `build.bat -o D:/MyGame/Config -n MyGame.Config` |
| **生成样本** | `sample.bat` |
| **完整命令** | `dotnet run --project src\TableTool.Cli -- build` |

### 改默认输出路径

不想每次都敲参数？直接改 `build.bat`：

```bat
@echo off
chcp 65001 >nul
cd /d "%~dp0"

dotnet run --project src\TableTool.Cli -- build -o D:/MyGame/Config -n MyGame.Config %*
pause
```

改了之后下次双击 `build.bat` 就直接输出到 `D:/MyGame/Config` 了。`%*` 是保留的手动传参，如果你临时想换个路径也能在后面加。

### 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--schema, -s` | `schema/tables.yaml` | YAML 配置 |
| `--excel, -e` | `excel/` | Excel 目录 |
| `--output, -o` | `output/` | **输出根目录** |
| `--data, -d` | `data` | JSON 子目录 |
| `--gen, -g` | `gen` | C# 子目录 |
| `--namespace, -n` | `GameConfig` | C# 命名空间 |

> 每次构建前会自动清空 `output/` 下的旧文件，删表后旧代码和旧 JSON 不会残留。

---

## 8. 在项目里使用

### 加引用

把 `output/gen/*.cs` 加到你的 C# 项目，引用 `TableTool.Runtime`。

### 初始化

```csharp
using GameConfig;

Tables.DataPath = "./output/data";   // JSON 放哪
Tables.LoadAll();                    // 预加载 (不调也行，首次访问自动懒加载)
```

### 查数据

```csharp
// 单主键
var sword = Tables.Item.Get(1001);          // 没有就抛异常
var maybe = Tables.Item.TryGet(9999);        // 没有返回 null

// 复合主键
var skill = Tables.Skill.Get(new SkillKey(101, 2));

// 遍历
foreach (var item in Tables.Item.GetAll())
    Console.WriteLine($"{item.Id}: {item.Name}");

// 数量 / 是否存在
int count = Tables.Item.Count;
bool has = Tables.Item.ContainsKey(1001);

// 热重载
Tables.ReloadAll();
```

### API 一览

| 方法 | 说明 |
|------|------|
| `Get(key)` | 查，没有抛 `KeyNotFoundException` |
| `TryGet(key)` | 查，没有返回 `null` |
| `ContainsKey(key)` | 判断存在 |
| `GetAll()` | 全部记录 |
| `Count` | 总条数 |

---

## 9. 完整示例

### 建表 `Character.xlsx`

| `#Id` | `Name` | `Class#ref=Class.Id` | `Level` | `Skills` |
|-------|--------|---------------------|---------|----------|
| `int` | `string` | `int` | `int` | `list<int>` |
| `## 角色ID` | `## 名称` | `## 职业FK` | `## 等级` | `## 已学技能` |
| `1001` | `阿呆` | `1` | `10` | `[101,102]` |
| `1002` | `阿瓜` | `2` | `5` | `[102]` |

### YAML

```yaml
tables:
  - name: Character
    file: Character.xlsx
    primary_key: Id
    fields:
      - name: Id;      type: int
      - name: Name;    type: string
      - name: Class;   type: int;   ref: Class.Id
      - name: Level;   type: int
      - name: Skills;  type: list<int>
```

### 构建

```bash
build.bat
```

### 代码里用

```csharp
var hero = Tables.Character.Get(1001);
Console.WriteLine($"{hero.Name} Lv.{hero.Level}");
// → 阿呆 Lv.10
```

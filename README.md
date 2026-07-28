# TableTool — Excel 导表工具

把 Excel 配置表转成 JSON + C# 代码。支持主键、外键、自定义类型。

Excel **自己就是 schema**，不需要额外配 YAML 定义表结构。

## 一分钟上手

```bash
sample.bat         # 生成样本 Excel
build.bat          # Excel → JSON + C#
```

然后在你的 C# 项目里：
```csharp
Tables.DataPath = "./output/data";
var sword = Tables.Item.Get(1001);
```

---

## 目录

- [1. 项目结构](#1-项目结构)
- [2. Excel 怎么写](#2-excel-怎么写)
- [3. 字段类型](#3-字段类型)
- [4. 主键 & 外键](#4-主键--外键)
- [5. 可选：schema/types.yaml](#5-可选schemayaml)
- [6. 构建命令](#6-构建命令)
- [7. 在项目里使用](#7-在项目里使用)
- [8. 完整示例](#8-完整示例)

---

## 1. 项目结构

```
C:\Project\TableTool/
├── build.bat / sample.bat          # 快捷命令
├── schema/types.yaml               # (可选) 枚举/自定义类型/独立结构体
├── excel/*.xlsx                    # ★ 你的 Excel 放这里，表头就是 schema
├── output/
│   ├── data/*.json                 #   JSON 数据
│   └── gen/*.cs                    #   C# 代码
└── README.md
```

**使用流程：**
1. 写 Excel（表头自带类型、主键、外键）→ 扔进 `excel/`
2. （可选）配 `schema/types.yaml` 加枚举和自定义类型
3. 跑 `build.bat`
4. 把 `output/gen/*.cs` 加到你的 C# 项目，开查

---

## 2. Excel 怎么写

### 格式

```
Row 1: 字段名     #Id=主键,  Category#ref=Table.Field=外键
Row 2: 类型       int, string, list<int>, map<string,int>, DateTime...
Row 3: 注释       (## 开头，可选)
Row 4+: 数据
```

### 栗子

| | A | B | C | D | E | F |
|---|-------|-------|-------|-------|-------|-------|
| **Row 1** | `#Id` | `Name` | `Price` | `Category#ref=ItemCategory.Id` | `Tags` | `CreateTime` |
| **Row 2** | `int` | `string` | `int` | `int` | `list<string>` | `DateTime` |
| **Row 3** | `## 物品ID` | `## 名称` | `## 价格` | `## FK` | `## 标签` | `## 时间` |
| **Row 4** | `1001` | `Iron Sword` | `500` | `1` | `[weapon]` | `2024-01-15` |

> 工具自动扫 `excel/` 下所有 `.xlsx`，不需要配表定义文件。

---

## 3. 字段类型

| Excel 里写 | C# 类型 | 说明 |
|-----------|---------|------|
| `bool` | `bool` | 也支持 `1/0`, `yes/no` |
| `int` | `int` | 32 位 |
| `long` | `long` | 64 位 |
| `float` / `double` | `float` | 浮点 |
| `string` | `string` | 字符串 |
| `list<T>` | `List<T>` | Excel 写 `[a,b,c]` 或 `a,b,c` |
| `map<K,V>` | `Dictionary<K,V>` | Excel 写 `{k:v,k:v}` |
| 枚举名 | 枚举 | 在 `types.yaml` 的 `enums` 里定义 |
| 自定义类型名 | 自定义 | 在 `types.yaml` 的 `custom_types` 里定义 |
| `struct` 名 | struct 类 | 在 `types.yaml` 的 `structs` 里定义 |

---

## 4. 主键 & 外键

### 主键

直接在 Excel 表头用 `#` 标记：

| 类型 | 表头写 |
|------|--------|
| 单主键 | `#Id` |
| 复合主键 | `#Id` 和 `#Level` 多列都加 `#` |

工具自动识别，复合主键会生成 `XxxKey` struct。

### 外键

表头标注即可，不配 YAML 一样做外键检查：

```
Category#ref=ItemCategory.Id
```

构建时自动检查每个 Category 的值在 ItemCategory 表里是否存在。

---

## 5. 可选：schema/types.yaml

大部分情况你只需要写 Excel。但有些东西 Excel 表达不了，用这个文件补充：

```yaml
# 枚举
enums:
  - name: ElementType
    values: { None: 0, Fire: 1, Water: 2 }

# 自定义类型（用于 C# 类型转换）
custom_types:
  - name: DateTime
    storage: string
    csharp: System.DateTime
    parse: System.DateTime.Parse({0}, System.Globalization.CultureInfo.InvariantCulture)
    import: [System]

# 独立结构体（可以在多张表里引用）
structs:
  - name: RewardItem
    generate_code: true               # 生成 C# 类
    fields:
      - name: ItemId;  type: int;     ref: Item.Id
      - name: Count;   type: int
      - name: Rate;    type: float

# 外部类型（不生成代码，你用自己写的 C# 类）
extern_types:
  - name: MyExternal
    generate_code: false
    fields:
      - name: ItemId;  type: int;  ref: Item.Id
```

然后在 Excel 类型行直接写类型名：

```
| Items                        |
| list<RewardItem>             |    ← 引用 struct
```

---

## 6. 构建命令

| 方式 | 命令 |
|------|------|
| **bat 快捷** | `build.bat` |
| **指定输出路径** | `build.bat -o D:/MyGame/Config -n MyGame.Config` |
| **生成样本** | `sample.bat` |
| **完整命令** | `dotnet run --project src\TableTool.Cli -- build` |

> 每次构建前自动清空旧文件，删表后旧代码不残留。

### 改默认输出路径

```bat
REM 直接改 build.bat 里这一行:
dotnet run --project src\TableTool.Cli -- build -o D:/MyGame/Config -n MyGame.Config %*
```

### 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `-s, --schema` | `schema/types.yaml` | 类型定义（可选，没有也行） |
| `-e, --excel` | `excel/` | Excel 目录 |
| `-o, --output` | `output/` | 输出根目录 |
| `-d, --data` | `data` | JSON 子目录 |
| `-g, --gen` | `gen` | C# 子目录 |
| `-n, --namespace` | `GameConfig` | C# 命名空间 |

---

## 7. 在项目里使用

### 加引用

把 `output/gen/*.cs` 加到你的 C# 项目，引用 `src/TableTool.Runtime/`。

### 初始化

```csharp
using GameConfig;

Tables.DataPath = "./output/data";     // JSON 文件路径
Tables.LoadAll();                      // 预加载（不调也行，懒加载）
```

### 查数据

```csharp
// 单主键
var sword = Tables.Item.Get(1001);        // 没有抛 KeyNotFoundException
var maybe = Tables.Item.TryGet(9999);     // 没有返回 null

// 复合主键
var skill = Tables.Skill.Get(new SkillKey(101, 2));

// 遍历
foreach (var item in Tables.Item.GetAll())
    Console.WriteLine($"{item.Id}: {item.Name}");

// 判断/数量
bool has = Tables.Item.ContainsKey(1001);
int count = Tables.Item.Count;

// 热重载
Tables.ReloadAll();
```

### API

| 方法 | 说明 |
|------|------|
| `Get(key)` | 查，没有抛异常 |
| `TryGet(key)` | 查，没有返回 null |
| `ContainsKey(key)` | 判断存在 |
| `GetAll()` | 全部记录 |
| `Count` | 总条数 |

---

## 8. 完整示例

### 写 Excel

`excel/Character.xlsx`：

| `#Id` | `Name` | `Level` | `Skills` |
|-------|--------|---------|----------|
| `int` | `string` | `int` | `list<int>` |
| `## ID` | `## 名称` | `## 等级` | `## 已学技能` |
| `1001` | `阿呆` | `10` | `[101,102]` |
| `1002` | `阿瓜` | `5` | `[102]` |

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

### 删除某张表

删掉 `excel/Character.xlsx` 和 `schema/types.yaml` 里引用它的 ref，跑 `build.bat` 就自动清掉了。

using ClosedXML.Excel;

namespace TableTool.Cli;

/// <summary>Generates sample Excel files for testing the TableTool pipeline.
/// Excel format:
///   Row 1: Headers — #Id for PK, Name#ref=Table.Field for FK
///   Row 2: Types — int, string, float, list&lt;string&gt;, ...
///   Row 3: Comments (optional, prefix with ##)
///   Row 4+: Data</summary>
public static class SampleBuilder
{
    public static void Generate(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        GenerateItemCategory(outputDir);
        GenerateItem(outputDir);
        GenerateSkill(outputDir);
        GenerateReward(outputDir);

        Console.WriteLine($"Sample Excel files generated in: {outputDir}");
        Console.WriteLine("  4 files with types, PK markers (#), and FK references (#ref=)");
    }

    private static void GenerateItemCategory(string outputDir)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");

        // Row 1: Headers (with # for PK)
        ws.Cell(1, 1).Value = "#Id";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Icon";

        // Row 2: Types
        ws.Cell(2, 1).Value = "int";
        ws.Cell(2, 2).Value = "string";
        ws.Cell(2, 3).Value = "string";

        // Row 3: Comments (## prefix)
        ws.Cell(3, 1).Value = "## 分类唯一ID";
        ws.Cell(3, 2).Value = "## 分类名称";
        ws.Cell(3, 3).Value = "## 图标文件名";

        // Row 4+: Data
        ws.Cell(4, 1).Value = 1;
        ws.Cell(4, 2).Value = "Weapon";
        ws.Cell(4, 3).Value = "icon_weapon.png";

        ws.Cell(5, 1).Value = 2;
        ws.Cell(5, 2).Value = "Armor";
        ws.Cell(5, 3).Value = "icon_armor.png";

        ws.Cell(6, 1).Value = 3;
        ws.Cell(6, 2).Value = "Potion";
        ws.Cell(6, 3).Value = "icon_potion.png";

        ws.Cell(7, 1).Value = 4;
        ws.Cell(7, 2).Value = "Material";
        ws.Cell(7, 3).Value = "icon_material.png";

        ws.Columns().AdjustToContents();
        workbook.SaveAs(Path.Combine(outputDir, "ItemCategory.xlsx"));
    }

    private static void GenerateItem(string outputDir)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");

        // Row 1: Headers — PK (#Id), FK (#ref=Table.Field), list/map types
        ws.Cell(1, 1).Value = "#Id";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "Price";
        ws.Cell(1, 4).Value = "Category#ref=ItemCategory.Id";
        ws.Cell(1, 5).Value = "Tags";
        ws.Cell(1, 6).Value = "Attributes";

        // Row 2: Types
        ws.Cell(2, 1).Value = "int";
        ws.Cell(2, 2).Value = "string";
        ws.Cell(2, 3).Value = "int";
        ws.Cell(2, 4).Value = "int";
        ws.Cell(2, 5).Value = "list<string>";
        ws.Cell(2, 6).Value = "map<string,int>";

        // Row 3: Comments
        ws.Cell(3, 1).Value = "## 物品唯一ID";
        ws.Cell(3, 2).Value = "## 显示名称";
        ws.Cell(3, 3).Value = "## 购买价格(金币)";
        ws.Cell(3, 4).Value = "## FK → ItemCategory.Id";
        ws.Cell(3, 5).Value = "## 搜索标签";
        ws.Cell(3, 6).Value = "## 属性加成 (如 atk:10, def:5)";

        // Row 4+: Data
        ws.Cell(4, 1).Value = 1001;
        ws.Cell(4, 2).Value = "Iron Sword";
        ws.Cell(4, 3).Value = 500;
        ws.Cell(4, 4).Value = 1;
        ws.Cell(4, 5).Value = "[weapon,sword,starter]";
        ws.Cell(4, 6).Value = "{atk:10,spd:-1}";

        ws.Cell(5, 1).Value = 1002;
        ws.Cell(5, 2).Value = "Steel Shield";
        ws.Cell(5, 3).Value = 800;
        ws.Cell(5, 4).Value = 2;
        ws.Cell(5, 5).Value = "[armor,shield]";
        ws.Cell(5, 6).Value = "{def:15}";

        ws.Cell(6, 1).Value = 1003;
        ws.Cell(6, 2).Value = "Health Potion";
        ws.Cell(6, 3).Value = 50;
        ws.Cell(6, 4).Value = 3;
        ws.Cell(6, 5).Value = "[potion,consumable]";
        ws.Cell(6, 6).Value = "{hp:50}";

        ws.Cell(7, 1).Value = 1004;
        ws.Cell(7, 2).Value = "Mana Potion";
        ws.Cell(7, 3).Value = 60;
        ws.Cell(7, 4).Value = 3;
        ws.Cell(7, 5).Value = "[potion,consumable]";
        ws.Cell(7, 6).Value = "{mp:30}";

        ws.Cell(8, 1).Value = 2001;
        ws.Cell(8, 2).Value = "Dragon Scale";
        ws.Cell(8, 3).Value = 5000;
        ws.Cell(8, 4).Value = 4;
        ws.Cell(8, 5).Value = "[rare,material]";
        ws.Cell(8, 6).Value = "{}";

        ws.Columns().AdjustToContents();
        workbook.SaveAs(Path.Combine(outputDir, "Item.xlsx"));
    }

    private static void GenerateSkill(string outputDir)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");

        // Row 1: Headers — composite PK (#Id + #Level), enum type
        ws.Cell(1, 1).Value = "#Id";
        ws.Cell(1, 2).Value = "#Level";
        ws.Cell(1, 3).Value = "Name";
        ws.Cell(1, 4).Value = "Description";
        ws.Cell(1, 5).Value = "Cost";
        ws.Cell(1, 6).Value = "Element";

        // Row 2: Types
        ws.Cell(2, 1).Value = "int";
        ws.Cell(2, 2).Value = "int";
        ws.Cell(2, 3).Value = "string";
        ws.Cell(2, 4).Value = "string";
        ws.Cell(2, 5).Value = "float";
        ws.Cell(2, 6).Value = "ElementType";

        // Row 3: Comments
        ws.Cell(3, 1).Value = "## 技能ID";
        ws.Cell(3, 2).Value = "## 技能等级 (复合主键)";
        ws.Cell(3, 3).Value = "## 技能显示名";
        ws.Cell(3, 4).Value = "## 技能描述";
        ws.Cell(3, 5).Value = "## MP消耗";
        ws.Cell(3, 6).Value = "## 元素类型 (枚举)";

        // Row 4+: Data — composite key: same Id + different Level
        ws.Cell(4, 1).Value = 101;
        ws.Cell(4, 2).Value = 1;
        ws.Cell(4, 3).Value = "Fire Bolt";
        ws.Cell(4, 4).Value = "Launches a small fireball";
        ws.Cell(4, 5).Value = 10.0;
        ws.Cell(4, 6).Value = "Fire";

        ws.Cell(5, 1).Value = 101;
        ws.Cell(5, 2).Value = 2;
        ws.Cell(5, 3).Value = "Fire Blast";
        ws.Cell(5, 4).Value = "Launches a powerful fireball";
        ws.Cell(5, 5).Value = 20.0;
        ws.Cell(5, 6).Value = "Fire";

        ws.Cell(6, 1).Value = 101;
        ws.Cell(6, 2).Value = 3;
        ws.Cell(6, 3).Value = "Inferno";
        ws.Cell(6, 4).Value = "Engulfs the target in hellfire";
        ws.Cell(6, 5).Value = 35.0;
        ws.Cell(6, 6).Value = "Fire";

        ws.Cell(7, 1).Value = 102;
        ws.Cell(7, 2).Value = 1;
        ws.Cell(7, 3).Value = "Heal";
        ws.Cell(7, 4).Value = "Restores a small amount of HP";
        ws.Cell(7, 5).Value = 15.0;
        ws.Cell(7, 6).Value = "None";

        ws.Cell(8, 1).Value = 102;
        ws.Cell(8, 2).Value = 2;
        ws.Cell(8, 3).Value = "Cure";
        ws.Cell(8, 4).Value = "Restores a moderate amount of HP";
        ws.Cell(8, 5).Value = 25.0;
        ws.Cell(8, 6).Value = "None";

        ws.Cell(9, 1).Value = 103;
        ws.Cell(9, 2).Value = 1;
        ws.Cell(9, 3).Value = "Ice Shard";
        ws.Cell(9, 4).Value = "Hurls a shard of ice";
        ws.Cell(9, 5).Value = 12.0;
        ws.Cell(9, 6).Value = "Water";

        ws.Columns().AdjustToContents();
        workbook.SaveAs(Path.Combine(outputDir, "Skill.xlsx"));
    }

    private static void GenerateReward(string outputDir)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");

        // Row 1: Headers
        ws.Cell(1, 1).Value = "#Id";
        ws.Cell(1, 2).Value = "Description";
        ws.Cell(1, 3).Value = "Items";

        // Row 2: Types — struct is a list of inline objects
        ws.Cell(2, 1).Value = "int";
        ws.Cell(2, 2).Value = "string";
        ws.Cell(2, 3).Value = "list<struct>";

        // Row 3: Comments
        ws.Cell(3, 1).Value = "## 奖励ID";
        ws.Cell(3, 2).Value = "## 奖励描述";
        ws.Cell(3, 3).Value = "## 奖励物品列表 (JSON array)";

        // Row 4+: Data — Items as JSON array of inline struct
        ws.Cell(4, 1).Value = 1;
        ws.Cell(4, 2).Value = "Starter Pack";
        ws.Cell(4, 3).Value = "[{\"ItemId\":1001,\"Count\":1,\"Rate\":1.0},{\"ItemId\":1003,\"Count\":3,\"Rate\":1.0}]";

        ws.Cell(5, 1).Value = 2;
        ws.Cell(5, 2).Value = "Dragon Hunter Reward";
        ws.Cell(5, 3).Value = "[{\"ItemId\":2001,\"Count\":1,\"Rate\":0.5},{\"ItemId\":1002,\"Count\":1,\"Rate\":1.0}]";

        ws.Cell(6, 1).Value = 3;
        ws.Cell(6, 2).Value = "Daily Login Bonus";
        ws.Cell(6, 3).Value = "[{\"ItemId\":1003,\"Count\":1,\"Rate\":1.0}]";

        ws.Columns().AdjustToContents();
        workbook.SaveAs(Path.Combine(outputDir, "Reward.xlsx"));
    }
}

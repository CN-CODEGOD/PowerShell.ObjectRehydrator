using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;

/// <summary>
/// PoC: 验证 C# 能否通过 PowerShell SDK 解析脚本 class 为 System.Type 并实例化。
/// 
/// 测试链路：
///   PowerShell Runspace → 定义 class → PSTypeName/TypeResolver → System.Type
///   → Activator.CreateInstance → Reflection 设属性 → 验证真实实例
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("=== PoC: PowerShell Class Type Resolution ===\n");
        int failures = 0;

        // 1. 创建 Runspace 并定义脚本 class
        using var runspace = RunspaceFactory.CreateRunspace(
            InitialSessionState.CreateDefault());
        runspace.Open();

        using var ps = PowerShell.Create();
        ps.Runspace = runspace;

        // 定义一个无构造函数的纯数据类（零代码目标）
        ps.AddScript(@"
            class DemoZeroCode {
                [string]$Name
                [int]$Value
                [double]$Score
            }
        ").Invoke();
        ps.Commands.Clear();

        // 也定义一个有嵌套的类
        ps.AddScript(@"
            class DemoInner {
                [string]$Text
                [string]$Label
            }
            class DemoNested {
                [DemoInner]$Inner
                [string]$Title
                [int]$Count
            }
        ").Invoke();
        ps.Commands.Clear();

        Console.WriteLine("[Step 1] 脚本 class 已定义: DemoZeroCode, DemoInner, DemoNested\n");

        // 2. 尝试通过多种路径解析 System.Type
        Console.WriteLine("[Step 2] 类型解析测试\n");

        // 路径 A: PSTypeName (PowerShell 官方 Type Resolver)
        Type? typeA = null;
        try
        {
            var typeName = new PSTypeName("DemoZeroCode");
            typeA = typeName.Type;
            Console.WriteLine($"  [A] PSTypeName(\"DemoZeroCode\").Type = {(typeA != null ? typeA.FullName : "null")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [A] PSTypeName 失败: {ex.Message}");
        }

        // 路径 B: PowerShell.Invoke 获取类型
        Type? typeB = null;
        try
        {
            ps.Commands.Clear();
            var results = ps.AddScript("[DemoZeroCode]").Invoke();
            if (results.Count > 0)
            {
                typeB = results[0].BaseObject as Type;
                Console.WriteLine($"  [B] [DemoZeroCode] → {(typeB != null ? typeB.FullName : "null")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [B] [DemoZeroCode] 失败: {ex.Message}");
        }

        // 路径 C: Type.GetType (预期失败)
        Type? typeC = Type.GetType("DemoZeroCode");
        Console.WriteLine($"  [C] Type.GetType(\"DemoZeroCode\") = {(typeC?.FullName ?? "null")} (预期 null)");

        // 路径 D: 扫描已加载程序集
        Type? typeD = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            typeD = asm.GetType("DemoZeroCode");
            if (typeD != null) break;
        }
        Console.WriteLine($"  [D] AppDomain 扫描 = {(typeD?.FullName ?? "null")}");

        // 选择成功的 Type
        Type? resolved = typeA ?? typeB ?? typeD;
        if (resolved == null)
        {
            Console.WriteLine("\n❌ 所有路径均失败！方案一不可行，需 fallback 方案二。");
            return 1;
        }

        Console.WriteLine($"\n  ✅ 解析成功: {resolved.FullName}");
        Console.WriteLine($"     Assembly: {resolved.Assembly.GetName().Name}");
        Console.WriteLine($"     BaseType: {resolved.BaseType?.FullName}");

        // 3. 检查构造函数
        Console.WriteLine("\n[Step 3] 构造函数检查\n");
        var ctors = resolved.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var ctor in ctors)
        {
            var parms = ctor.GetParameters();
            Console.WriteLine($"  ctor({string.Join(", ", parms.Select(p => $"{p.ParameterType.Name} {p.Name}"))})" +
                              $" [{(ctor.IsPublic ? "public" : "non-public")}]");
        }
        if (ctors.Length == 0)
            Console.WriteLine("  (无显式构造函数)");

        // 4. Activator.CreateInstance 实例化
        Console.WriteLine("\n[Step 4] Activator.CreateInstance 实例化\n");
        try
        {
            object? instance = Activator.CreateInstance(resolved);
            if (instance == null)
            {
                Console.WriteLine("  ❌ CreateInstance 返回 null");
                failures++;
            }
            else
            {
                Console.WriteLine($"  ✅ 实例化成功: {instance.GetType().FullName}");
                Console.WriteLine($"     Is DemoZeroCode: {instance.GetType() == resolved}");

                // 5. Reflection 设属性
                Console.WriteLine("\n[Step 5] Reflection 设属性\n");
                var props = resolved.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in props)
                    Console.WriteLine($"  属性: {p.PropertyType.Name} {p.Name} (CanWrite={p.CanWrite})");

                // 设值
                var nameProp = resolved.GetProperty("Name");
                var valueProp = resolved.GetProperty("Value");
                var scoreProp = resolved.GetProperty("Score");

                if (nameProp != null) { nameProp.SetValue(instance, "hello"); Console.WriteLine("  Name = \"hello\" ✅"); }
                if (valueProp != null) { valueProp.SetValue(instance, 42); Console.WriteLine("  Value = 42 ✅"); }
                if (scoreProp != null) { scoreProp.SetValue(instance, 99.5); Console.WriteLine("  Score = 99.5 ✅"); }

                // 读回验证
                Console.WriteLine("\n[Step 6] 读回验证\n");
                Console.WriteLine($"  Name  = {nameProp?.GetValue(instance)}");
                Console.WriteLine($"  Value = {valueProp?.GetValue(instance)}");
                Console.WriteLine($"  Score = {scoreProp?.GetValue(instance)}");

                // 通过 PowerShell 验证类型
                ps.Commands.Clear();
                ps.AddScript("$args[0] -is [DemoZeroCode]")
                  .AddArgument(instance);
                var psResult = ps.Invoke();
                bool isRealType = psResult.Count > 0 && (bool)(psResult[0].BaseObject);
                Console.WriteLine($"  pwsh: -is [DemoZeroCode] = {isRealType}");

                if (!isRealType) failures++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ CreateInstance 失败: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }

        // 7. 嵌套类测试
        Console.WriteLine("\n[Step 7] 嵌套类测试 (DemoNested + DemoInner)\n");
        try
        {
            Type? innerType = null;
            Type? nestedType = null;

            // 解析 DemoInner（路径 B: PowerShell.Invoke）
            ps.Commands.Clear();
            var innerResults = ps.AddScript("[DemoInner]").Invoke();
            if (innerResults.Count > 0) innerType = innerResults[0].BaseObject as Type;
            Console.WriteLine($"  DemoInner Type: {innerType?.FullName ?? "null"}");

            // 解析 DemoNested（路径 B）
            ps.Commands.Clear();
            var nestedResults = ps.AddScript("[DemoNested]").Invoke();
            if (nestedResults.Count > 0) nestedType = nestedResults[0].BaseObject as Type;
            Console.WriteLine($"  DemoNested Type: {nestedType?.FullName ?? "null"}");

            if (innerType != null && nestedType != null)
            {
                object inner = Activator.CreateInstance(innerType)!;
                innerType.GetProperty("Text")?.SetValue(inner, "inner text");
                innerType.GetProperty("Label")?.SetValue(inner, "inner label");

                object nested = Activator.CreateInstance(nestedType)!;
                nestedType.GetProperty("Inner")?.SetValue(nested, inner);
                nestedType.GetProperty("Title")?.SetValue(nested, "nested title");
                nestedType.GetProperty("Count")?.SetValue(nested, 7);

                Console.WriteLine($"  ✅ 嵌套实例化成功");
                Console.WriteLine($"     Inner.Text  = {innerType.GetProperty("Text")?.GetValue(inner)}");
                Console.WriteLine($"     Inner.Label = {innerType.GetProperty("Label")?.GetValue(inner)}");
                Console.WriteLine($"     Nested.Title = {nestedType.GetProperty("Title")?.GetValue(nested)}");
                Console.WriteLine($"     Nested.Count = {nestedType.GetProperty("Count")?.GetValue(nested)}");

                // pwsh 验证
                ps.Commands.Clear();
                ps.AddScript("$args[0] -is [DemoNested]").AddArgument(nested);
                var r = ps.Invoke();
                bool ok = r.Count > 0 && (bool)(r[0].BaseObject);
                Console.WriteLine($"     pwsh: -is [DemoNested] = {ok}");
                if (!ok) failures++;
            }
            else
            {
                Console.WriteLine("  ❌ 嵌套类型解析失败");
                failures++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 嵌套测试失败: {ex.Message}");
            failures++;
        }

        // 总结
        Console.WriteLine("\n========================================");
        if (failures == 0)
            Console.WriteLine("✅ PoC 全部通过！方案一技术路径可行。");
        else
            Console.WriteLine($"❌ PoC 有 {failures} 个失败项。");
        Console.WriteLine("========================================");

        return failures == 0 ? 0 : 1;
    }
}

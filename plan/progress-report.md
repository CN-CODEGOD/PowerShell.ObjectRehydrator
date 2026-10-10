# PowerShell.ObjectRehydrator — 工作总结报告

> 生成日期：2026-10-04
> 项目根：`/root/workspace/PowerShell.ObjectRehydrator/`
> 参与方：规划师（planner agent）+ Qwen Code（宿主侧编译/测试）
> 状态：**方案二已完成并验证；方案一（终态）设计已定，待推进**

---

## 1. 项目背景与目标

### 1.1 现状痛点

原 `import-xml` 模块的 rehydrate 流程要求**每个类手写 `save()` 方法**，
且方法体内的 XML 结构是**手工逐层写死**的：

```powershell
class nestedInstance {
    [testInstance]$testInstance
    [string]$text
    [object]save(){
        # ↓ 这段 XML 结构靠人手写；加一个属性就要改这里
        [xelement]::new("object", [XAttribute]::new("name","nestedInstance"),
            [XObject[]]@(
                [XElement]::new("property",[XAttribute]::new("name","testInstance"),
                    [XElement]::new("property",[XAttribute]::new("name","txt"),$this.testInstance.txt),
                    [XElement]::new("property",[XAttribute]::new("name","name"),$this.testInstance.name)),
                [XElement]::new("property",[XAttribute]::new("name","text"),$this.text)
            )).ToString()
    }
}
```

**痛点**：加/改/删一个属性 → 必须同步手改 `save()`，极易与类结构不一致。

### 1.2 项目目标（GOAL）

> **从 C# 底层自动解析、重建 PowerShell 类 —— 用户不用手写 `save()` 方法，
> 也能直接序列化并重建类。**
>
> 即：类的**结构**（属性名 / 类型 / 嵌套关系）由 C# 侧**反射自动推导**，
> 用户只需按约定提供极少的锚点。

### 1.3 技术路线（两条）

| | 方案一（终态/最好） | 方案二（已落地/备用） |
|---|---|---|
| 思路 | 像 `Import-Clixml` 一样，纯 XML 自解析 → 保存 → 重建 | 类保留固定锚点，结构推导自动化 |
| 用户写类时 | **零代码**（不写 `save()` 锚点）| 只加**一行固定锚点** `save()` |
| 序列化入口 | 纯外部 cmdlet（`Export-XmlObject`）| 类方法调用 + 命令行别名 |
| 前提 | XML 必须**自描述**（含类型元数据）| 复用现有序列化器；已实现 |
| 状态 | 设计完成，待实现 | **✅ 已实现并验证** |

### 1.4 与官方 CLIXML 的关系

**不是重新实现 CLIXML**，而是补上 CLIXML 不做的最后一步：
官方 CLIXML 反序列化只产出 `PSObject` + `Deserialized.Xxx` 快照，
**从不执行** `Type.GetType() → ctor → new Class()`。
本项目替换该最终 Factory 层，重建**真实类型的 PS 类实例**。

---

## 2. 已完成步骤（Step 1-5）

### Step 0 — 项目迁移 ✅
- 新建独立项目 `/root/workspace/PowerShell.ObjectRehydrator/`
- `compile-plan.md` 从 `import-xml` 迁入（原处保留"迁往说明" + `.original` 备份）
- **`import-xml` 本尊、`dre/modules/import-xml` 副本均未改动**

### Step 1 — C# 骨架 ✅

```
src/ObjectRehydrator/
├── ObjectRehydrator.csproj           # net8.0 + Microsoft.PowerShell.SDK 7.4.6
├── ObjectRehydrator.psd1             # RootModule = DLL
├── Cmdlets/
│   ├── ImportXmlCommand.cs           # Import-Xml
│   ├── SaveXmlObjectCommand.cs       # Save-XmlObject
│   ├── SaveXmlInstanceCommand.cs     # Save-XmlInstance (Alias: save)  ★方案二入口
│   └── ExportXmlObjectCommand.cs     # Export-XmlObject
├── Serialization/
│   ├── XElementSerializer.cs         # 移植 ConvertTo-XElement（已修 3 硬伤）
│   └── InstanceSerializer.cs         # ★方案二核心：反射实例 → 自动 XML
└── Rehydration/
    ├── TypeResolver.cs               # 名字 → 真实 Type
    ├── ObjectFactory.cs              # ctor / DoInit 注入
    └── ObjectRehydrator.cs           # 核心还原流程
```

### Step 2 — 首次构建 & 编译修复 ✅
- pwsh 容器安装 **.NET 8 SDK (8.0.425)**
- `./build.sh` **构建成功**
- 修复 4 个编译错误（见 §3）
- 产出 `dist/ObjectRehydrator/`（DLL + 依赖 + psd1）

### Step 3 — pwsh 内 Import 冒烟测试 ✅
- `Import-Module dist/ObjectRehydrator/ObjectRehydrator.psd1` 成功
- 导出命令：`Import-Xml` / `Save-XmlObject` / `Export-XmlObject` / `Save-XmlInstance`（+ `save` 别名）

### Step 4 — 回归测试（对照 import-xml 原行为）✅
详见 §4.2。

### Step 5 — 方案二：自动化 `save`（消除手写结构）✅
- `InstanceSerializer`：反射属性 → 自动生成 `<object>` XML
- `Save-XmlInstance` cmdlet + `save` 别名（psd1 导出）
- 递归展开嵌套类属性；hashtable / array / scriptblock 分支
- 排除 `hidden` 属性与约定 `$Path`
- 示例类 `tests/demo.class.ps1`：`save()` 内一行 `[InstanceSerializer]::Save($this)`
- 回归 `tests/roundtrip.tests.ps1` —— **6/6 通过**

---

## 3. 编译期修复的 4 个问题

| # | 文件:行 | 问题 | 修复 |
|---|---|---|---|
| 1 | `SaveXmlObjectCommand.cs:51` | `??` 与 `?:` 运算符优先级 | 加括号 |
| 2 | `ObjectRehydrator.cs:82` | 缺 `using System.Linq` | 补 using |
| 3 | `InstanceSerializer.cs:76` | `PSPropertyInfo` 没有 `IsHidden` | 改用属性/成员判断 |
| 4 | `ExportXmlObjectCommand.cs:52` | `Count()` 需要 LINQ | 补 `using System.Linq` |

---

## 4. 测试验证结果

### 4.1 Pester roundtrip：6/6 通过

覆盖：嵌套对象结构、hashtable、array（index 正确）、scriptblock、
排除 hidden `$Path`、Save→Import 往返还原为真实类实例。

### 4.2 Step 4 回归（`tests/xml/*.xml`）

加载 `test.class.ps1` 原始类定义后：

| 文件 | 结果 |
|---|---|
| `nestedInstance.xml` | ✅ `-is [nestedInstance]`=True; text=text; testInstance.text=default text |
| `array.xml` | ✅ 3×arrayInstance, `-is [arrayInstance]`=True |
| `hashobject.xml` | ✅ 3×hashtableInstance + 1×PSCustomObject（降级，类型名不匹配）|
| `scriptblockInstance.xml` | ✅ 3×scriptblockInstance, scriptblock type=ScriptBlock |
| `Example_Instances.xml` | ✅ 降级不报错 |

**关键结论**：旧样本能还原为**真实 PS 类实例**（非 `Deserialized.*` 快照）；
畸形/未知类型安静降级，不抛异常。

---

## 5. class 内 cmdlet 调用问题的解决方案

### 问题（事前预警，事后确认）

PowerShell **class 方法内**直接调用外部 cmdlet / 别名（如 `save $this`）
**解析不可靠**（实测确认存在）。

### 解决方案

类内改用 **.NET 静态方法**：

```powershell
class DemoNested {
    [DemoInner]$inner
    [string]$text

    # ★ 锚点：内容恒定，结构由 C# 反射生成
    [object] save() {
        return [PowerShell.ObjectRehydrator.Serialization.InstanceSerializer]::Save($this)
    }

    # 重建支持（供 ObjectFactory 使用）
    [void] DoInit([pscustomobject]$o) {
        foreach ($n in ($o | Get-Member -Type NoteProperty).Name) { $this.$n = $o.$n }
    }
    DemoNested([pscustomobject]$o) { $this.DoInit($o) }
    DemoNested() {}
}
```

`tests/demo.class.ps1` 已更新此模式，roundtrip 6/6 通过。

### 双入口关系

| 入口 | 形式 | 用途 |
|---|---|---|
| ① 类内锚点 | `[InstanceSerializer]::Save($this)` | 类方法内序列化实例 |
| ② 命令行/交互 | `save` 别名（→ `Save-XmlInstance`） | 手动调用，如 `$obj \| save` |

两者底层走同一实现（`InstanceSerializer.Save`），**行为一致**。

---

## 6. PSSerializer 实验结论（A2 否决）

### 实验

在 pwsh 容器对 `DemoNested` 实例执行 `[System.Management.Automation.PSSerializer]::Serialize($obj)`：

```xml
<Objs Version="1.1.0.1" xmlns="http://schemas.microsoft.com/powershell/2004/04">
  <Obj RefId="0">
    <TN RefId="0"><T>DemoNested</T><T>System.Object</T></TN>
    <ToString>DemoNested</ToString>
    <Props>
      <S N="inner">DemoInner</S>    <!-- ❌ 嵌套对象只变成了 ToString() 字符串 -->
      <S N="text">hello</S>
    </Props>
  </Obj>
</Objs>
```

### 关键发现

1. ✅ 类型名保留：`<T>DemoNested</T>`
2. ❌ **嵌套对象未展开**：`inner` 属性变成 `<S>DemoInner</S>`（仅 `.ToString()` 结果），不是嵌套对象
3. ❌ 反序列化结果：`Deserialized.DemoNested`（PSObject，非真实类型）

### 结论

**A2 路线（直接用 PSSerializer / CLIXML 底座）不可行** —— 它连嵌套对象都不展开，
只存了字符串，且反序列化不产出真实类型。

→ **选定 A1（自有格式 + type 元数据）**，复用我们已有的 `InstanceSerializer`。

---

## 7. 方案一（A1）设计方向

### 7.1 核心前提：XML 必须自描述

现格式只存 value，**丢了类型**（不知 `[string]`/`[int]`、不知嵌套子类型、不知数组元素类型）。
→ A1：在内层 XML **加 `type` 元数据**。

设计目标（示例）：

```xml
<objects>
  <object name="DemoNested" type="DemoNested">
    <property name="inner" type="DemoInner">
      <property name="text" type="System.String">default text</property>
      <property name="name" type="System.String">default name</property>
    </property>
    <property name="text" type="System.String">text</property>
  </object>
</objects>
```

### 7.2 为何 A1 相对安全

`InstanceSerializer` 已能正确递归嵌套 / hashtable / array / scriptblock
（roundtrip 6/6 验证）。方案一只需：

1. 每个元素多写一个 `type` 属性；
2. 反序列侧读 `type` 路由到 `TypeResolver`；
3. 序列化入口由类锚点改为**外部命令**。

### 7.3 关键挑战

1. **类型名与值的映射**：C# 拿到 `System.String` / `DemoInner` → `TypeResolver` 解析；
   脚本 class 需会话可见。
2. **嵌套类型名歧义**：旧格式嵌套子对象**类型名继承外层**；自描述后需每层真实类型。
3. **强类型赋值**：仍需 `LanguagePrimitives.ConvertTo(value, propType)`。
4. **兼容性**：(a) 旧 XML 降级 PSObject；或 (b) 完全换代。**[待用户拍板]**

### 7.4 任务拆分（A1）

- [ ] 6.4.1 序列化端：`InstanceSerializer` 加 `WithTypeInfo` → 输出带 `type` 的 XML
- [ ] 6.4.2 反序列化端：`ObjectRehydrator.ParseState` 读 `type` → 路由 `TypeResolver`
- [ ] 6.4.3 类零代码验证：**无 `save()`** 的类仅靠 `type` 标记被重建
- [ ] 6.4.4 兼容层：无 `type` 旧 XML → 降级路径（已实测不报错）
- [ ] 6.4.5 外部入口：`Export-XmlObject $obj` 驱动序列化（不依赖类方法）
- [ ] 6.4.6 回归：自描述样本 + 双向往返

### 7.5 序列化外部化（已确认 ✅）

"类零代码"下，序列化入口不再是 `$obj.save()`（类方法）→ 改为**外部 cmdlet**
（`Export-XmlObject $obj`）。**已与 Qwen Code 确认接受** —— 这正是方案一"类零代码"的核心价值。

---

## 8. 待用户决策项

| # | 决策项 | 选项 | 现状 |
|---|---|---|---|
| 1 | **方案一 XML 兼容策略** | (a) 旧 XML 走"尽力而为 + 降级 PSObject"；(b) 完全换代，旧文件不支持 | ⏳ 待定 |
| 2 | **`type` 属性粒度** | **方案 X**：每层都标（object/嵌套 property/标量）；**方案 Y**：只标嵌套复杂类型，标量靠推断 | ⏳ 待定 |
| 3 | **cmdlet 名冲突** | `Import-Xml` 可能与旧模块撞名 → 是否换前缀（如 `Import-ObjXml`） | ⏸ 搁置（用户明确暂不处理）|
| 4 | **`ObjectRehydrator` 命名** | 类名 == 命名空间尾段；当前用全限定位规避。（编译已过，暂无影响） | ✅ 已规避 |

**建议**：决策 2 倾向**方案 Y**（最小改动、保留旧格式观感）；
决策 1 倾向先实现 (a) 兼容路径，再评估是否换代。

---

## 9. 环境信息

| 项 | 值 |
|---|---|
| 目标框架 | **net8.0** |
| PowerShell SDK | **Microsoft.PowerShell.SDK 7.4.6** |
| 构建环境 .NET SDK | **8.0.425** |
| 构建环境 | **pwsh 容器** |
| 规划机 | Debian 12 容器（无 dotnet / 无 pwsh，不安装 SDK）|
| 构建脚本 | `./build.sh` |
| 产物 | `dist/ObjectRehydrator/`（DLL + 依赖 + psd1）|

**版本对齐要求**：DLL 使用的 PowerShell SDK 7.4.6 必须与运行它的 pwsh 版本兼容；
跨大版本（如 pwsh 7.2）可能加载失败。

**分工**：
- **规划机（planner agent）**：维护 `plan/plan.md`、`plan/todo.md`、记忆、报告
- **宿主侧（Qwen Code）**：pwsh 容器内编译、测试、实现

---

## 附：项目结构一览

```
PowerShell.ObjectRehydrator/
├── build.sh
├── README.md
├── compile-plan.md                   # 迁入的历史规划
├── plan/
│   ├── plan.md                       # 计划真源
│   ├── progress-report.md            # 本报告
│   └── drafts/
├── src/ObjectRehydrator/             # C# 源码（见 §1.2 骨架）
└── tests/
    ├── Example_Instances.xml
    ├── test.class.ps1                # 旧类（手写 save，对照）
    ├── demo.class.ps1                # 方案二示例
    ├── roundtrip.tests.ps1           # 方案二回归（6/6）
    └── xml/                          # 各类型样本
```

---

*报告完毕。方案二闭环，方案一待实现；见 §8 待决策项。*

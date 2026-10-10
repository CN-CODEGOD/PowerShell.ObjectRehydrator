# PowerShell.ObjectRehydrator — 迁移实施计划

> 目标：把 `import-xml` 的序列化 / rehydrate 逻辑，重写为一个 **C# 编译的 DLL 二进制 PowerShell 模块**，用 `Import-Module` 导入 pwsh。
> 参考 PowerShell 官方 `serialization.cs` 的架构，但替换最后的 **Object Factory** 层。
>
> 项目根：`/root/workspace/PowerShell.ObjectRehydrator/`
> 旧实现参照：`/root/workspace/import-xml/`（保持原样，不动）

---

## 0. 项目目标（GOAL）★

### 0.1 现状痛点

现行 `import-xml` 的 rehydrate 依赖**每个类手写 `save()` 方法**，且 `save()` 里的
XML 结构是**手工逐层写死**的：

```powershell
class nestedInstance {
    [testInstance]$testInstance
    [string]$text
    [object]save(){
        # ↓ 这段 XML 结构靠人手写，加一个属性就要改这里
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

### 0.2 项目目标

> **从 C# 底层自动解析、重建 PowerShell 类 —— 用户不用手写 `save()` 方法，
> 也能直接序列化并重建类。**

即：类的**结构**（属性名 / 类型 / 嵌套关系）由 C# 侧**反射自动推导**，
用户只需按约定提供极少的锚点。

### 0.3 两条技术路线

#### 方案一（最好情况）—— 像 `Import-Clixml` 一样纯 XML 自解析

```text
XML →（C# 自解析）→ 保存 → 重建真实类实例
```
- 用户写类时 **零额外代码**（不写 `save()`、不写静态方法）。
- **前提**：XML 必须**自描述**（含类型/结构元数据）。
- → 需要**定义新 XML 格式**（可参考/复用 CLIXML），旧的 `import-xml` 文件
  **不再兼容**（或只能"尽力而为 + 降级 PSObject"）。

#### 方案二（备用）—— 类保留固定锚点，结构推导自动化

```powershell
class nestedInstance {
    [testInstance]$testInstance
    [string]$text

    [object] save() {
        save $this        # ← 固定不变的壳，把 $this 交给 C# 全局 save 函数
    }
}
```
- 用户写类时**只需这个方法**（内容恒定不变），**不再手写 XML 结构**。
- C# 侧提供全局 `save($object)` 函数：反射 `$object` 的属性 → **自动生成 XML**。
- 重建侧仍复用 `TypeResolver` + `ObjectFactory`。
- 与旧机制的差异：旧机制是 `$obj.save()` 类方法**自己**返回 XML；
  方案二把"结构推导"从类里**搬到 C#**。

> 两者可**并存**：方案二先落地（风险低、兼容旧文件读取），方案一作为终态演进。

---

### 0.4 本项目与官方 CLIXML 的关系

**不是重新实现 CLIXML**，而是补上 CLIXML 不做的一步。

| | 官方 PSSerializer (CLIXML) | 本项目 ObjectRehydrator |
|---|---|---|
| XML ↔ 对象状态 | ✅ 负责 | ✅（借用其架构思路）|
| 类型解析 | ❌ | ✅ `TypeResolver` |
| 真实类型重建 | ❌ 只给 `Deserialized.Xxx` | ✅ `ObjectFactory` |
| 结果 | PSObject snapshot | **真实 PowerShell Class 实例** |
| 依赖 | System.Management.Automation | 同左（`Microsoft.PowerShell.SDK`）|
| 用途 | 通用对象快照 | **类实例持久化 + 还原（无需手写 save）** |

```
XML
 │  InternalDeserializer-like parser
 ▼
对象状态 (propertyName → value)
 │  TypeResolver        ← 名字 → 真实 Type
 ▼
真实 Type
 │  ObjectFactory       ← ctor / DoInit 注入 state（替换官方 Factory）
 ▼
真实 PowerShell Class 实例
```

### 核心价值缺口（为什么官方不够用）

官方文档明确：反序列化对象是**序列化时的 snapshot，不是 live object**。
```powershell
$obj.PSTypeNames   # → Deserialized.Some.Type   ← 不是原始类型
```
且 `InternalDeserializer` **不走** `Type.GetType() → ctor → new Class()`。
所以普通 PS class 无法靠 `Import-Clixml` 还原回真类型 —— 这正是本项目存在的理由。
本项目借用其**对象树解析模型**，只在最后一步换成自己的工厂。

---

## 1. 项目结构

```
PowerShell.ObjectRehydrator/
├── README.md
├── build.sh                          # 构建 + 组装模块目录
├── compile-plan.md                   # 原始 C# 迁移构想（已从 import-xml 迁入）
├── plan/
│   └── plan.md                       # 本文件
├── src/ObjectRehydrator/
│   ├── ObjectRehydrator.csproj       # net8.0 + Microsoft.PowerShell.SDK 7.4.6
│   ├── ObjectRehydrator.psd1         # 模块清单（RootModule = DLL）
│   ├── Cmdlets/
│   │   ├── ImportXmlCommand.cs       # Import-Xml
│   │   ├── SaveXmlObjectCommand.cs   # Save-XmlObject
│   │   ├── SaveXmlInstanceCommand.cs # Save-XmlInstance（Alias: save）★方案二入口
│   │   └── ExportXmlObjectCommand.cs # Export-XmlObject
│   ├── Serialization/
│   │   ├── XElementSerializer.cs     # 移植自 ConvertTo-XElement（已修 bug）
│   │   └── InstanceSerializer.cs     # ★方案二：反射实例 → 自动 XML
│   └── Rehydration/
│       ├── TypeResolver.cs           # 名字 → 真实 Type
│       ├── ObjectFactory.cs          # ctor / DoInit 注入
│       └── ObjectRehydrator.cs       # 核心还原流程
└── tests/
    ├── Example_Instances.xml
    ├── test.class.ps1                # 旧类（手写 save，对照用）
    ├── demo.class.ps1                # ★方案二示例：save() { save $this }
    ├── roundtrip.tests.ps1           # ★方案二回归测试
    └── xml/                          # 各类型样本
```

---

## 2. XML 线格式（wire format）

本项目的 XML 不是 CLIXML，是旧 `import-xml` 的自定义格式。**必须严格兼容**，
否则旧数据文件读不出来。

### 2.1 顶层容器

```xml
<?xml version="1.0" encoding="utf-8"?>
<objects>
  <object name="nestedInstance" id="1"> ... </object>
  <object name="arrayInstance" id="2"> ... </object>
</objects>
```

- 根元素固定 `objects`。
- 每个实例一个 `object`，`name` = **目标类型名**，`id` = 文件内自增序号。

### 2.2 属性节点

```xml
<property name="text">text</property>
```

`name` = 属性名，节点内容 = 值。值分五种：

| 类型 | XML 形态 | 示例 |
|---|---|---|
| 标量 | 纯文本 | `<property name="text">text</property>` |
| 嵌套对象 | `<property>` 下直接再嵌 `<property>` | 见 2.3 |
| hashtable | `<hashtable><key name="..">val</key></hashtable>` | 见 2.4 |
| array | `<array><value index="0">..</value></array>` | 见 2.5 |
| scriptblock | `<scriptblock>Write-Host ".."</scriptblock>` | 见 2.6 |

### 2.3 嵌套对象（nestedInstance）

```xml
<object name="nestedInstance" id="1">
  <property name="testInstance">
    <property name="text">default text</property>
    <property name="name">default name</property>
  </property>
  <property name="text">text</property>
</object>
```
判定规则：**`<property>` 下若存在子 `<property>`，即为嵌套对象**（递归 Rehydrate）。
注意：嵌套子对象的类型名 = 外层的 `name`（旧格式没给子类型单独起名）。

### 2.4 hashtable

```xml
<property name="hash">
  <hashtable>
    <key name="key2">value2</key>
    <key name="key1">value1</key>
  </hashtable>
</property>
```
→ 还原为 `Dictionary[string,object]`（PS 侧即 hashtable）。

### 2.5 array

```xml
<property name="array">
  <array>
    <value index="0">1</value>
    <value index="1">2</value>
    ...
  </array>
</property>
```
→ 还原为 `object[]`。**注意**：`index` 属性是写入顺序标记；样本里值都是字符串 `"1"`，
强类型 `[int[]]` 的属性赋值依赖 PS 侧转换（见 3.4 的 `LanguagePrimitives.ConvertTo`）。

### 2.6 scriptblock

```xml
<property name="scriptblock">
  <scriptblock>Write-Host "Hello, World!"</scriptblock>
</property>
```
→ `ScriptBlock.Create(text)`。

### 2.7 已知畸形样本

`tests/Example_Instances.xml` 里 `arrayInstance` 的 `<object name="arrayobject">`
**类型名写错**（应为 `arrayInstance`）。处理：`TypeResolver` 解析失败 → 降级 PSObject，
**不报错中断**。这本身成为"降级路径"的天然测试用例。

---

## 3. 类约定（来自 tests/test.class.ps1）

旧模块依赖的 PS class 必须满足约定，`ObjectFactory` 才能重建。
下方为**旧约定（对照）**；方案二的**已验证模板**见 §3.1.1。

### 3.1 约定接口（旧）

```powershell
class nestedInstance {
    [testInstance]$testInstance
    [string]$text
    hidden [string]$Path = "$PSScriptRoot/xml/nestedInstance.xml"

    [void] DoInit([pscustomobject]$pscustomobject){
        $pscustomobjectName = (($pscustomobject)|Get-Member -Type NoteProperty).Name
        foreach($propertyName in $pscustomobjectName){
            $this.$propertyName = $pscustomobject.$propertyName
        }
    }
    nestedInstance($object){ $this.doinit($object) }     # ← ctor 接 pscustomobject
    [object] save(){ ... 手写 XElement ... }
}
```

**关键点**：
1. 构造函数 `ClassName($object)` —— `$this.DoInit($object)`。
2. `DoInit([pscustomobject])` —— 把 note 属性逐个赋给 `$this`。
3. `save()` —— 各 class **自己**手写 XML（`XElement`），**不由序列化器统一生成**。
4. `hidden [string]$Path` —— 每个类内嵌自己的默认 XML 路径。

### 3.1.1 方案二已验证模板 ✅（2026-10-04 实测通过）

**核心差异**：`save()` 方法体不再手写 XML，改为一行调用 .NET 静态方法。

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

所有类（DemoHash / DemoArray / DemoScriptBlock）同一模式。

**为何用静态方法而非 cmdlet/别名**：PowerShell class 方法内直接调用外部
cmdlet/别名（`save $this`）**解析不可靠**（实测确认）。故类内固定用
.NET 静态方法 `[InstanceSerializer]::Save($this)`。

### 3.1.2 双入口关系（`save` 的两种用法）

| 入口 | 形式 | 用途 |
|---|---|---|
| **① 类内锚点** | `[InstanceSerializer]::Save($this)` | 序列化某个实例（类方法调用） |
| **② 命令行/交互** | `save` 别名（→ `Save-XmlInstance`） | 手动调用，如 `$obj \| save` |

两者底层都走同一实现（`InstanceSerializer.Save`），**行为一致**。
psd1 同时导出 `Save-XmlInstance` cmdlet 与 `save` 别名。

### 3.2 对 ObjectFactory 的含义

工厂**不需要**知道属性顺序，只要：
1. 把解析出的状态包成 `PSObject`；
2. 优先调用 `ctor(PSObject/object)`；
3. 否则无参构造 + `DoInit(PSObject/object)`；
4. 再否则逐属性反射赋值。

### 3.3 序列化端的一个现实

旧实现的序列化是**两套并存**：
- `save-object` 调用对象自身的 `save()`（class 手写 XML）；
- `ConvertTo-XElement` 是通用转换器（新加的，给 `shred`/嵌套用）。

→ C# 端 `XElementSerializer` 覆盖通用转换；`Save-XmlObject` 由工厂侧统一用
`XElementSerializer` 生成 `object` 节点（**不再依赖 class 自己的 save()**），
但需保证输出与旧 `save()` 格式一致（见 2.x 各形态）。

### 3.4 类型强转（重建时）

`[int[]]$array` 这类强类型属性，直接把字符串数组塞进去会失败。
→ `DoInit` 里 `$this.$name = $pscustomobject.$name` 依赖 PS 隐式转换；
C# 侧若走"逐属性赋值"路径，需用 `LanguagePrimitives.ConvertTo(value, prop.PropertyType)`。

---

## 4. 关键设计决策

### 4.1 目标框架与依赖
- **net8.0 + `Microsoft.PowerShell.SDK` 7.4.6**（`PrivateAssets=none`）。
- 产出的 DLL 依赖 `System.Management.Automation.dll`，**只能在 pwsh 进程内 Import**（正是需求）。
- 备选"零依赖 netstandard2.0"无法使用 PS 类型，不满足需求 → **否决**。
- publish 时需把 SDK 依赖 DLL 一并拷入模块目录（`build.sh` 已处理）。

### 4.2 对象状态解析（`ObjectRehydrator.ParseState`）
参照 `InternalDeserializer` 的对象树模型，按 2.x 规则递归：
```
ParseState(objectElement)
  └─ for each <property>:
       name = @name
       value = ParsePropertyValue(prop):
          if prop 有子 <property>   → 递归 Rehydrate(合成 object 节点)
          elif prop 有 <hashtable>  → Dictionary
          elif prop 有 <array>      → object[]
          elif prop 有 <scriptblock>→ ScriptBlock
          else                      → prop.Value (string)
       result.Properties.Add(PSNoteProperty(name, value))
```

### 4.3 类型解析（`TypeResolver`）
1. 优先在当前 **PowerShell 会话**解析（脚本 class）：
   `[PSTypeName]::new($name).Type`，通过 `SessionState.InvokeCommand`。
2. 回退 .NET：`Type.GetType` → 扫 `AppDomain` 已加载程序集。
3. 失败 → **返回 null → 降级 PSObject**（保持官方语义，不抛异常中断）。

### 4.4 工厂重建（`ObjectFactory`）—— 核心增值
优先级：
1. `ctor(PSObject | object)` —— PS class 约定（构造 → DoInit）。
2. 无参构造 + `DoInit(PSObject | object)`。
3. 无参构造 + 逐 public settable 属性赋值（必要时 `LanguagePrimitives.ConvertTo`）。
4. 全失败 → `null`（调用方降级 PSObject）。

### 4.5 命名冲突（写入计划）

**问题**：核心类名 `ObjectRehydrator` 与命名空间尾段 `PowerShell.ObjectRehydrator` 同名。

```csharp
namespace PowerShell.ObjectRehydrator.Rehydration
{
    public sealed class ObjectRehydrator { ... }   // ← 类名 == 命名空间尾段
}
```

**当前规避**：cmdlet 内用 `Rehydration.ObjectRehydrator` 全限定名。
**决策**：写进本计划 —— 若首次编译产生歧义/告警，把类**改名 `RehydrateEngine`**
（`Rehydration/RehydrateEngine.cs`），并同步更新所有引用点。

### 4.6 已修复的原实现硬伤
| # | 原问题（import-xml） | 处理 |
|---|---|---|
| 1 | `ConvertTo-XElement` 里 `(if ($item -ne $null){...})` 表达式，仅 PS7+ 合法 | C# 侧显式 `item != null` 判断 |
| 2 | `rehydrate.ps1` 变量 `$intializePsobject` / `$intializepsobject` 拼写不一致 | 统一命名 |
| 3 | `Example_Instances.xml` 中 `arrayobject` 类型名与类名不符 | 标记为降级测试样本 |
| 4 | `ConvertTo-XElement` 分支 `$item.PSObject -ne $null` 恒真 | 改用类型判定 `IsComplex()` |

---

## 5. Cmdlet / 函数契约

### 5.0 全局 `save` 函数（方案二核心）

| 项 | 值 |
|---|---|
| 签名 | `save($object)` （从 C# 导出为 PS 函数/cmdlet） |
| 输入 | `$object` —— 类实例（经锚点方法 `save $this` 传入） |
| 行为 | 反射 `$object` 的属性 → 自动生成 `<object name=类型名>` XML 片段 |
| 输出 | XML 字符串（供 `save-object` 写入），或直接写文件 |
| 类契约 | 类须含 `[object] save() { save $this }` 锚点（内容恒定） |

> 与旧 `save-object` 分工：
> - **C# `save($object)`** = **结构层**（属性 → XML 片段），自动化。
> - **`save-object`** = **文件层**（建文件 / 算 id / append），复用旧逻辑。

### 5.1 Cmdlet 表


| Cmdlet | 参数 | 行为 | 旧对应 |
|---|---|---|---|
| `Import-Xml` | `-Path` (Mandatory, Pos0, pipeline) | 读文件，逐 `<object>` 还原，输出实例 | `Import-Xml` |
| `Save-XmlObject` | `-InputObject` (Mandatory, pipeline), `-Path` | 序列化单对象，**追加**到 `<objects>` | `save-object` |
| `Export-XmlObject` | `-InputObject[]` (Mandatory, pipeline), `-Path` (Pos1) | **覆盖**导出整批 | `export-object` |

命名注意：`Import-Xml` 可能与旧模块 **cmdlet 名冲突** → 若冲突，改前缀（如 `Import-ObjXml`）。
`Save-XmlObject` / `Export-XmlObject` 中 `Path` 缺省时，回退读对象的 `.Path` 属性（兼容旧约定）。

---

## 6. 实施步骤

### Step 0 — 环境（**阻塞**）
- [ ] 安装 .NET 8 SDK（build 机）
- [ ] 安装 PowerShell 7.4（Import 验证机）
- ⚠️ **本机（Debian 12 容器）不装 SDK**（用户明确）；构建在有 SDK 的机器执行 `./build.sh`

### Step 1 — 骨架 ✅（2026-10-04 完成）
- [x] 目录结构
- [x] `.csproj` / `.psd1`
- [x] 3 个 cmdlet
- [x] `XElementSerializer` / `TypeResolver` / `ObjectFactory` / `ObjectRehydrator`
- [x] `build.sh`
- [x] 测试样本拷入

### Step 2 — 首次构建 & 编译修复
- [ ] 在 SDK 机器执行 `./build.sh`
- [ ] 修复编译错误。**首要预判**：类 `ObjectRehydrator` 与命名空间尾段重名 →
      cmdlet 内已用 `Rehydration.ObjectRehydrator` 全限定规避；若仍告警，改类名 `RehydrateEngine`
- [ ] `dist/ObjectRehydrator/` 产出 DLL + 依赖 + psd1

### Step 3 — pwsh 内 Import 冒烟测试 ✅（2026-10-04）
```powershell
Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1
Get-Command Import-Xml, Save-XmlObject, Export-XmlObject, Save-XmlInstance
. ./tests/test.class.ps1
$o = Import-Xml -Path ./tests/xml/nestedInstance.xml
$o -is [nestedInstance]      # 期望 True
```

### Step 2 — 首次构建 & 编译修复 ✅（2026-10-04）
- [x] pwsh 容器安装 .NET 8 SDK (8.0.425)，`./build.sh` 构建成功
- [x] 修复 4 个编译错误（见 §7.1）
- [x] `dist/ObjectRehydrator/` 产出 DLL + 依赖 + psd1

### Step 4 — 回归测试（对照 import-xml 原行为）✅（2026-10-04）
用 `tests/xml/*.xml`：

| 样本 | 结果 |
|---|---|
| `nestedInstance.xml` | ✅ `-is [nestedInstance]`=True; text=text; testInstance.text=default text |
| `array.xml` | ✅ 3×arrayInstance, `-is [arrayInstance]`=True |
| `hashobject.xml` | ✅ 3×hashtableInstance + 1×PSCustomObject（降级，类型名不匹配）|
| `scriptblockInstance.xml` | ✅ 3×scriptblockInstance, scriptblock type=ScriptBlock |
| `Example_Instances.xml` | ✅ 降级不报错 |

- [x] 全部样本还原成真实 class 实例（适用时）
- [x] 降级路径（类型名不匹配）实测不报错
- [x] `Save-XmlObject` → `Import-Xml` 往返一致（roundtrip 6/6）

### Step 5 — 方案二：自动化 `save`（消除手写结构）★ ✅
- [x] C# `InstanceSerializer`：反射属性 → 自动生成 `<object>` XML（入口为实例）
- [x] `Save-XmlInstance` cmdlet + `save` 别名（psd1 导出）
- [x] 递归展开嵌套类属性；hashtable / array / scriptblock 分支
- [x] 排除 `hidden` 属性与约定 `$Path`
- [x] 示例类 `tests/demo.class.ps1`（改用 `[InstanceSerializer]::Save($this)`）
- [x] 回归测试 `tests/roundtrip.tests.ps1` —— **6/6 通过**
- [x] 验证：加/删/改属性后**不改类代码**，序列化自动跟随

### Step 6 — 方案一：纯 XML 自解析（终态）★ 待推进

**目标**：类**零代码** —— 连 `save(){}` 锚点都不需要。像 `Import-Clixml` 一样，
XML 自描述 → C# 自解析 → 重建。

#### 6.1 核心前提：XML 必须自描述

现格式（§2）只存 value，**丢了类型**：
- `text` 不知是 `[string]` 还是 `[int]`；
- 嵌套对象不知子类型是谁；
- 数组不知元素类型。

→ 方案一要成立，**内层 XML 必须携带类型元数据**。设计目标：

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

**新增 `type` 属性**（每层 `object` / 嵌套 `property` 都标类型）。
内部仍复用 §2 的 hashtable/array/scriptblock 形态（各自也加类型标记）。

#### 6.2 两类实现路径

| 路径 | 做法 | 依赖 |
|---|---|---|
| **A2 复用 CLIXML** | ❌ **实测否决**（2026-10-04）：`PSSerializer.Serialize` 对脚本 class 的**嵌套对象不展开** —— `inner` 被当成 `ToString()` 字符串（`<S>DemoInner</S>`）；且反序列化只得 `Deserialized.DemoNested`（PSObject）。CLIXML 底座不够用。 | ❌ 否决 |
| **A1 自有格式 + type** ✅ | 在我们**已有且已测**的 `InstanceSerializer`（能正确递归嵌套）上**加 `type` 元数据**，反序列侧消费。 | ✅ 选定 |

> **关键**：由于 `InstanceSerializer` 的递归已由 roundtrip 6/6 验证，
> 方案一 ≈ **方案二序列化器 + type 属性 + 无锚点外部入口**，风险大幅降低。

**PSSerializer 实验记录（2026-10-04）**：
```xml
<Objs Version="1.1.0.1" xmlns=".../2004/04">
  <Obj RefId="0">
    <TN RefId="0"><T>DemoNested</T><T>System.Object</T></TN>
    <ToString>DemoNested</ToString>
    <Props>
      <S N="inner">DemoInner</S>   <!-- ❌ 嵌套对象未展开 -->
      <S N="text">hello</S>
    </Props>
  </Obj>
</Objs>
```
→ 类型名保留（`<T>DemoNested</T>`），但**嵌套未递归**、**反序列得 PSObject**。

#### 6.2.1 A1 为何相对安全

`InstanceSerializer` 已能正确递归嵌套 / hashtable / array / scriptblock
（roundtrip 6/6）。方案一只需在此基础上：
1. 每个元素多写一个 `type` 属性；
2. 反序列侧读 `type` 路由到 `TypeResolver`；
3. 序列化入口由类锁点改为**外部命令**（§6.5）。

#### 6.3 关键挑战

1. **类型名与值的映射**：C# 侧拿到 `System.String` / `DemoInner` → `TypeResolver` 解析。
   脚本 class（`DemoInner`）需会话可见（同 §7.1）。
2. **嵌套类型名歧义**：旧格式嵌套子对象**Type名继承外层**（§2.3）；自描述后需
   每层真实类型 → 序列化时必须写对。
3. **强类型赋值**：仍需 `LanguagePrimitives.ConvertTo(value, propType)`（§3.4）。
4. ~~**兼容性**~~ → **已拍板（2026-10-07）：完全换代**，旧 `import-xml` XML 不支持。

#### 6.3.1 用户已锁定决策（2026-10-07）★

| # | 决策 | 结论 |
|---|---|---|
| 1 | XML 兼容策略 | **完全换代**（旧文件不支持）|
| 2 | 属性类型范围 | **什么类型都有**（含 `[object]`/弱类型）|
| 3 | int/string 歧义解法 | **是方案一能否成立的判据**；不行则退回方案二 |
| 4 | 最终目标 | **能全自动 import-xml 即可**（= 类零代码）|
| 5 | XML 的定位 | **程序产物**（非人写数据源，是程序生成/消费的中间格式）|

**决策推导（关键洞察）**：
决策 5 + 决策 1 一起，**直接消灭 int/string 歧义问题**：
> XML 既是本程序生成（写时 C# 反射已知真实类型 → 写 `type`），
> 又是本程序消费（读 `type` 精确还原）→ **零推断、零歧义**。

歧义只存在于“拿到别人手写的旧格式 XML 去猜类型”这一旧场景 —— 该场景已被
决策 1（完全换代）排除。故 `"123"` 是 string 还是 int **无需猜测**，
`type="System.String"` 直接写在元素上。

**结论**：`type` 粒度采用 **方案 X（每层都标）**（决策 2 要求；方案 Y 的推断在弱类型字段必翻车）。

#### 6.3.2 唯一真正的成败点 —— Go/No-Go 实验 ★

int/string **已不是难点**。真正决定方案一能否实现的是：

> **脚本 class 的类型可见性与实例化**：
> `TypeResolver` 要按类型名 `new` 出对象，但 PowerShell 脚本 class
> （`class DemoNested {...}`）**不是 CLR 类型**，C# 无法直接 `Activator.CreateInstance`。
> 旧机制靠 `constructor($object)` 约定；方案一“零代码”要求**连构造函数都不能强制**。

**两条实现路径**：
- **A1a**：C# 侧经 PowerShell API（`[type]` 字面量 / `New-Object` / `InvokeCommand`）创建
  脚本 class 实例 → 满足零代码，但可能更慢。
- **A1b**：要求类保留一个极简约定（非 `save()`）→ **违背零代码**，仅作兜底。

**Go/No-Go 判据**：
| 结果 | 动作 |
|---|---|
| C# 能根据字符串类型名实例化**无任何约定的**脚本 class | ✅ 方案一成立，执行 §6.4 |
| 不能 | ❌ **Fallback：采用方案二**（已闭环，直接收工）|

> 用户明确：“确定，如果不能则 fallback，我来测试。” → 实验由**宿主侧（Qwen Code）**执行，
> 用户在 pwsh 容器内亲自验证。

**✅ 实验结论：GO（2026-10-07）**

7 步全通过：
1. 脚本 class 定义（零代码，无构造函数）✅
2. 类型解析 ✅
3. 构造函数检查（自动有无参 public ctor）✅
4. `Activator.CreateInstance()` 成功创建真实实例 ✅
5. Reflection 设属性（string/int/double）✅
6. `-is [DemoZeroCode] = True` ✅
7. 嵌套类（DemoNested + DemoInner）全通过 ✅

**关键实测发现（决定实现细节）**：
| 路径 | 方法 | 结果 | 采纳 |
|---|---|---|---|
| **A** | `new PSTypeName(name).Type` | ❌ 返回 null（不可靠） | 不用 |
| **B** | `ps.AddScript("[ClassName]")` via PowerShell.Invoke → `BaseObject as Type` | ✅ **最可靠** | **首选** |
| C | `Type.GetType(name)` | ❌ null（预期） | 不用 |
| **D** | `AppDomain.CurrentDomain.GetAssemblies()` 扫描 | ✅ 可行（备选） | fallback |

- Assembly = `PowerShell Class Assembly`（动态程序集）
- 无参构造函数**自动存在**
- 嵌套类（引用其他脚本 class）也能正确解析 + 实例化

> ⚠️ **对现有代码的影响**：`src/ObjectRehydrator/Rehydration/TypeResolver.cs` 当前实现
> 用的正是 **路径 A**（`PSTypeName`）→ **必须改用路径 B**。

PoC 代码：`tests/poc/Program.cs`（可复现）

#### 6.4 任务拆分（A1）★ ✅ 已完成（2026-10-07）

**阶段一：TypeResolver 重构（基础）**
- [x] **6.4.0 ✅ Go/No-Go 实验**（2026-10-07 GO，7 步全通过，PoC 在 `tests/poc/Program.cs`）
- [x] **6.4.1 ✅ TypeResolver 改路径 B** ★
  - `PSTypeName`（路径 A，实测 null）→ `PowerShell.Create()` + `AddScript("[ClassName]")`（路径 B）
  - 构造函数改为 `TypeResolver(Runspace runspace)`；保 B→D→CLR 三级 fallback

**阶段二：序列化端（写 type）**
- [x] **6.4.2 ✅ `InstanceSerializer` + `WithTypeInfo`** → 每层标 `type`（方案 X）
  - `<object name type>`；每个 `<property name type>`
  - 类型来源：反射实例属性的声明类型（`realType.GetProperties` → `PropertyType.FullName`）
  - hashtable / array / scriptblock 形态各自带 `type`

**阶段三：反序列化端（读 type）**
- [x] **6.4.3 ✅ `ObjectRehydrator` 读 `type`**
  - `Attribute("type") ?? Attribute("name")`（优先 type，回退 name）
  - `Activator.CreateInstance` + Reflection 逐属性赋值
  - 标量：`LanguagePrimitives.TryConvertTo(value, targetType, out converted)`
  - 嵌套：递归重建（含 `nestedType`）

**阶段四：外部入口**
- [x] **6.4.4 ✅ `Export-XmlObject $obj`** → 直接 `InstanceSerializer`，不依赖 `$obj.save()`
  - 又改：`ExportXmlObjectCommand.cs` / `SaveXmlObjectCommand.cs` / `ImportXmlCommand.cs`（均 2026-10-07 20:34-20:35）

**阶段五：验证**
- [x] **6.4.5 ✅ 类零代码验证**：无 `save()` / 无构造函数 / 无 `DoInit` → `-is [DemoNested]` = True，`Inner -is [DemoInner]` = True，`Count` = 3 (Int32)
- [x] **6.4.6 ✅ 全类型回归 14/14**：Hashtable 3/3 · Array 3/3 · ScriptBlock 3/3 · 深层嵌套（TestDeep→TestHash+TestArray）5/5
- [ ] 6.4.7 与旧 `import-xml` 输出格式对比（确认 XML 结构差异，旧格式明确不兼容）

**实际修复的 3 个冲突点**：
1. ✅ TypeResolver：`PSTypeName`（失效）→ `PowerShell.Invoke`（可靠）
2. ✅ ParseState：只读 `name` → 优先读 `type`，回退 `name`
3. ✅ ObjectFactory：依赖 ctor/DoInit → `Activator.CreateInstance` + Reflection 直接赋值

**验收结果（2026-10-07）**：方案一核心价值已证明 —— **用户零代码**（无 save/ctor/DoInit），
`Export-XmlObject` 序列化 + `Import-Xml` 重建真实类实例。

**验收判据（方案一完成）**：
1. 零代码类可被 `Import-Xml` 重建为**真实类型实例**（`-is [ClassName]` = True）
2. 所有属性类型（string/int/double/嵌套 class/array/hashtable/scriptblock）往返正确
3. 序列化不需要类提供任何方法
4. 嵌套类引用正确解析

#### 6.5 一个待澄清的设计点 🔸（已确认 ✅）

“类零代码”下，序列化入口不再是 `$obj.save()`（类方法）。→ 改为**外部 cmdlet**
（`Export-XmlObject $obj`）。
**已与 Qwen Code 确认：接受** —— 这正是方案一“类零代码”的核心价值。

→ 方案一：序列化**不依赖类任何方法**，全部由外部命令驱动。
入口候选：现有 `Export-XmlObject` / `Save-XmlObject` cmdlet。

### Step 7 — 打包与集成
- [ ] `dotnet publish -c Release`（如需单文件/自包含）
- [ ] 集成 DRE：替换 `dre/modules/import-xml` 的调用，或并行双跑对比
- [ ] 决定是否把旧模块标记 deprecated

---

## 7. 待确认 / 风险

1. **PS class 在函数体内定义时不可见**：`TypeResolver` 依赖会话可见性；脚本 class 必须在
   Import 前加载（`using module` 或 dot-source）。→ Step 3 验证。
2. **`ObjectRehydrator` 命名冲突**：类名 == 命名空间尾段。当前用全限定位规避。（编译已过，暂无影响；若后续告警再改名 `RehydrateEngine`。）
3. ✅ **已解决 —— 类内调用 cmdlet**：PowerShell class 方法内直接调用外部 cmdlet/别名
   （`save $this`）解析不可靠。**实测确认存在**。
   **解决方案**：类内改用 .NET 静态方法 `[InstanceSerializer]::Save($this)`。
   `tests/demo.class.ps1` 已更新，6/6 测试通过。→ 已从风险转为既定方案。
4. **SDK 版本对齐**：DLL 用的 SDK 7.4.6 必须与运行它的 pwsh 7.4.x 兼容；
   跨大版本（如 pwsh 7.2）可能加载失败。（构建环境 .NET 8.0.425 / pwsh 容器已通）
5. ✅ **已解决 —— 无 SDK 环境**：已在 pwsh 容器安装 .NET 8 SDK (8.0.425)，`build.sh` 构建成功。
6. **cmdlet 名冲突**：`Import-Xml` 可能与旧模块撞名 → **搁置（待定）**，用户明确暂不处理。
   plan 里先保留原名，必要时换前缀（如 `Import-ObjXml`）。
7. **序列化端格式保真**：C# 统一生成 `object` 节点，需与旧 class `save()` 输出逐字节兼容，
   否则旧文件/新文件互不认。→ roundtrip 已过 6/6；Step 4 补全样本覆盖。
8. **强类型属性**：`[int[]]` 等需 `LanguagePrimitives.ConvertTo`，否则赋值失败。

### 7.1 编译期已修复的问题（2026-10-04，宿主侧）

| # | 文件:行 | 问题 | 修复 |
|---|---|---|---|
| 1 | `SaveXmlObjectCommand.cs:51` | `??` 与 `?:` 运算符优先级 | 加括号 |
| 2 | `ObjectRehydrator.cs:82` | 缺 `using System.Linq` | 补 using |
| 3 | `InstanceSerializer.cs:76` | `PSPropertyInfo` 无 `IsHidden` | 改属性/成员判断 |
| 4 | `ExportXmlObjectCommand.cs:52` | `Count()` 需 LINQ | 补 `using System.Linq` |

---

## 8. 变更记录

- 2026-10-04：项目建立；从 `import-xml` 迁入 `compile-plan.md`（原处保留说明）；
  完成 Step 1 骨架；补全 plan.md 细节（XML 线格式、类约定、cmdlet 契约、风险）。
- 2026-10-04：**GOAL 澄清** —— 目标升级为"从 C# 底层自动解析重建类，免手写 save"；
  新增 §0.1~0.4 目标与双方案；新增 §4.5 命名冲突决策；新增 §5.0 全局 save 契约；
  Step 5/6 展开方案二/方案一。cmdlet 撞名（冲突①）搁置。
- 2026-10-04：**方案二落地（代码）** —— `InstanceSerializer.cs` + `Save-XmlInstance` cmdlet
  （Alias `save`）；示例类 `demo.class.ps1`；回归 `roundtrip.tests.ps1`。待 SDK 机器编译验证。
- 2026-10-04：**构建+测试通过** —— pwsh 容器装 .NET 8 SDK 8.0.425，`build.sh` 成功；
  修 4 个编译错误（§7.1）；roundtrip **6/6 通过**；类内 cmdlet 风险改用
  `[InstanceSerializer]::Save($this)` 解决（§7.3）。Step 2/3/5 完成。
- 2026-10-04：**Step 4 回归通过** —— 全样本实测（nestedInstance/array/hashobject/
  scriptblockInstance 还原真实类实例；Example_Instances 降级不报错）；
  确定双入口关系（§3.1.2）；固化方案二已验证模板（§3.1.1）。Step 4 完成。
- 2026-10-04：**Step 6（方案一）设计展开** —— 自描述 XML（加 `type` 元数据）、
  **A2 实测否决**（PSSerializer 对脚本类嵌套不展开）→ **选定 A1**；
  关键挑战、任务拆分、序列化外部化已确认（§6.5）。

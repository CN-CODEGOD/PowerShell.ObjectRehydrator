# PowerShell.ObjectRehydrator

把 PowerShell 类实例序列化为 XML，并**还原为真实类型的实例**（rehydrate）。

> [!IMPORTANT]
> 官方 CLIXML（`Export-Clixml` / `Import-Clixml`）反序列化后只会给你一个
> `PSObject` 快照，类型名变成 `Deserialized.Xxx`，**永远不会执行你的构造函数**。
>
> 本项目补上的正是这最后一步：解析出对象状态后，通过反射找到真实类型，
> 调用 `ctor` / `DoInit()` / 属性赋值，重建**真正的 PowerShell Class 实例**。

```powershell
$obj = Import-Xml -Path ./tests/xml/road.xml
$obj -is [roadcoordinate]      # Import-Clixml 做不到，这里 → True
```

## 为什么需要它

如果一个对象要跨进程、跨会话、写进配置文件再读回来，通常只有两条路：

| 方案 | 问题 |
|---|---|
| `Export-Clixml` / `Import-Clixml` | 只能拿回 `PSObject` 快照，方法全丢，`-is` 判断失败 |
| 每个类手写 `save()` / `load()` | 加一个属性就得同步改一遍，极易与类结构不一致 |

本项目要的是第三条路：**用户零额外代码，类结构由 C# 侧反射自动推导。**

## 架构

```
XML → [ObjectRehydrator.ParseState] → 对象状态
        → [TypeResolver]      → 真实 Type（优先当前会话的 PS class）
        → [ObjectFactory]     → ctor / DoInit / 属性赋值
                              → 真实 Class 实例
```

核心组件：

- `Serialization/XElementSerializer` — `ConvertTo-XElement` 移植（已修 3 处硬伤）
- `Serialization/InstanceSerializer` — 反射实例 → 自动生成 XML，**无需手写结构**
- `Rehydration/TypeResolver` — 类型名 → 真实 `Type`
- `Rehydration/ObjectFactory` — 构造真实实例
- `Rehydration/ObjectRehydrator` — 串联全流程

## 导出的命令

| 命令 | 作用 |
|---|---|
| `Import-Xml` | XML → 真实类型实例 |
| `Save-XmlObject` | 任意对象 → XML |
| `Export-XmlObject` | 批量导出到 XML |
| `Save-XmlInstance`（别名 `save`） | 反射实例 → XML，方案二入口 |

## 当前状态

方案二（类保留一行锚点，结构推导自动化）**已实现并通过回归测试**：

```
✅ C# 骨架 + 构建
✅ Import-Module 冒烟
✅ 对照 import-xml 的回归测试
✅ InstanceSerializer 自动序列化      6/6 测试通过
```

已支持的类型：

```
nestedInstance   enum        array       int
string           BigInteger  Decimal     Double
```

**尚未支持的类型**都已拆成可直接认领的 issue，见
[Issues](https://github.com/CN-CODEGOD/PowerShell.ObjectRehydrator/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)：

```
Vector3   Dictionary<K,V>   List<T>   Queue<T>   HashSet<T>
PSCustomObject   $null   DateTime   Guid   Version   Char / Bool   系统 Enum
```

> 想上手？挑一个类型 issue，跑通 `tests/test_script.ps1` 输出 `True` 就算完成。

## 构建

### 方式一：本机 .NET SDK 8.0+

```bash
./build.sh            # Release 构建 + 组装到 dist/ObjectRehydrator/
./build.sh Debug      # Debug 构建
```

### 方式二：Docker（不需要装 SDK）

```bash
./build.docker.sh     # 在容器里构建，产物落在 dist/ObjectRehydrator/
```

两种方式产物一致，都是可直接 `Import-Module` 的模块目录。

## 使用

```powershell
Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1

# 还原为真实类型
$objs = Import-Xml -Path ./tests/xml/nestedInstance.xml
$objs[0] -is [nestedInstance]      # → True

# 保存单个对象
Save-XmlObject -InputObject $instance -Path ./out.xml

# 批量导出
Export-XmlObject -InputObject $instances -Path ./out.xml

# 方案二：类里写一行锚点，结构自动推导
class demo {
    [string]$name
    [object]save() { [InstanceSerializer]::Save($this) }
}
```

## 测试

```powershell
pwsh -NoProfile -File ./tests/roundtrip.tests.ps1    # 往返回归
./tests/test_script.ps1 -path ./tests/xml/test.xml -inputObject $obj
```

## 目录

```
src/ObjectRehydrator/     C# 源码（Cmdlets / Serialization / Rehydration）
tests/                    XML 样本、测试类、往返测试
plan/plan.md              实施计划（两条技术路线与目标）
plan/progress-report.md   工作总结（Step 0-5 完成情况）
compile-plan.md           原始迁移构想
```

## 已知限制

- 产物依赖 `System.Management.Automation.dll`，必须由 `pwsh` 进程内 `Import-Module`
- 方案一（纯 XML 自解析、用户零代码）仍在设计中，见 `plan/plan.md`
- 类必须在**还原时所在会话**中已定义，`TypeResolver` 才能找到它

## License

MIT

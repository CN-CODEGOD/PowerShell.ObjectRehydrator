# PowerShell.ObjectRehydrator

把 PowerShell 类实例序列化为 XML，并**还原为真实类型实例**（rehydrate）的二进制 PowerShell 模块。

## 与官方 CLIXML 的区别

官方 `PSSerializer`（`Export-Clixml` / `Import-Clixml`）反序列化后只得到
`PSObject` + `Deserialized.Xxx` 类型名，**不是**原始 class 实例。

本项目借用官方的对象树解析思路，但**替换了最后的 Object Factory**，
在解析出对象状态后：

1. `TypeResolver` — 把名字解析为真实 Type（优先当前会话的 PS class）
2. `ObjectFactory` — 通过 `ctor(pscustomobject)` / `DoInit()` / 属性赋值重建实例

→ 得到**真实的 PowerShell Class 实例**。

## 架构

```
XML → [ObjectRehydrator.ParseState] → 对象状态
        → [TypeResolver] → 真实 Type
        → [ObjectFactory] → 真实实例
```

## 构建

需要 .NET SDK 8.0+：

```bash
./build.sh          # Release 构建 + 组装到 dist/ObjectRehydrator/
```

（本仓库所在环境未安装 SDK；构建请在有 SDK 的机器执行。）

## 使用

```powershell
Import-Module ./dist/ObjectRehydrator/ObjectRehydrator.psd1

# 还原
$objs = Import-Xml -Path ./tests/xml/nestedInstance.xml
$objs[0] -is [nestedInstance]   # → True

# 保存
Save-XmlObject -InputObject $instance -Path ./out.xml

# 批量导出
Export-XmlObject -InputObject $instances -Path ./out.xml
```

## 目录

- `src/ObjectRehydrator/` — C# 源码
- `tests/` — XML 样本 + 测试类
- `plan/plan.md` — 实施计划
- `compile-plan.md` — 原始迁移构想

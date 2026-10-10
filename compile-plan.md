# 编译计划 - PowerShell SDK 方向

## 背景

当前 DRE 项目架构：
- `dre.py` (Python) — 大脑/调度
- `workflows.ps1` (PowerShell) — 执行层
- 两者未打通

## PowerShell SDK 简介

> 让 C#/.NET 程序能够"内嵌并控制 PowerShell"的开发工具包。

它是一套 **.NET API**，不是新的 PowerShell 编译器。

### 核心能力

```
C# 程序
  ↓
PowerShell SDK (System.Management.Automation)
  ↓
PowerShell Engine
  ↓
执行 PowerShell 命令
  ↓
把结果返回给 C#
```

### 示例代码

```csharp
using System.Management.Automation;

using var ps = PowerShell.Create();
ps.AddCommand("Get-Process");
var results = ps.Invoke();

foreach (var result in results)
{
    Console.WriteLine(result);
}
```

## 与 PS2EXE 的区别

| | PS2EXE | PowerShell SDK |
|---|--------|----------------|
| 目的 | 把 `.ps1` 打包成 `.exe` | 在 .NET 程序里运行 PowerShell |
| 主语言 | PowerShell | C# / .NET |
| C# 调用 PowerShell | ❌ | ✅ |
| PowerShell 调用 C# | 可以通过 DLL | ✅ |
| 适合开发复杂程序 | 一般 | **非常适合** |

## 目标架构（未来 Windows 程序）

```
┌─────────────────────────────────────────┐
│  C# EXE                                 │
│                                         │
│  ┌─────────────────────────────────┐   │
│  │ Workflow / Agent                │   │
│  │                                 │   │
│  │  - GUI                          │   │
│  │  - 工作流调度                    │   │
│  │  - 参数解析                      │   │
│  │  - 结果判断                      │   │
│  └─────────────┬───────────────────┘   │
│                │                        │
│  ┌─────────────▼───────────────────┐   │
│  │ PowerShell SDK                  │   │
│  │                                 │   │
│  │  - 内嵌 PowerShell 引擎          │   │
│  │  - 执行 .ps1 / 命令              │   │
│  │  - 对象转换                      │   │
│  └─────────────┬───────────────────┘   │
│                │                        │
│  ┌─────────────▼───────────────────┐   │
│  │ 系统 API                        │   │
│  │                                 │   │
│  │  - AMD SMI / ADLX DLL          │   │
│  │  - Windows API                  │   │
│  │  - GPU 查询 / 文件操作           │   │
│  └─────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

### 分工

**C# 负责：**
- GUI（WPF / WinForms / MAUI）
- 工作流调度
- 参数解析
- 结果判断
- 异常处理 / fallback

**PowerShell 负责：**
- `Get-CimInstance` / `Get-WmiObject`
- 系统管理
- GPU 查询
- 文件操作

**底层（直接调用）：**
- C# → ADLX DLL
- C# → AMD SMI
- C# → Windows API

## 实施步骤

### Phase 1：当前项目继续完善（Python + PowerShell）

1. **打通 dre.py ↔ workflows.ps1**
   - dre.py Layer 3 改成调用 `pwsh -c "workflow.Execute()"`
   - 或者用 subprocess 调用 PowerShell 脚本

2. **check_agent 接入 checkResult.py**
   - 关键词匹配 → LLM 语义判断

3. **实现 fallback 链**
   - 失败后尝试下一个 workflow 或 LLM 兜底

### Phase 2：迁移到 C# + PowerShell SDK

1. **创建 C# 项目**
   ```
   dre-csharp/
   ├── Dre.App/           # WPF/MAUI GUI
   ├── Dre.Core/          # 工作流调度、参数解析
   ├── Dre.PowerShell/    # PowerShell SDK 封装
   └── Dre.Workflows/     # workflow 定义（从 PS1 迁移）
   ```

2. **PowerShell SDK 封装层**
   ```csharp
   // Dre.PowerShell/PowerShellHost.cs
   public class PowerShellHost
   {
       public async Task<PSObject[]> ExecuteAsync(string script)
       {
           using var ps = PowerShell.Create();
           ps.AddScript(script);
           return await Task.Run(() => ps.Invoke());
       }
       
       public async Task<T> ExecuteAsync<T>(string script)
       {
           var results = await ExecuteAsync(script);
           // 转换 PSObject → C# 对象
       }
   }
   ```

3. **Workflow 迁移**
   ```csharp
   // Dre.Workflows/GpuQueryWorkflow.cs
   public class GpuQueryWorkflow : IWorkflow
   {
       public string Name => "gpu_query";
       public string Description => "查询 GPU 状态";
       
       public async Task<WorkflowResult> ExecuteAsync(string input)
       {
           var psHost = new PowerShellHost();
           var results = await psHost.ExecuteAsync(@"
               $gpu = Get-CimInstance Win32_VideoController
               $gpu | Select-Object Name, DriverVersion, AdapterRAM
           ");
           return new WorkflowResult(results);
       }
   }
   ```

4. **check_agent 实现**
   ```csharp
   public bool CheckAgent(string userInput)
   {
       // 1. 关键词匹配（快速）
       // 2. LLM 语义判断（准确）
       using var ps = PowerShell.Create();
       ps.AddCommand("Invoke-CheckAgent")
         .AddParameter("Input", userInput)
         .AddParameter("Workflow", this.Name);
       var result = ps.Invoke();
       return result.First().BaseObject is bool b && b;
   }
   ```

### Phase 3：打包发布

1. **单文件 EXE**
   ```
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```

2. **包含 PowerShell 运行时**
   - .NET 6+ 自带 PowerShell SDK
   - 不需要用户安装 PowerShell

## 优势

1. **正规 Windows 程序** — 有 GUI、有图标、有安装程序
2. **性能更好** — C# 编译为原生代码
3. **类型安全** — 编译时检查
4. **易于分发** — 单个 EXE，无需依赖
5. **保持 PowerShell 生态** — 系统管理命令继续用 PS

## 待确认

1. **是否现在就开始 C# 迁移？** 还是先把 Python 版本跑通？
2. **GUI 框架选择？** WPF / WinForms / MAUI / Avalonia？
3. **是否需要跨平台？** 如果只 Windows，C# + WPF 最简单。

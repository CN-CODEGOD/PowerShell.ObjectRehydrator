using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerShell.ObjectRehydrator.Rehydration
{
    /// <summary>
    /// 把 XML 中的类型名解析为真实的 System.Type。
    ///
    /// 解析优先级：
    ///   C-1) 会话作用域 SessionState.InvokeCommand.InvokeScript("[TypeName]")
    ///        —— cmdlet 场景首选，指向真正的调用方会话（Pester/远程/嵌套 runspace 均正确）
    ///   B)   已知 Runspace + PowerShell.Invoke("[TypeName]")
    ///   D)   AppDomain 程序集扫描
    ///   CLR) Type.GetType 兜底
    /// </summary>
    public sealed class TypeResolver
    {
        private readonly Runspace _runspace;
        private readonly SessionState _sessionState;

        public TypeResolver(Runspace runspace)
        {
            _runspace = runspace;
        }

        /// <summary>cmdlet 内传 this.SessionState（PSCmdlet 无公开 Runspace 属性）。</summary>
        public TypeResolver(SessionState sessionState)
        {
            _sessionState = sessionState;
        }

        public Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            // C-1) 会话作用域解析（cmdlet 场景）
            if (_sessionState != null)
            {
                try
                {
                    var results = _sessionState.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create($"[{typeName}]"),
                        null,
                        null);

                    if (results.Count > 0 && results[0]?.BaseObject is Type t1)
                        return t1;
                }
                catch { }
            }

            // B) 已知 runspace
            if (_runspace != null)
            {
                try
                {
                    using var ps = System.Management.Automation.PowerShell.Create();
                    ps.Runspace = _runspace;
                    var results = ps.AddScript($"[{typeName}]").Invoke();
                    if (results.Count > 0 && results[0]?.BaseObject is Type t2)
                        return t2;
                }
                catch { }
            }

            // D) AppDomain 扫描
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName, throwOnError: false);
                if (t != null) return t;
            }

            // CLR fallback
            return Type.GetType(typeName, throwOnError: false);
        }
    }
}

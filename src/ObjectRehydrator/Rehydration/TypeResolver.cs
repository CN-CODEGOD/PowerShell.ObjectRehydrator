using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerShell.ObjectRehydrator.Rehydration
{
    public sealed class TypeResolver
    {
        private readonly Runspace _runspace;
        private readonly SessionState _sessionState;

        private static readonly Dictionary<string, Type> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "System.Single", typeof(float) },
            { "System.Double", typeof(double) },
            { "System.Int32", typeof(int) },
            { "System.Int64", typeof(long) },
            { "System.Int16", typeof(short) },
            { "System.Byte", typeof(byte) },
            { "System.SByte", typeof(sbyte) },
            { "System.UInt32", typeof(uint) },
            { "System.UInt64", typeof(ulong) },
            { "System.UInt16", typeof(ushort) },
            { "System.String", typeof(string) },
            { "System.Boolean", typeof(bool) },
            { "System.Char", typeof(char) },
            { "System.Decimal", typeof(decimal) },
            { "System.Object", typeof(object) },
            { "System.DateTime", typeof(DateTime) },
            { "System.TimeSpan", typeof(TimeSpan) },
            { "System.Guid", typeof(Guid) },
            { "System.Version", typeof(Version) },
            { "System.Numerics.Vector3", typeof(System.Numerics.Vector3) },
            { "System.Numerics.Vector2", typeof(System.Numerics.Vector2) },
            { "System.Numerics.Vector4", typeof(System.Numerics.Vector4) },
            { "System.Collections.Hashtable", typeof(Hashtable) },
            { "System.Collections.ArrayList", typeof(ArrayList) },
            { "System.Management.Automation.ScriptBlock", typeof(ScriptBlock) },
            { "System.Management.Automation.PSCustomObject", typeof(PSObject) },
            { "PSCustomObject", typeof(PSObject) },
            { "float", typeof(float) },
            { "double", typeof(double) },
            { "int", typeof(int) },
            { "long", typeof(long) },
            { "string", typeof(string) },
            { "bool", typeof(bool) },
            { "char", typeof(char) },
            { "decimal", typeof(decimal) },
            { "byte", typeof(byte) },
            { "object", typeof(object) },
        };

        public TypeResolver(Runspace runspace)
        {
            _runspace = runspace;
        }

        public TypeResolver(SessionState sessionState)
        {
            _sessionState = sessionState;
        }

        public Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            if (BuiltInTypes.TryGetValue(typeName, out var builtIn))
                return builtIn;

            var result = TryResolveViaSession(typeName);
            if (result != null) return result;

            result = TryResolveGeneric(typeName);
            if (result != null) return result;

            return null;
        }

        private Type TryResolveViaSession(string typeName)
        {
            if (_sessionState != null)
            {
                try
                {
                    var escaped = typeName.Replace("`", "``");
                    var results = _sessionState.InvokeCommand.InvokeScript(
                        false,
                        ScriptBlock.Create($"[{escaped}]"),
                        null,
                        null);

                    if (results.Count > 0 && results[0]?.BaseObject is Type t1)
                        return t1;
                }
                catch { }
            }

            if (_runspace != null)
            {
                try
                {
                    using var ps = System.Management.Automation.PowerShell.Create();
                    ps.Runspace = _runspace;
                    var escaped = typeName.Replace("`", "``");
                    var results = ps.AddScript($"[{escaped}]").Invoke();
                    if (results.Count > 0 && results[0]?.BaseObject is Type t2)
                        return t2;
                }
                catch { }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName, throwOnError: false);
                if (t != null) return t;
            }

            return Type.GetType(typeName, throwOnError: false);
        }

        internal static Type TryResolveGeneric(string typeName)
        {
            int backtickIdx = typeName.IndexOf('`');
            if (backtickIdx < 0) return null;

            int bracketStart = typeName.IndexOf('[', backtickIdx);
            if (bracketStart < 0) return null;

            int bracketEnd = typeName.LastIndexOf(']');
            if (bracketEnd <= bracketStart) return null;

            string baseName = typeName.Substring(0, backtickIdx);
            string argsStr = typeName.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

            var argNames = SplitGenericArgs(argsStr);
            if (argNames.Count == 0) return null;

            Type baseType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                baseType = asm.GetType(baseName, throwOnError: false);
                if (baseType != null) break;
            }
            baseType ??= Type.GetType(baseName, throwOnError: false);

            if (baseType == null || !baseType.IsGenericTypeDefinition)
                return null;

            var argTypes = new List<Type>();
            foreach (var argName in argNames)
            {
                string trimmed = argName.Trim();
                Type argType = null;

                if (trimmed.Contains("`") && trimmed.Contains("["))
                    argType = TryResolveGeneric(trimmed);

                if (argType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        argType = asm.GetType(trimmed, throwOnError: false);
                        if (argType != null) break;
                    }
                    argType ??= Type.GetType(trimmed, throwOnError: false);
                }

                if (argType == null) return null;
                argTypes.Add(argType);
            }

            try
            {
                return baseType.MakeGenericType(argTypes.ToArray());
            }
            catch
            {
                return null;
            }
        }

        private static List<string> SplitGenericArgs(string argsStr)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < argsStr.Length; i++)
            {
                char c = argsStr[i];
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(argsStr.Substring(start, i - start));
                    start = i + 1;
                }
            }

            if (start < argsStr.Length)
                result.Add(argsStr.Substring(start));

            return result;
        }
    }
}

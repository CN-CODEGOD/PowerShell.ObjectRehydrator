using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace PowerShell.ObjectRehydrator.Rehydration
{
    /// <summary>
    /// 自描述 XML → 真实类型实例（零代码重建）。
    ///
    /// 顶层分流：
    ///   &lt;array&gt;      → 直接构造 object[]（不走实例化）
    ///   &lt;hashtable&gt;  → 直接构造 Hashtable
    ///   &lt;scriptblock&gt;→ ScriptBlock.Create
    ///   &lt;property&gt;*  → 类实例路径：TypeResolver + 多策略实例化 + 反射赋值
    ///   纯文本          → 标量：LanguagePrimitives.ConvertTo
    ///
    /// 类型解析失败 / 实例化失败一律抛异常，不静默降级为 PSObject。
    /// </summary>
    public sealed class ObjectRehydrator
    {
        private readonly TypeResolver _typeResolver;

        public ObjectRehydrator(TypeResolver typeResolver)
        {
            _typeResolver = typeResolver;
        }

        public object Rehydrate(XElement objectElement)
        {
            if (objectElement == null) return null;

            string typeName = (string)objectElement.Attribute("type")
                           ?? (string)objectElement.Attribute("name");

            // ---- 顶层弱类分流 ----
            var arrayEl = objectElement.Element("array");
            if (arrayEl != null) return BuildArray(arrayEl);

            var hashEl = objectElement.Element("hashtable");
            if (hashEl != null) return BuildHashtable(hashEl);

            var sbEl = objectElement.Element("scriptblock");
            if (sbEl != null) return ScriptBlock.Create(sbEl.Value);

            // ---- 类实例路径 ----
            if (objectElement.Elements("property").Any())
            {
                Type type = ResolveOrFail(typeName, objectElement);

                PSObject state = ParseState(objectElement);
                object instance = CreateInstance(type, state, out string creationError);

                if (instance == null)
                    throw new InvalidOperationException(
                        $"Instance creation failed for '{typeName}': {creationError}");

                Populate(instance, type, state, objectElement);
                return instance;
            }

            // ---- 标量 ----
            string text = objectElement.Value;
            if (string.IsNullOrEmpty(typeName)) return text;

            Type scalarType = ResolveOrFail(typeName, objectElement);
            return ConvertValue(text, scalarType);
        }

        private Type ResolveOrFail(string typeName, XElement context)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new InvalidOperationException(
                    "Type resolution failed: XML element has neither 'type' nor 'name' attribute.");

            Type type = _typeResolver.Resolve(typeName);
            if (type == null)
                throw new InvalidOperationException(
                    $"Type resolution failed for '{typeName}': not resolvable in the current session.");

            return type;
        }

        /// <summary>
        /// 多策略实例化：
        ///   1) 无参构造 Activator.CreateInstance
        ///   2) 单参构造（PSObject / PSCustomObject / object）传入 state
        ///   3) 任意单参构造传入 state
        ///   4) RuntimeHelpers.GetUninitializedObject（跳过构造函数造壳）
        /// </summary>
        private static object CreateInstance(Type type, PSObject state, out string error)
        {
            error = null;
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1) 无参构造
            try
            {
                var o = Activator.CreateInstance(type, nonPublic: true);
                if (o != null) return o;
            }
            catch (Exception ex)
            {
                error = $"no usable parameterless constructor ({ex.GetType().Name}: {ex.Message})";
            }

            var ctors = type.GetConstructors(flags);

            // 2) 单参构造，参数类型可接受 state
            foreach (var ctor in ctors)
            {
                var ps = ctor.GetParameters();
                if (ps.Length != 1) continue;

                var pt = ps[0].ParameterType;
                if (pt == typeof(PSObject) || pt == typeof(object) ||
                    pt == typeof(PSCustomObject) || pt.IsInstanceOfType(state?.BaseObject))
                {
                    var r = TryInvokeCtor(ctor, state);
                    if (r != null) return r;
                }
            }

            // 3) 任意单参构造
            foreach (var ctor in ctors)
            {
                if (ctor.GetParameters().Length != 1) continue;
                var r = TryInvokeCtor(ctor, state);
                if (r != null) return r;
            }

            // 4) 跳过构造函数直接造壳
            try
            {
                var shell = RuntimeHelpers.GetUninitializedObject(type);
                if (shell != null) return shell;
                error = (error ?? "") + "; GetUninitializedObject returned null";
            }
            catch (Exception ex)
            {
                error = (error ?? "") + $"; GetUninitializedObject failed ({ex.GetType().Name}: {ex.Message})";
            }

            return null;
        }

        /// <summary>依次尝试用 PSObject / BaseObject 调用构造函数。</summary>
        private static object TryInvokeCtor(ConstructorInfo ctor, PSObject state)
        {
            foreach (var arg in new object[] { state, state?.BaseObject })
            {
                if (arg == null) continue;
                try { return ctor.Invoke(new[] { arg }); }
                catch { }
            }
            return null;
        }

        /// <summary>反射逐属性赋值；嵌套对象按 type 递归重建。</summary>
        private void Populate(object instance, Type type, PSObject state, XElement objectElement)
        {
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var propElements = objectElement.Elements("property")
                .Where(e => !string.IsNullOrEmpty((string)e.Attribute("name")))
                .ToDictionary(e => (string)e.Attribute("name"),
                              e => e,
                              StringComparer.OrdinalIgnoreCase);

            foreach (var note in state.Properties)
            {
                var prop = type.GetProperty(note.Name, flags);
                if (prop == null || !prop.CanWrite) continue;

                object value = note.Value;

                if (propElements.TryGetValue(note.Name, out var propEl))
                {
                    string declaredTypeName = (string)propEl.Attribute("type");

                    // 嵌套对象：递归重建
                    if (propEl.Elements("property").Any() && !string.IsNullOrEmpty(declaredTypeName))
                    {
                        var nested = new XElement("object",
                            new XAttribute("name", declaredTypeName),
                            new XAttribute("type", declaredTypeName),
                            propEl.Elements());
                        value = Rehydrate(nested);
                    }
                    else if (!string.IsNullOrEmpty(declaredTypeName))
                    {
                        Type declared = _typeResolver.Resolve(declaredTypeName);
                        if (declared != null)
                            value = ConvertValue(value, declared);
                    }
                }
                else if (prop.PropertyType != typeof(object))
                {
                    value = ConvertValue(value, prop.PropertyType);
                }

                try { prop.SetValue(instance, value); }
                catch { }
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType == null) return value;
            if (targetType.IsInstanceOfType(value)) return value;

            try
            {
                if (LanguagePrimitives.TryConvertTo(value, targetType, out object converted))
                    return converted;
            }
            catch { }

            return value;
        }

        private PSObject ParseState(XElement objectElement)
        {
            var result = new PSObject();
            foreach (var prop in objectElement.Elements("property"))
            {
                string name = (string)prop.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;

                result.Properties.Add(new PSNoteProperty(name, ParsePropertyNode(prop)));
            }
            return result;
        }

        /// <summary>解析一个 property/key/value 节点的内容（按其子结构分流）。</summary>
        private object ParsePropertyNode(XElement node)
        {
            // 嵌套对象
            if (node.Elements("property").Any())
            {
                string nestedType = (string)node.Attribute("type")
                                 ?? (string)node.Attribute("name");
                var nested = new XElement("object",
                    new XAttribute("name", nestedType ?? "unknown"),
                    new XAttribute("type", nestedType ?? "unknown"),
                    node.Elements("property"));
                return Rehydrate(nested);
            }

            var arrayEl = node.Element("array");
            if (arrayEl != null) return BuildArray(arrayEl);

            var hashEl = node.Element("hashtable");
            if (hashEl != null) return BuildHashtable(hashEl);

            var sbEl = node.Element("scriptblock");
            if (sbEl != null) return ScriptBlock.Create(sbEl.Value);

            // 标量
            string declared = (string)node.Attribute("type");
            string text = node.Value;
            if (!string.IsNullOrEmpty(declared))
            {
                Type t = _typeResolver.Resolve(declared);
                if (t != null) return ConvertValue(text, t);
            }
            return text;
        }

        private object[] BuildArray(XElement arrayEl)
        {
            var list = new List<object>();
            foreach (var v in arrayEl.Elements("value"))
                list.Add(ParsePropertyNode(v));
            return list.ToArray();
        }

        private Hashtable BuildHashtable(XElement hashtableEl)
        {
            var ht = new Hashtable();
            foreach (var key in hashtableEl.Elements("key"))
            {
                string keyName = (string)key.Attribute("name") ?? string.Empty;
                string keyType = (string)key.Attribute("keyType");

                object k = keyName;
                if (!string.IsNullOrEmpty(keyType))
                {
                    Type kt = _typeResolver.Resolve(keyType);
                    if (kt != null) k = ConvertValue(keyName, kt);
                }

                ht[k] = ParsePropertyNode(key);
            }
            return ht;
        }
    }
}

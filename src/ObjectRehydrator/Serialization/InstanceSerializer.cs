using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Xml.Linq;

namespace PowerShell.ObjectRehydrator.Serialization
{
    /// <summary>
    /// 反射实例 → 自动生成带 type 元数据的自描述 XML（方案 X：每层都标 type）。
    /// 用户类零代码（不需要 save()、构造函数、DoInit）。
    ///
    /// 顶层分流：弱类型（标量/array/hashtable/scriptblock）直接写值节点，
    /// 不写 property 列表；类实例写 property 列表。
    /// </summary>
    public static class InstanceSerializer
    {
        public static string Save(object instance)
        {
            return ToObjectElement(instance).ToString();
        }

        public static XElement ToObjectElement(object instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            string typeName = GetTypeName(instance);
            object baseObj = (instance is PSObject wrapped) ? wrapped.BaseObject : instance;

            var element = new XElement("object",
                new XAttribute("name", typeName),
                new XAttribute("type", typeName));

            object content = SerializeValue(baseObj);

            // 类实例 → property 列表；弱类型 → 单个值节点
            if (content is IEnumerable<XElement> propList)
            {
                foreach (var p in propList) element.Add(p);
            }
            else
            {
                element.Add(content);
            }

            return element;
        }

        private static string GetTypeName(object instance)
        {
            var ps = instance as PSObject ?? PSObject.AsPSObject(instance);
            if (ps.TypeNames.Count > 0)
                return ps.TypeNames[0];
            return instance.GetType().Name;
        }

        private static List<XElement> SerializeProperties(object instance)
        {
            var result = new List<XElement>();
            var ps = instance as PSObject ?? PSObject.AsPSObject(instance);
            var baseObj = ps.BaseObject;
            Type realType = baseObj?.GetType() ?? ps.GetType();

            var propTypeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in realType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                propTypeMap[p.Name] = p.PropertyType;

            foreach (var p in ps.Properties)
            {
                if (p.MemberType != PSMemberTypes.Property &&
                    p.MemberType != PSMemberTypes.NoteProperty)
                    continue;

                if (string.Equals(p.Name, "Path", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var mi = p.GetType().GetProperty("PropertyInfo")?.GetValue(p) as PropertyInfo;
                    if (mi != null && mi.GetCustomAttributes(true).Any(a => a.GetType().Name.Contains("Hidden")))
                        continue;
                }
                catch { }

                object value;
                try { value = p.Value; }
                catch { continue; }

                string typeAttr;
                if (propTypeMap.TryGetValue(p.Name, out var declaredType))
                    typeAttr = declaredType.FullName ?? declaredType.Name;
                else if (value != null)
                    typeAttr = TypeOf(value);
                else
                    typeAttr = "System.String";

                result.Add(new XElement(
                    "property",
                    new XAttribute("name", p.Name),
                    new XAttribute("type", typeAttr),
                    SerializeValue(value)));
            }

            return result;
        }

        /// <summary>按值种类生成节点：scriptblock / hashtable / array / property 列表 / 文本。</summary>
        private static object SerializeValue(object value)
        {
            if (value == null)
                return string.Empty;

            if (value is PSObject pso)
                value = pso.BaseObject;

            if (value is ScriptBlock sb)
                return new XElement("scriptblock", sb.ToString());

            if (value is IDictionary dict)
            {
                var keys = new List<XElement>();
                foreach (DictionaryEntry kv in dict)
                {
                    keys.Add(new XElement("key",
                        new XAttribute("name", kv.Key?.ToString() ?? string.Empty),
                        new XAttribute("keyType", kv.Key != null ? TypeOf(kv.Key) : "System.String"),
                        new XAttribute("type", kv.Value != null ? TypeOf(kv.Value) : "System.String"),
                        ScalarOrNested(kv.Value)));
                }
                return new XElement("hashtable", keys);
            }

            if (value is IEnumerable en && !(value is string))
            {
                var vals = new List<XElement>();
                int i = 0;
                foreach (var item in en)
                {
                    object itemContent = item is XElement xe ? (object)xe : ScalarOrNested(item);
                    vals.Add(new XElement("value",
                        new XAttribute("index", i),
                        new XAttribute("type", item != null ? TypeOf(item) : "System.String"),
                        itemContent));
                    i++;
                }
                return new XElement("array", vals);
            }

            if (IsNestedObject(value))
                return SerializeProperties(value);

            if (value is PSCustomObject)
                return SerializeProperties(value);

            return value.ToString();
        }

        private static object ScalarOrNested(object item)
        {
            if (item is PSObject pso) item = pso.BaseObject;
            if (item is ScriptBlock sb)
                return new XElement("scriptblock", sb.ToString());
            if (item is IDictionary || (item is IEnumerable && !(item is string)))
                return SerializeValue(item);
            if (IsNestedObject(item))
                return SerializeProperties(item);
            if (item is PSCustomObject)
                return SerializeProperties(item);
            return item?.ToString() ?? string.Empty;
        }

        /// <summary>PS class 用 TypeNames[0]（脚本类名），其余用 CLR FullName。</summary>
        private static string TypeOf(object value)
        {
            if (value == null) return "System.String";

            var ps = value as PSObject ?? PSObject.AsPSObject(value);
            if (ps.TypeNames.Count > 0)
            {
                var tn = ps.TypeNames[0];
                // 脚本 class 名不含点，直接用；System.* 用 CLR 全名更精确
                if (!tn.StartsWith("System.", StringComparison.Ordinal))
                    return tn;
            }

            var t = (value is PSObject w ? w.BaseObject : value)?.GetType();
            return t?.FullName ?? t?.Name ?? "System.String";
        }

        private static bool IsNestedObject(object value)
        {
            if (value == null) return false;
            if (value is string) return false;
            if (value is IDictionary) return false;
            if (value is IEnumerable) return false;

            var t = value.GetType();
            if (t.IsPrimitive || t.IsEnum) return false;
            if (value is decimal || value is DateTime || value is DateTimeOffset) return false;
            if (value is Guid || value is TimeSpan || value is Uri) return false;

            var ps = value as PSObject ?? PSObject.AsPSObject(value);
            if (ps.TypeNames.Count > 0)
            {
                var tn = ps.TypeNames[0];
                if (tn.StartsWith("System.", StringComparison.Ordinal))
                    return false;
                return true;
            }
            return false;
        }
    }
}

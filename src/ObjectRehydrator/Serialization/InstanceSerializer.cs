using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Xml.Linq;

namespace PowerShell.ObjectRehydrator.Serialization
{
    public static class InstanceSerializer
    {
        public static string Save(object instance)
        {
            return ToObjectElement(instance).ToString();
        }

        public static XElement ToObjectElement(object instance)
        {
            if (instance == null)
            {
                return new XElement("object",
                    new XAttribute("name", "null"),
                    new XAttribute("type", "null"),
                    new XAttribute("null", "true"));
            }

            object unwrapped = Unwrap(instance);

            if (unwrapped == null)
            {
                return new XElement("object",
                    new XAttribute("name", "null"),
                    new XAttribute("type", "null"),
                    new XAttribute("null", "true"));
            }

            string typeName = GetTypeName(instance);

            var element = new XElement("object",
                new XAttribute("name", typeName),
                new XAttribute("type", typeName));

            object content = SerializeValue(unwrapped);

            if (content is IEnumerable<XElement> propList && !(content is XElement))
            {
                foreach (var p in propList) element.Add(p);
            }
            else
            {
                element.Add(content);
            }

            return element;
        }

        private static object Unwrap(object value)
        {
            if (value is PSObject pso) return pso.BaseObject;
            return value;
        }

        private static string GetTypeName(object instance)
        {
            if (instance == null) return "null";

            var ps = instance as PSObject ?? PSObject.AsPSObject(instance);
            var baseObj = ps.BaseObject;

            if (baseObj == null) return "null";

            if (baseObj is PSCustomObject)
                return "PSCustomObject";

            if (ps.TypeNames.Count > 0)
            {
                var tn = ps.TypeNames[0];
                if (!string.IsNullOrEmpty(tn))
                    return tn;
            }

            return baseObj.GetType().FullName ?? baseObj.GetType().Name;
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
                try { value = p.Value; } catch { continue; }

                string typeAttr;
                if (propTypeMap.TryGetValue(p.Name, out var declaredType))
                    typeAttr = declaredType.FullName ?? declaredType.Name;
                else if (value != null)
                    typeAttr = TypeOf(value);
                else
                    typeAttr = "null";

                result.Add(new XElement(
                    "property",
                    new XAttribute("name", p.Name),
                    new XAttribute("type", typeAttr),
                    SerializeValue(Unwrap(value))));
            }

            return result;
        }

        private static object SerializeValue(object value)
        {
            if (value == null)
                return string.Empty;

            if (value is PSObject pso)
                value = pso.BaseObject;

            if (value == null)
                return string.Empty;

            if (value is ScriptBlock sb)
                return new XElement("scriptblock", sb.ToString());

            if (value is DateTime dt)
            {
                return new XElement("datetime",
                    new XAttribute("ticks", dt.Ticks),
                    new XAttribute("kind", (int)dt.Kind));
            }

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

            if (value is Enum enumVal)
                return enumVal.ToString("D");

            if (IsNestedObject(value))
                return SerializeProperties(value);

            if (value is PSCustomObject)
                return SerializeProperties(value);

            return value.ToString();
        }

        private static object ScalarOrNested(object item)
        {
            if (item is PSObject pso) item = pso.BaseObject;
            if (item == null) return string.Empty;
            if (item is ScriptBlock sb)
                return new XElement("scriptblock", sb.ToString());
            if (item is DateTime dt)
                return new XElement("datetime",
                    new XAttribute("ticks", dt.Ticks),
                    new XAttribute("kind", (int)dt.Kind));
            if (item is IDictionary || (item is IEnumerable && !(item is string)))
                return SerializeValue(item);
            if (item is Enum enumVal)
                return enumVal.ToString("D");
            if (IsNestedObject(item))
                return SerializeProperties(item);
            if (item is PSCustomObject)
                return SerializeProperties(item);
            return item?.ToString() ?? string.Empty;
        }

        private static string TypeOf(object value)
        {
            if (value == null) return "null";

            var ps = value as PSObject ?? PSObject.AsPSObject(value);
            var baseObj = ps.BaseObject;

            if (baseObj == null) return "null";

            if (ps.TypeNames.Count > 0 && !string.IsNullOrEmpty(ps.TypeNames[0]))
                return ps.TypeNames[0];

            return baseObj.GetType().FullName ?? baseObj.GetType().Name;
        }

        private static bool IsNestedObject(object value)
        {
            if (value == null) return false;
            if (value is string) return false;
            if (value is IDictionary) return false;
            if (value is IEnumerable) return false;
            if (value is ScriptBlock) return false;
            if (value is DateTime) return false;
            if (value is Enum) return false;

            var t = value.GetType();
            if (t.IsPrimitive || t.IsEnum) return false;
            if (value is decimal || value is Guid || value is TimeSpan) return false;
            if (value is DateTimeOffset || value is Uri) return false;
            if (value is Version || value is char || value is bool) return false;

            var ps = value as PSObject ?? PSObject.AsPSObject(value);
            if (ps.TypeNames.Count > 0)
            {
                var tn = ps.TypeNames[0];
                if (tn.StartsWith("System.", StringComparison.Ordinal))
                {
                    var writableProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Any(p => p.CanWrite);
                    return writableProps;
                }
                return true;
            }
            return false;
        }
    }
}

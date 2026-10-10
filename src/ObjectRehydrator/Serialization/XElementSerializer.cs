using System;
using System.Collections;
using System.Management.Automation;
using System.Xml.Linq;

namespace PowerShell.ObjectRehydrator.Serialization
{
    /// <summary>
    /// 把任意 PS 值转换为 XML 元素树。
    ///
    /// 移植自 import-xml.psm1 的 ConvertTo-XElement，并修复以下硬伤：
    ///   1) 原实现使用 `(if ($item -ne $null) { ... })` —— 仅在 PS7+ 合法，
    ///      C# 侧改为显式 null 判断。
    ///   2) 原实现变量名拼写不一致，C# 侧统一命名。
    ///   3) 原实现依赖 `$item.PSObject -ne $null` 分支，C# 侧用 PSObject.Base 判定。
    /// </summary>
    public static class XElementSerializer
    {
        public static XElement ToXElement(
            object value,
            string elementName = "object",
            string nameAttr = null)
        {
            // 已是 XElement：直通
            if (value is XElement existing)
                return existing;

            // IDictionary / hashtable
            if (value is IDictionary dict)
            {
                var children = new System.Collections.Generic.List<XElement>();
                foreach (DictionaryEntry kv in dict)
                {
                    children.Add(new XElement(
                        "key",
                        new XAttribute("name", kv.Key),
                        ValueNode(kv.Value)));
                }
                var content = new XElement("hashtable", children);
                return nameAttr != null
                    ? new XElement(elementName, new XAttribute("name", nameAttr), content)
                    : new XElement(elementName, content);
            }

            // 数组 / IEnumerable（排除 string）
            if (value is IEnumerable en && !(value is string))
            {
                var vals = new System.Collections.Generic.List<XElement>();
                int i = 0;
                foreach (var item in en)
                {
                    XElement itemNode;
                    if (item is XElement xe)
                        itemNode = xe;
                    else if (IsComplex(item))
                        itemNode = ToXElement(item, "value");
                    else
                        itemNode = new XElement("value", item?.ToString() ?? string.Empty);

                    itemNode.Add(new XAttribute("index", i));
                    vals.Add(itemNode);
                    i++;
                }
                var arrayNode = new XElement("array", vals);
                return nameAttr != null
                    ? new XElement(elementName, new XAttribute("name", nameAttr), arrayNode)
                    : new XElement(elementName, arrayNode);
            }

            // 对象 / PSCustomObject / 类实例
            var propElements = new System.Collections.Generic.List<XElement>();
            var ps = value as PSObject ?? PSObject.AsPSObject(value);
            foreach (var p in ps.Properties)
            {
                // 仅保留 NoteProperty / Property；跳过方法等
                if (p.MemberType != PSMemberTypes.NoteProperty &&
                    p.MemberType != PSMemberTypes.Property)
                    continue;

                object pv = p.Value;
                XElement content;

                if (pv is ScriptBlock sb)
                    content = new XElement("scriptblock", sb.ToString());
                else if (pv is XElement pxe)
                    content = pxe;
                else if (IsComplex(pv))
                    content = ToXElement(pv, "value");
                else
                    content = new XElement("value", pv?.ToString() ?? string.Empty);

                propElements.Add(new XElement(
                    "property",
                    new XAttribute("name", p.Name),
                    content));
            }

            return nameAttr != null
                ? new XElement(elementName, new XAttribute("name", nameAttr), propElements)
                : new XElement(elementName, propElements);
        }

        /// <summary>是否属于需要递归展开的复杂类型（字典 / 非字符串 IEnumerable）。</summary>
        private static bool IsComplex(object item)
        {
            if (item == null) return false;
            if (item is string) return false;
            if (item is IDictionary) return true;
            if (item is IEnumerable) return true;
            return false;
        }

        /// <summary>把值包成 text / 结构节点。</summary>
        private static object ValueNode(object v)
        {
            if (v == null) return string.Empty;
            if (v is XElement xe) return xe;
            if (IsComplex(v)) return ToXElement(v, "value");
            return v.ToString();
        }
    }
}

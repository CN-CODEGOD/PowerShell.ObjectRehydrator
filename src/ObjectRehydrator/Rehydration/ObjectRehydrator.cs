using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace PowerShell.ObjectRehydrator.Rehydration
{
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

            if (typeName == "null" || objectElement.Attribute("null")?.Value == "true")
                return null;

            var dtEl = objectElement.Element("datetime");
            if (dtEl != null)
            {
                long ticks = long.Parse((string)dtEl.Attribute("ticks"));
                var kind = (DateTimeKind)(int)dtEl.Attribute("kind");
                return new DateTime(ticks, kind);
            }

            var arrayEl = objectElement.Element("array");
            if (arrayEl != null)
                return BuildCollectionOrArray(typeName, arrayEl);

            var hashEl = objectElement.Element("hashtable");
            if (hashEl != null)
                return BuildDictionaryOrHashtable(typeName, hashEl);

            var sbEl = objectElement.Element("scriptblock");
            if (sbEl != null) return ScriptBlock.Create(sbEl.Value);

            if (objectElement.Elements("property").Any())
            {
                if (typeName == "PSCustomObject" ||
                    typeName == "System.Management.Automation.PSCustomObject")
                    return BuildPSCustomObject(objectElement);

                Type type = ResolveOrFail(typeName, objectElement);
                PSObject state = ParseState(objectElement);
                object instance = CreateInstance(type, state, out string creationError);

                if (instance == null)
                    throw new InvalidOperationException(
                        $"Instance creation failed for '{typeName}': {creationError}");

                Populate(instance, type, state, objectElement);
                return instance;
            }

            string text = objectElement.Value;
            if (string.IsNullOrEmpty(typeName)) return text;

            if (typeName == "null") return null;

            Type scalarType = ResolveOrFail(typeName, objectElement);
            return ConvertScalarValue(text, scalarType);
        }

        private object BuildCollectionOrArray(string typeName, XElement arrayEl)
        {
            var items = BuildItemList(arrayEl);

            if (string.IsNullOrWhiteSpace(typeName) || typeName == "System.Object[]" || typeName.EndsWith("[]"))
                return items.ToArray();

            Type collectionType = _typeResolver.Resolve(typeName);
            if (collectionType == null)
                return items.ToArray();

            return BuildTypedCollection(collectionType, items, typeName);
        }

        private object BuildTypedCollection(Type collectionType, List<object> items, string typeName)
        {
            try
            {
                if (collectionType == typeof(PSObject))
                    return items.ToArray();

                if (collectionType.IsGenericType)
                {
                    var genericDef = collectionType.GetGenericTypeDefinition();
                    var elementType = collectionType.GetGenericArguments()[0];

                    if (genericDef == typeof(List<>))
                        return BuildList(elementType, items);
                    if (genericDef == typeof(Queue<>))
                        return BuildQueue(elementType, items);
                    if (genericDef == typeof(HashSet<>))
                        return BuildHashSet(elementType, items);
                    if (genericDef == typeof(Stack<>))
                        return BuildStack(elementType, items);
                    if (genericDef == typeof(LinkedList<>))
                        return BuildLinkedList(elementType, items);
                }

                var parameterless = Activator.CreateInstance(collectionType, nonPublic: true);
                if (parameterless != null)
                {
                    var addMethod = collectionType.GetMethod("Enqueue")
                                 ?? collectionType.GetMethod("Push")
                                 ?? collectionType.GetMethod("Add");
                    if (addMethod != null)
                    {
                        var addParamType = addMethod.GetParameters()[0].ParameterType;
                        foreach (var item in items)
                        {
                            var converted = ConvertScalarValue(item, addParamType);
                            addMethod.Invoke(parameterless, new[] { converted });
                        }
                    }
                    return parameterless;
                }
            }
            catch { }

            return items.ToArray();
        }

        private static object BuildList(Type elementType, List<object> items)
        {
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            foreach (var item in items)
            {
                var converted = ConvertScalarValue(item, elementType);
                addMethod.Invoke(list, new[] { converted });
            }
            return list;
        }

        private static object BuildQueue(Type elementType, List<object> items)
        {
            var queueType = typeof(Queue<>).MakeGenericType(elementType);
            var queue = Activator.CreateInstance(queueType);
            var enqueueMethod = queueType.GetMethod("Enqueue");
            foreach (var item in items)
            {
                var converted = ConvertScalarValue(item, elementType);
                enqueueMethod.Invoke(queue, new[] { converted });
            }
            return queue;
        }

        private static object BuildHashSet(Type elementType, List<object> items)
        {
            var setType = typeof(HashSet<>).MakeGenericType(elementType);
            var set = Activator.CreateInstance(setType);
            var addMethod = setType.GetMethod("Add");
            foreach (var item in items)
            {
                var converted = ConvertScalarValue(item, elementType);
                addMethod.Invoke(set, new[] { converted });
            }
            return set;
        }

        private static object BuildStack(Type elementType, List<object> items)
        {
            var stackType = typeof(Stack<>).MakeGenericType(elementType);
            var stack = Activator.CreateInstance(stackType);
            var pushMethod = stackType.GetMethod("Push");
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var converted = ConvertScalarValue(items[i], elementType);
                pushMethod.Invoke(stack, new[] { converted });
            }
            return stack;
        }

        private static object BuildLinkedList(Type elementType, List<object> items)
        {
            var listType = typeof(LinkedList<>).MakeGenericType(elementType);
            var list = Activator.CreateInstance(listType);
            var addLastMethod = listType.GetMethod("AddLast", new[] { elementType });
            foreach (var item in items)
            {
                var converted = ConvertScalarValue(item, elementType);
                addLastMethod.Invoke(list, new[] { converted });
            }
            return list;
        }

        private object BuildDictionaryOrHashtable(string typeName, XElement hashtableEl)
        {
            if (string.IsNullOrWhiteSpace(typeName) ||
                typeName == "System.Collections.Hashtable" ||
                typeName == "Hashtable")
                return BuildHashtable(hashtableEl);

            Type dictType = _typeResolver.Resolve(typeName);
            if (dictType == null || !dictType.IsGenericType)
                return BuildHashtable(hashtableEl);

            var genericDef = dictType.GetGenericTypeDefinition();
            if (genericDef == typeof(Dictionary<,>) ||
                genericDef == typeof(SortedDictionary<,>) ||
                genericDef == typeof(ConcurrentDictionary<,>))
            {
                return BuildTypedDictionary(dictType, hashtableEl);
            }

            return BuildHashtable(hashtableEl);
        }

        private object BuildTypedDictionary(Type dictType, XElement hashtableEl)
        {
            try
            {
                var instance = Activator.CreateInstance(dictType, nonPublic: true);
                var genericArgs = dictType.GetGenericArguments();
                var keyType = genericArgs[0];
                var valueType = genericArgs[1];

                var addMethod = dictType.GetMethod("Add", new[] { keyType, valueType });

                foreach (var keyEl in hashtableEl.Elements("key"))
                {
                    string keyName = (string)keyEl.Attribute("name") ?? string.Empty;
                    object k = ConvertScalarValue(keyName, keyType);
                    object v = ParsePropertyNode(keyEl);
                    if (v != null && valueType != typeof(object))
                        v = ConvertScalarValue(v, valueType);

                    if (addMethod != null)
                        addMethod.Invoke(instance, new[] { k, v });
                    else if (instance is IDictionary dict)
                        dict[k] = v;
                }

                return instance;
            }
            catch
            {
                return BuildHashtable(hashtableEl);
            }
        }

        private Hashtable BuildHashtable(XElement hashtableEl)
        {
            var ht = new Hashtable();
            foreach (var key in hashtableEl.Elements("key"))
            {
                string keyName = (string)key.Attribute("name") ?? string.Empty;
                string keyTypeStr = (string)key.Attribute("keyType");

                object k = keyName;
                if (!string.IsNullOrEmpty(keyTypeStr))
                {
                    Type kt = _typeResolver.Resolve(keyTypeStr);
                    if (kt != null) k = ConvertScalarValue(keyName, kt);
                }

                ht[k] = ParsePropertyNode(key);
            }
            return ht;
        }

        private PSObject BuildPSCustomObject(XElement objectElement)
        {
            var ps = new PSObject();
            foreach (var prop in objectElement.Elements("property"))
            {
                string name = (string)prop.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                ps.Properties.Add(new PSNoteProperty(name, ParsePropertyNode(prop)));
            }
            return ps;
        }

        private List<object> BuildItemList(XElement arrayEl)
        {
            var list = new List<object>();
            foreach (var v in arrayEl.Elements("value"))
                list.Add(ParsePropertyNode(v));
            return list;
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

        private static object CreateInstance(Type type, PSObject state, out string error)
        {
            error = null;
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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

            foreach (var ctor in ctors)
            {
                if (ctor.GetParameters().Length != 1) continue;
                var r = TryInvokeCtor(ctor, state);
                if (r != null) return r;
            }

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
                object value = note.Value;
                Type declaredType = null;

                if (propElements.TryGetValue(note.Name, out var propEl))
                {
                    string declaredTypeName = (string)propEl.Attribute("type");

                    if (propEl.Elements("property").Any() && !string.IsNullOrEmpty(declaredTypeName))
                    {
                        if (declaredTypeName != "PSCustomObject" &&
                            declaredTypeName != "System.Management.Automation.PSCustomObject")
                        {
                            var nested = new XElement("object",
                                new XAttribute("name", declaredTypeName),
                                new XAttribute("type", declaredTypeName),
                                propEl.Elements());
                            value = Rehydrate(nested);
                        }
                    }
                    else if (!string.IsNullOrEmpty(declaredTypeName))
                    {
                        declaredType = _typeResolver.Resolve(declaredTypeName);
                        if (declaredType != null)
                            value = ConvertScalarValue(value, declaredType);
                    }
                }

                var prop = type.GetProperty(note.Name, flags);
                if (prop != null && prop.CanWrite)
                {
                    if (declaredType == null && prop.PropertyType != typeof(object))
                        value = ConvertScalarValue(value, prop.PropertyType);

                    try { prop.SetValue(instance, value); }
                    catch { }
                    continue;
                }

                var field = type.GetField(note.Name, flags);
                if (field != null && !field.IsInitOnly)
                {
                    if (declaredType == null && field.FieldType != typeof(object))
                        value = ConvertScalarValue(value, field.FieldType);

                    try { field.SetValue(instance, value); }
                    catch { }
                }
            }
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

        private object ParsePropertyNode(XElement node)
        {
            if (node == null) return null;

            var dtEl = node.Element("datetime");
            if (dtEl != null)
            {
                long ticks = long.Parse((string)dtEl.Attribute("ticks"));
                var kind = (DateTimeKind)(int)dtEl.Attribute("kind");
                return new DateTime(ticks, kind);
            }

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
            if (arrayEl != null)
            {
                string arrType = (string)node.Attribute("type");
                return BuildCollectionOrArray(arrType, arrayEl);
            }

            var hashEl = node.Element("hashtable");
            if (hashEl != null)
            {
                string hashType = (string)node.Attribute("type");
                return BuildDictionaryOrHashtable(hashType, hashEl);
            }

            var sbEl = node.Element("scriptblock");
            if (sbEl != null) return ScriptBlock.Create(sbEl.Value);

            string declared = (string)node.Attribute("type");
            string text = node.Value;

            if (declared == "null") return null;

            if (!string.IsNullOrEmpty(declared))
            {
                Type t = _typeResolver.Resolve(declared);
                if (t != null) return ConvertScalarValue(text, t);
            }
            return text;
        }

        internal static object ConvertScalarValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType == null) return value;
            if (targetType == typeof(object)) return value;
            if (targetType.IsInstanceOfType(value)) return value;

            if (targetType == typeof(Guid))
            {
                if (Guid.TryParse(value.ToString(), out var g)) return g;
            }

            if (targetType == typeof(Version))
            {
                if (Version.TryParse(value.ToString(), out var v)) return v;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    var underlyingValue = System.Convert.ToInt64(value);
                    return Enum.ToObject(targetType, underlyingValue);
                }
                catch { }

                try
                {
                    return Enum.Parse(targetType, value.ToString(), ignoreCase: true);
                }
                catch { }
            }

            try
            {
                return System.Convert.ChangeType(value, targetType);
            }
            catch { }

            try
            {
                if (LanguagePrimitives.TryConvertTo(value, targetType, out object converted))
                    return converted;
            }
            catch { }

            return value;
        }
    }
}

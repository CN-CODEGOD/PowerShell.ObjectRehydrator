using System;
using System.Management.Automation;
using System.Reflection;

namespace PowerShell.ObjectRehydrator.Rehydration
{
    /// <summary>
    /// 工厂：把还原出的对象状态注入真实类型实例。
    ///
    /// 这是本项目的核心增值点 —— 官方 CLIXML 不做这一步。
    ///
    /// 策略（按优先级）：
    ///   1) PS class 约定：构造函数接受 [pscustomobject]，内部调用 DoInit($object)。
    ///      → 反射找 .ctor(PSObject | object)，传入 state。
    ///   2) 无参构造 + DoInit(PSObject/object) 方法。
    ///   3) 无参构造 + 逐属性赋值（public settable property）。
    ///   4) 全部失败 → 返回 null（调用方报错，不静默降级为 PSObject）。
    /// </summary>
    public static class ObjectFactory
    {
        public static object Create(Type type, PSObject state)
        {
            if (type == null) return null;

            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1) ctor(PSObject/object)
            foreach (var ctor in type.GetConstructors(flags))
            {
                var ps = ctor.GetParameters();
                if (ps.Length == 1 &&
                    (ps[0].ParameterType == typeof(PSObject) ||
                     ps[0].ParameterType == typeof(object)))
                {
                    try
                    {
                        return ctor.Invoke(new object[] { state });
                    }
                    catch
                    {
                        // 继续尝试其他策略
                    }
                }
            }

            // 2) ctor() + DoInit(PSObject/object)
            object instance = null;
            try
            {
                instance = Activator.CreateInstance(type, nonPublic: true);
            }
            catch
            {
                return null;
            }

            if (instance == null) return null;

            var doInit = type.GetMethod("DoInit", flags, null,
                new[] { typeof(PSObject) }, null)
                ?? type.GetMethod("DoInit", flags, null, new[] { typeof(object) }, null);

            if (doInit != null)
            {
                try
                {
                    doInit.Invoke(instance, new object[] { state });
                    return instance;
                }
                catch
                {
                    // 落到逐属性赋值
                }
            }

            // 3) 逐属性赋值
            bool assigned = false;
            foreach (var note in state.Properties)
            {
                var prop = type.GetProperty(note.Name, flags);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        prop.SetValue(instance, note.Value);
                        assigned = true;
                    }
                    catch { /* 忽略单个属性失败 */ }
                }
            }

            return assigned || doInit == null ? instance : null;
        }
    }
}

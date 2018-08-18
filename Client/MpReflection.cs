﻿using Harmony;
using Multiplayer.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Multiplayer.Client
{
    public static class MpReflection
    {
        public delegate object Getter(object instance, int index);
        public delegate void Setter(object instance, object value, int index);

        public static IEnumerable<Assembly> AllAssemblies
        {
            get
            {
                yield return Assembly.GetAssembly(typeof(Game));

                foreach (ModContentPack mod in LoadedModManager.RunningMods)
                    foreach (Assembly assembly in mod.assemblies.loadedAssemblies)
                        yield return assembly;

                if (Assembly.GetEntryAssembly() != null)
                    yield return Assembly.GetEntryAssembly();
            }
        }

        private static Dictionary<string, Type> types = new Dictionary<string, Type>();
        private static Dictionary<string, Type> pathType = new Dictionary<string, Type>();
        private static Dictionary<string, Getter> getters = new Dictionary<string, Getter>();
        private static Dictionary<string, Setter> setters = new Dictionary<string, Setter>();

        /// <summary>
        /// Get the value of a static property/field in type specified by memberPath
        /// </summary>
        public static object GetValueStatic(Type type, string memberPath, int index = -1)
        {
            return GetValue(null, type + "/" + memberPath, index);
        }

        /// <summary>
        /// Get the value of a static property/field specified by memberPath
        /// </summary>
        public static object GetValueStatic(string memberPath, int index = -1)
        {
            return GetValue(null, memberPath, index);
        }

        /// <summary>
        /// Get the value of a property/field specified by memberPath
        /// Type specification in path is not required if instance is provided
        /// </summary>
        public static object GetValue(object instance, string memberPath, int index = -1)
        {
            if (instance != null)
                memberPath = AppendType(memberPath, instance.GetType());

            InitPropertyOrField(memberPath);
            return getters[memberPath](instance, index);
        }

        public static void SetValueStatic(Type type, string memberPath, object value, int index = -1)
        {
            SetValue(null, type + "/" + memberPath, value, index);
        }

        public static void SetValue(object instance, string memberPath, object value, int index = -1)
        {
            if (instance != null)
                memberPath = AppendType(memberPath, instance.GetType());

            InitPropertyOrField(memberPath);
            if (setters[memberPath] == null)
                throw new Exception($"The value of {memberPath} can't be set");

            setters[memberPath](instance, value, index);
        }

        public static Type PropertyOrFieldType(string memberPath)
        {
            InitPropertyOrField(memberPath);
            return pathType[memberPath];
        }

        /// <summary>
        /// Appends the type name to the path if needed
        /// </summary>
        public static string AppendType(string memberPath, Type type)
        {
            string[] parts = memberPath.Split(new[] { '/' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1 || !parts[0].Contains('.'))
                memberPath = type + "/" + memberPath;

            return memberPath;
        }

        private static void InitPropertyOrField(string memberPath)
        {
            if (getters.ContainsKey(memberPath))
                return;

            string[] parts = memberPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new Exception($"Path requires at least the type and one member: {memberPath}");

            Type type = GetTypeByName(parts[0]);
            if (type == null)
                throw new Exception($"Type {parts[0]} not found for path: {memberPath}");

            List<MemberInfo> members = new List<MemberInfo>();
            Type currentType = type;
            bool hasSetter = false;

            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                bool arrayAccess = part.EndsWith("[]");
                part = part.Replace("[]", "");

                if (!currentType.IsInterface)
                {
                    FieldInfo field = AccessTools.Field(currentType, part);
                    if (field != null)
                    {
                        members.Add(field);
                        currentType = field.FieldType;
                        hasSetter = true;
                    }

                    PropertyInfo property = AccessTools.Property(currentType, part);
                    if (property != null)
                    {
                        members.Add(property);
                        currentType = property.PropertyType;
                        hasSetter = property.GetSetMethod(true) != null;
                    }
                }

                MethodInfo method = AccessTools.Method(currentType, part);
                if (method != null)
                {
                    members.Add(method);
                    currentType = method.ReturnType;
                    hasSetter = false;
                }

                if (currentType != null)
                {
                    if (arrayAccess)
                    {
                        if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            PropertyInfo indexer = AccessTools.Property(currentType, "Item");
                            members.Add(indexer);
                            currentType = indexer.PropertyType;
                            hasSetter = true;
                        }
                        else if (currentType.IsArray)
                        {
                            currentType = currentType.GetElementType();
                            members.Add(new ArrayAccess() { ElementType = currentType });
                            hasSetter = true;
                        }
                    }

                    continue;
                }

                throw new Exception($"Member {part} not found in path: {memberPath}, current type: {currentType}");
            }

            MemberInfo lastMember = members.Last();
            pathType[memberPath] = currentType;

            string methodName = memberPath.Replace('/', '_');
            DynamicMethod getter = new DynamicMethod("MP_Reflection_Getter_" + methodName, typeof(object), new[] { typeof(object), typeof(int) }, true);
            ILGenerator getterGen = getter.GetILGenerator();

            EmitAccess(type, members, members.Count, getterGen, 1);
            getterGen.Emit(OpCodes.Ret);
            getters[memberPath] = (Getter)getter.CreateDelegate(typeof(Getter));

            if (!hasSetter)
            {
                setters[memberPath] = null;
            }
            else
            {
                DynamicMethod setter = new DynamicMethod("MP_Reflection_Setter_" + methodName, null, new[] { typeof(object), typeof(object), typeof(int) }, true);
                ILGenerator setterGen = setter.GetILGenerator();

                // Load the instance
                EmitAccess(type, members, members.Count - 1, setterGen, 2);

                // Load the index
                if (lastMember is ArrayAccess || (lastMember is PropertyInfo prop1 && prop1.GetIndexParameters().Length == 1))
                    setterGen.Emit(OpCodes.Ldarg_2);

                // Load and box/cast the value
                setterGen.Emit(OpCodes.Ldarg_1);
                if (currentType.IsValueType)
                    setterGen.Emit(OpCodes.Unbox_Any, currentType);
                else
                    setterGen.Emit(OpCodes.Castclass, currentType);

                if (lastMember is FieldInfo field)
                {
                    if (field.IsStatic)
                        setterGen.Emit(OpCodes.Stsfld, field);
                    else
                        setterGen.Emit(OpCodes.Stfld, field);
                }
                else if (lastMember is PropertyInfo prop)
                {
                    MethodInfo setterMethod = prop.GetSetMethod(true);
                    setterGen.Emit(setterMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, setterMethod);
                }
                else if (lastMember is ArrayAccess)
                {
                    setterGen.Emit(OpCodes.Stelem, currentType);
                }

                setterGen.Emit(OpCodes.Ret);
                setters[memberPath] = (Setter)setter.CreateDelegate(typeof(Setter));
            }
        }

        private static void EmitAccess(Type type, List<MemberInfo> members, int count, ILGenerator gen, int indexArg)
        {
            if (!members[0].IsStatic())
            {
                gen.Emit(OpCodes.Ldarg_0);

                if (type.IsValueType)
                    gen.Emit(OpCodes.Unbox_Any, type);
                else
                    gen.Emit(OpCodes.Castclass, type);
            }

            for (int i = 0; i < count; i++)
            {
                MemberInfo member = members[i];
                Type memberType;

                if (member is FieldInfo field)
                {
                    if (field.IsStatic)
                        gen.Emit(OpCodes.Ldsfld, field);
                    else
                        gen.Emit(OpCodes.Ldfld, field);

                    memberType = field.FieldType;
                }
                else if (member is PropertyInfo prop)
                {
                    if (prop.GetIndexParameters().Length == 1)
                        gen.Emit(OpCodes.Ldarg, indexArg);

                    MethodInfo m = prop.GetGetMethod(true);
                    gen.Emit(m.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, m);
                    memberType = m.ReturnType;
                }
                else if (member is MethodInfo method)
                {
                    gen.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
                    memberType = method.ReturnType;
                }
                else if (member is ArrayAccess arr)
                {
                    gen.Emit(OpCodes.Ldarg, indexArg);
                    gen.Emit(OpCodes.Ldelem);
                    memberType = arr.ElementType;
                }
                else
                {
                    throw new Exception("Unsupported member type " + member.GetType());
                }

                BoxIfNeeded(memberType);
            }

            void BoxIfNeeded(Type forType)
            {
                if (forType.IsValueType)
                    gen.Emit(OpCodes.Box, forType);
            }
        }

        public static Type GetTypeByName(string name)
        {
            if (types.TryGetValue(name, out Type cached))
                return cached;

            foreach (Assembly assembly in AllAssemblies)
            {
                Type type = assembly.GetType(name, false);
                if (type != null)
                {
                    types[name] = type;
                    return type;
                }
            }

            types[name] = null;
            return null;
        }
    }

    public class ArrayAccess : MemberInfo
    {
        public Type ElementType { get; set; }

        public override MemberTypes MemberType => throw new NotImplementedException();

        public override string Name => throw new NotImplementedException();

        public override Type DeclaringType => throw new NotImplementedException();

        public override Type ReflectedType => throw new NotImplementedException();

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }
    }

    public static class ReflectionExtensions
    {
        public static object GetPropertyOrField(this object obj, string memberPath, int index = -1)
        {
            return MpReflection.GetValue(obj, memberPath, index);
        }

        public static void SetPropertyOrField(this object obj, string memberPath, object value, int index = -1)
        {
            MpReflection.SetValue(obj, memberPath, value, index);
        }

        private static readonly MethodInfo exposeSmallComps = AccessTools.Method(typeof(Game), "ExposeSmallComponents");
        public static void ExposeSmallComponents(this Game game)
        {
            exposeSmallComps.Invoke(game, null);
        }

        public static bool IsStatic(this MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.IsStatic;
            else if (member is PropertyInfo prop)
                return prop.GetGetMethod(true).IsStatic;
            else if (member is MethodInfo method)
                return method.IsStatic;
            else
                throw new Exception("Invalid member " + member?.GetType());
        }
    }
}

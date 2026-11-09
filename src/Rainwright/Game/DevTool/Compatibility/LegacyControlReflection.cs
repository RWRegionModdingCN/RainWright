using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DryCycle.DevUI.DevTool.Compatibility;

// CLR member layouts are immutable; original values, options and callbacks are not.
// Weak type ownership also avoids retaining an optional plugin solely for this cache.
internal static class LegacyControlReflection
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private sealed class Schema
    {
        internal readonly Dictionary<string, FieldInfo> Fields = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, PropertyInfo> Properties = new(StringComparer.Ordinal);
        internal readonly Dictionary<MethodKey, MethodInfo> Methods = new();
    }
    private readonly struct MethodKey : IEquatable<MethodKey>
    {
        private readonly string name;
        private readonly Type[] parameters;
        internal MethodKey(string name, Type[] parameters) { this.name = name; this.parameters = parameters; }
        public bool Equals(MethodKey other)
        {
            if (name != other.name || parameters.Length != other.parameters.Length) return false;
            for (int i = 0; i < parameters.Length; i++) if (parameters[i] != other.parameters[i]) return false;
            return true;
        }
        public override bool Equals(object value) => value is MethodKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = name.GetHashCode();
                for (int i = 0; i < parameters.Length; i++) hash = hash * 31 + parameters[i].GetHashCode();
                return hash;
            }
        }
    }
    private static readonly ConditionalWeakTable<Type, Schema> Schemas = new();
    internal static FieldInfo Field(Type type, string name)
    {
        if (type == null) return null;
        var schema = Schemas.GetValue(type, _ => new Schema());
        lock (schema)
        {
            if (schema.Fields.TryGetValue(name, out var found)) return found;
            for (var current = type; current != null; current = current.BaseType)
                if ((found = current.GetField(name, Flags)) != null) break;
            schema.Fields.Add(name, found);
            return found;
        }
    }
    internal static PropertyInfo Property(Type type, string name)
    {
        if (type == null) return null;
        var schema = Schemas.GetValue(type, _ => new Schema());
        lock (schema)
        {
            if (schema.Properties.TryGetValue(name, out var found)) return found;
            for (var current = type; current != null; current = current.BaseType)
                if ((found = current.GetProperty(name, Flags)) != null) break;
            schema.Properties.Add(name, found);
            return found;
        }
    }
    internal static MethodInfo Method(Type type, string name, Type[] parameters)
    {
        if (type == null) return null;
        var schema = Schemas.GetValue(type, _ => new Schema());
        var key = new MethodKey(name, parameters);
        lock (schema)
        {
            if (schema.Methods.TryGetValue(key, out var found)) return found;
            for (var current = type; current != null; current = current.BaseType)
                if ((found = current.GetMethod(name, Flags, null, parameters, null)) != null) break;
            schema.Methods.Add(new MethodKey(name, (Type[])parameters.Clone()), found);
            return found;
        }
    }
}

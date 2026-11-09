using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Background;

/// <summary>
/// Optional data-only interop. No RegionKit method, property accessor, constructor, UI, factory
/// delegate or serializer is invoked. Reflected methods below belong only to the BCL weak table.
/// </summary>
internal static class BackgroundDataAccess
{
    internal sealed class Parameter
    {
        internal string Name;
        internal FieldInfo Field;
        internal Type ValueType;
    }
    private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> fields = new();
    private static readonly Dictionary<Type, Parameter[]> parameters = new();
    private static Assembly regionKit;
    private static object roomTable;
    private static MethodInfo tryGetRoom;
    private static object elementTable;
    private static MethodInfo tryGetElement, addElement;
    private static bool discoveryAttempted;
    internal static object Root(RoomSettings settings)
    {
        if (settings == null) return null;
        if (!discoveryAttempted)
        {
            discoveryAttempted = true;
            regionKit = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RegionKit");
            Type data = regionKit?.GetType("RegionKit.Modules.BackgroundBuilder.Data", false);
            roomTable = data?.GetField("table", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            tryGetRoom = roomTable?.GetType().GetMethod("TryGetValue");
            Type elementData = regionKit?.GetType("RegionKit.Modules.BackgroundBuilder.BackgroundElementData", false);
            elementTable = elementData?.GetField("table", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            tryGetElement = elementTable?.GetType().GetMethod("TryGetValue");
            addElement = elementTable?.GetType().GetMethod("Add");
        }
        if (roomTable == null || tryGetRoom == null) return null;
        object[] args = { settings, null };
        return (bool)tryGetRoom.Invoke(roomTable, args) ? args[1] : null;
    }
    internal static FieldInfo Field(Type type, string name)
    {
        if (!fields.TryGetValue(type, out var lookup)) fields.Add(type, lookup = new Dictionary<string, FieldInfo>(StringComparer.Ordinal));
        if (lookup.TryGetValue(name, out var known)) return known;
        FieldInfo result = null;
        for (Type current = type; current != null && result == null; current = current.BaseType)
            result = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        lookup[name] = result; return result;
    }
    internal static object Get(object target, string name) => target == null ? null : Field(target.GetType(), name)?.GetValue(target);
    internal static Func<string> StringReader(object target, string name)
    {
        var field = Field(target.GetType(), name) ?? throw new MissingFieldException(target.GetType().FullName, name);
        // Compile a field read once; the native sprite can observe legacy edits without reflection per frame.
        return Expression.Lambda<Func<string>>(Expression.Field(Expression.Constant(target), field)).Compile();
    }
    internal static void Set(object target, string name, object value)
    {
        var field = target == null ? null : Field(target.GetType(), name);
        if (field == null) throw new MissingFieldException(target?.GetType().FullName, name);
        field.SetValue(target, value);
    }
    internal static Parameter[] Parameters(object sceneData)
    {
        if (sceneData == null) return Array.Empty<Parameter>();
        Type type = sceneData.GetType();
        if (parameters.TryGetValue(type, out var known)) return known;
        var result = new List<Parameter>();
        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var attribute = prop.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "BackgroundDataAttribute");
            if (attribute == null) continue;
            string name = prop.Name, backing = "_" + prop.Name;
            foreach (var arg in attribute.NamedArguments)
            {
                if (arg.MemberName == "name") name = arg.TypedValue.Value as string ?? name;
                if (arg.MemberName == "backingFieldName") backing = arg.TypedValue.Value as string ?? backing;
            }
            var field = Field(type, backing) ?? Field(type, "<" + prop.Name + ">k__BackingField");
            if (field != null && IsScalar(field.FieldType)) result.Add(new Parameter { Name = name, Field = field, ValueType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType });
        }
        return parameters[type] = result.OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }
    internal static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(float) || type == typeof(int) || type == typeof(bool) || type == typeof(string) || type == typeof(Vector2) || type == typeof(Color) || type.IsEnum;
    }
    internal static string Text(object value)
    {
        if (value == null) return "Null";
        if (value is Vector2 vector) return F(vector.x) + ", " + F(vector.y);
        if (value is Color color) return ColorUtility.ToHtmlStringRGB(color);
        if (value is float number) return F(number);
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }
    internal static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    internal static object Parse(string text, Type type)
    {
        if (text == "Null" && (!type.IsValueType || Nullable.GetUnderlyingType(type) != null)) return null;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) { BackgroundDocument.ValidateValue(text); return text; }
        if (type == typeof(float)) return BackgroundDocument.Number(text);
        if (type == typeof(int)) return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(text);
        if (type == typeof(Vector2)) { var parts = text.Split(','); if (parts.Length != 2) throw new FormatException("Expected X, Y."); return new Vector2(BackgroundDocument.Number(parts[0]), BackgroundDocument.Number(parts[1])); }
        if (type == typeof(Color)) { if (!ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out Color value)) throw new FormatException("Expected a hex color."); return value; }
        if (type.IsEnum) return Enum.Parse(type, text, true);
        throw new NotSupportedException("Unsupported background value: " + type.FullName);
    }
    internal static object Effective(object data, Parameter parameter)
    {
        var visited = new HashSet<object>();
        for (object cursor = data; cursor != null && visited.Add(cursor); cursor = Get(cursor, "parent"))
        {
            var field = Field(cursor.GetType(), parameter.Field.Name);
            object value = field?.GetValue(cursor);
            if (value != null) return value;
        }
        return null;
    }
    internal static IEnumerable<object> Elements(object sceneData)
    {
        var visited = new HashSet<object>();
        var stack = new Stack<IEnumerator>();
        if (Get(sceneData, "backgroundElements") is IEnumerable list) stack.Push(list.GetEnumerator());
        int budget = 4096;
        while (stack.Count > 0)
        {
            var current = stack.Peek();
            if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); continue; }
            if (--budget < 0) throw new InvalidOperationException("Invalid or oversized background data tree.");
            object element = current.Current;
            if (element == null) continue;
            if (!visited.Add(element)) throw new InvalidOperationException("Invalid or oversized background data tree.");
            yield return element;
            if (Get(element, "elements") is IEnumerable children) stack.Push(children.GetEnumerator());
        }
    }
    internal static BackgroundElementSchema Schema(object record) => record == null ? null : BackgroundElementSchema.All.FirstOrDefault(s => s.Record == record.GetType().Name);
    internal static string Arguments(object record, BackgroundElementSchema schema)
    {
        return string.Join(", ", schema.Fields.Select(path =>
        {
            string[] parts = path.Split('.'); object value = Get(record, parts[0]);
            if (parts.Length > 1 && value is Vector2 vector) value = parts[1] == "x" ? vector.x : vector.y;
            return Text(value);
        }));
    }
    internal static string Tags(object record)
    {
        var result = new List<string>();
        foreach (var item in new[] { ("anchor", "anchorPos"), ("scale", "spriteScale"), ("alpha", "spriteAlpha"), ("shader", "spriteShader"), ("color", "spriteColor"), ("container", "container"), ("spriteName", "spriteName") })
            if (Get(record, item.Item2) is object value) result.Add(item.Item1 + "|" + Text(value));
        if (Get(record, "lockX") is true) result.Add("lock|X");
        if (Get(record, "lockY") is true) result.Add("lock|Y");
        if (Get(record, "unrecognizedTags") is IEnumerable unknown)
            foreach (object tag in unknown) result.Add((string)tag);
        return result.Count == 0 ? "" : " : " + string.Join(" : ", result);
    }
    internal static object AllocateRecord(BackgroundElementSchema schema)
    {
        Type type = regionKit?.GetType("RegionKit.Modules.BackgroundBuilder.BackgroundElementData+" + schema.Record, false);
        if (type == null) throw new InvalidOperationException("RegionKit background data schema is unavailable: " + schema.Record);
        object record = AllocateData(type);
        Set(record, "depth", 100f);
        SetArguments(record, schema, schema.Defaults);
        return record;
    }
    internal static object AllocateData(Type type)
    {
        object record = FormatterServices.GetUninitializedObject(type);
        // Initialize only BCL collections and scalar data; never execute a mod constructor.
        for (Type cursor = type; cursor != null; cursor = cursor.BaseType)
            foreach (var field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    field.SetValue(record, Activator.CreateInstance(field.FieldType));
        return record;
    }
    internal static object CloneDataGraph(object root)
    {
        var copied = new Dictionary<object, object>(); int budget = 20000;
        object Clone(object source, int depth)
        {
            if (source == null || source is string || source.GetType().IsValueType) return source;
            if (copied.TryGetValue(source, out object existing)) return existing;
            Type type = source.GetType();
            bool record = type.FullName?.StartsWith("RegionKit.Modules.BackgroundBuilder.Data+", StringComparison.Ordinal) == true ||
                type.FullName?.StartsWith("RegionKit.Modules.BackgroundBuilder.BackgroundElementData+", StringComparison.Ordinal) == true;
            if (!record && source is not IList) return source;
            if (--budget < 0 || depth > 256) throw new InvalidOperationException("Background snapshot exceeds its data budget.");
            if (source is IList list)
            {
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>)) throw new InvalidOperationException("Unsupported data collection: " + type.FullName);
                var result = (IList)Activator.CreateInstance(type); copied[source] = result;
                foreach (object value in list) result.Add(Clone(value, depth + 1));
                return result;
            }
            object target = FormatterServices.GetUninitializedObject(type); copied[source] = target;
            CopyFields(source, target, value => Clone(value, depth + 1)); return target;
        }
        return Clone(root, 0);
    }
    internal static void CopyFields(object source, object target, Func<object, object> transform = null)
    {
        if (source.GetType() != target.GetType()) throw new InvalidOperationException("Background data version changed.");
        for (Type type = source.GetType(); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                field.SetValue(target, transform == null ? field.GetValue(source) : transform(field.GetValue(source)));
    }
    internal static void BindRuntimeMetadata(BackgroundScene.BackgroundSceneElement element, object record)
    {
        if (elementTable == null || tryGetElement == null || addElement == null) return;
        object[] args = { element, null };
        object metadata;
        if ((bool)tryGetElement.Invoke(elementTable, args)) metadata = args[1];
        else
        {
            Type type = elementTable.GetType().GetGenericArguments()[1];
            metadata = AllocateData(type);
            addElement.Invoke(elementTable, new[] { element, metadata });
        }
        Set(metadata, "dataElement", record);
    }
    internal static void InitializePlainScene(object root, BackgroundScene scene, string name)
    {
        if (root == null || scene == null || name is not ("None" or "VoidSeaScene")) return;
        Type type = regionKit?.GetType("RegionKit.Modules.BackgroundBuilder.Data+BGSceneData", false);
        if (type == null) return;
        object data = AllocateData(type);
        Set(data, "_Scene", scene); Set(data, "sceneInitialized", true); Set(root, "sceneData", data);
        var typeField = Field(root.GetType(), "_type");
        var value = typeField.FieldType.GetField(name, BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
        if (value != null) typeField.SetValue(root, value);
    }
    internal static void SetArguments(object record, BackgroundElementSchema schema, string arguments)
    {
        string[] values = arguments.Split(',').Select(s => s.Trim()).ToArray();
        if (schema.Record == "BG_ElementGroup" && (values.Length == 1 || values.Length == 3))
            values = values.Concat(Enumerable.Repeat("0", schema.Fields.Length - values.Length)).ToArray();
        if (values.Length != schema.Fields.Length) throw new FormatException("Wrong argument count for " + schema.Name);
        // Validate everything before touching live data.
        var writes = new List<(FieldInfo Field, object Value)>();
        Vector2 pos = Get(record, "pos") is Vector2 existing ? existing : Vector2.zero;
        for (int i = 0; i < values.Length; i++)
        {
            string path = schema.Fields[i];
            if (path == "pos.x") { pos.x = BackgroundDocument.Number(values[i]); continue; }
            if (path == "pos.y") { pos.y = BackgroundDocument.Number(values[i]); continue; }
            var field = Field(record.GetType(), path) ?? throw new MissingFieldException(record.GetType().FullName, path);
            object value = Parse(values[i], field.FieldType);
            if (path is "depth" or "fromDepth" or "toDepth" && value is float depth && depth <= 0 && schema.Record != "BG_ElementGroup" && !(schema.Record == "RWS_PebbsGrid" && depth == 0)) throw new FormatException("Depth must be positive.");
            writes.Add((field, value));
        }
        foreach (var write in writes) write.Field.SetValue(record, write.Value);
        Set(record, "pos", pos);
    }
}

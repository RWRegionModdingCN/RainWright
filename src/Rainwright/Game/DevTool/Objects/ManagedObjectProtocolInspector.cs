using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using DryCycle.DevUI.DevTool.Compatibility;
using RWCustom;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Objects;

/// <summary>
/// Framework-neutral adapter for the ManagedData/ManagedField protocol popularized by POM.
/// DryCycle does not reference Pom.dll: compatibility is structural and discovered from the live
/// Data object (fields + GetValue/SetValue). This keeps POM/RegionKit optional and also allows
/// compatible third-party implementations to participate without a hard dependency.
/// </summary>
internal sealed class ManagedObjectProtocolInspector :
    IObjectInspectorAdapter,
    IObjectInspectorCoverageProvider,
    IObjectInspectorSnapshotProvider,
    IObjectInspectorGizmoAdapter
{
    internal static readonly ManagedObjectProtocolInspector Instance = new();

    private sealed class DataSchema
    {
        internal readonly MemberInfo FieldsMember;
        internal readonly MethodInfo GetValue;
        internal readonly MethodInfo SetValue;
        internal readonly MemberInfo BackedFieldsMember;
        internal readonly bool Valid;
        internal readonly ConcurrentDictionary<Type, MethodInfo> Setters = new();

        internal DataSchema(
            MemberInfo fieldsMember,
            MethodInfo getValue,
            MethodInfo setValue,
            MemberInfo backedFieldsMember = null)
        {
            FieldsMember = fieldsMember;
            GetValue = getValue?.MakeGenericMethod(typeof(object));
            SetValue = setValue;
            BackedFieldsMember = backedFieldsMember;
            Valid = fieldsMember != null && getValue != null && setValue != null;
        }
    }

    private sealed class Binding
    {
        internal object Descriptor;
        internal string RawKey;
        internal string Key;
        internal string DisplayName;
        internal Type ValueType;
        internal object CurrentValue;
        internal EditorPropertyKind Kind;
        internal EditorPropertyGizmoShape GizmoShape;
        internal float GizmoScale = 1f;
        internal bool HasRange;
        internal float Min;
        internal float Max;
        internal float Step;
        internal string[] Options = Array.Empty<string>();
        internal bool InspectorSupported;
        internal bool GizmoSupported = true;
        internal bool IsIntVector;
        internal int ArrayIndex = -1;
        internal Type StorageType;
        internal bool RequiresOriginalControl;
        internal string DrivenType, DrivingKey;

        internal Binding WithValue(object value)
        {
            Binding copy = (Binding)MemberwiseClone();
            copy.CurrentValue = value;
            return copy;
        }
    }

    private static readonly ConcurrentDictionary<Type, DataSchema> Schemas = new();
    // Many registrations share the same ManagedData CLR type but have different descriptors.
    // Cache immutable metadata by descriptor identity, never by object type or current values.
    private static ConditionalWeakTable<object, Binding> DescriptorBindings = new();
    private static ConditionalWeakTable<object, HashSet<string>> FailedFields = new();

    public bool CanInspect(PlacedObject target)
    {
        PlacedObject.Data data = target?.data;
        return data != null && Schemas.GetOrAdd(data.GetType(), BuildSchema).Valid;
    }

    public IReadOnlyList<EditorPropertySnapshot> Capture(PlacedObject target) => CaptureCore(target, false, out _);

    public IReadOnlyList<EditorPropertySnapshot> CaptureWithCoverage(PlacedObject target, out ObjectInspectorCoverage coverage) =>
        CaptureCore(target, true, out coverage);

    private static IReadOnlyList<EditorPropertySnapshot> CaptureCore(PlacedObject target, bool includeCoverage, out ObjectInspectorCoverage coverage)
    {
        coverage = ObjectInspectorCoverage.Conservative();
        PlacedObject.Data data = target?.data;
        if (data == null) return Array.Empty<EditorPropertySnapshot>();

        DataSchema schema = Schemas.GetOrAdd(data.GetType(), BuildSchema);
        if (!schema.Valid || !TryBuildBindings(data, schema, out List<Binding> bindings))
            return Array.Empty<EditorPropertySnapshot>();

        List<EditorPropertySnapshot> result = new(bindings.Count + 2)
        {
            new()
            {
                Key = "__managedDataType",
                DisplayName = "Data Type",
                Group = "Native Data",
                Source = "Managed object protocol",
                Kind = EditorPropertyKind.ReadOnly,
                StringValue = data.GetType().FullName ?? data.GetType().Name
            }
        };

        for (int i = 0; i < bindings.Count; i++)
            result.Add(Capture(bindings[i]));

        HashSet<string> managedKeys = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bindings.Count; i++) managedKeys.Add(bindings[i].RawKey);
        IncludeBackedMemberNames(data, schema, managedKeys);
        if (includeCoverage) coverage = BuildCoverage(data, schema, bindings, managedKeys);
        result.AddRange(NativeDataReflectionInspector.CaptureAdditionalFields(
            target, schema.FieldsMember.DeclaringType, managedKeys));

        // Serializing an entire third-party model is not an inspector render operation.
        if (DevUiDiagnosticsPolicy.Enabled)
        {
            string serialized;
            try { serialized = data.ToString() ?? string.Empty; }
            catch (Exception error)
            {
                ReportFieldFailure(data, "serialization", error);
                serialized = "<serialization failed: " + RootMessage(error) + ">";
            }
            result.Add(new EditorPropertySnapshot
            {
                Key = "__managedSerialized",
                DisplayName = "Serialized Data",
                Group = "Compatibility",
                Source = "Managed object protocol",
                Kind = EditorPropertyKind.ReadOnly,
                StringValue = serialized
            });
        }
        return result;
    }

    public bool TrySetValue(
        PlacedObject target,
        string key,
        EditorPropertyValue value)
    {
        PlacedObject.Data data = target?.data;
        if (data == null || string.IsNullOrEmpty(key))
            return false;

        if (key.StartsWith("native.", StringComparison.Ordinal))
            return NativeDataReflectionInspector.Instance.TrySetValue(target, key, value);

        DataSchema schema = Schemas.GetOrAdd(data.GetType(), BuildSchema);
        if (!schema.Valid ||
            !TryBuildBindings(data, schema, out List<Binding> bindings) ||
            !TryFindBinding(bindings, key, out Binding binding) ||
            !binding.InspectorSupported)
            return false;

        try
        {
            object next = ConvertEditorValue(binding, value);
            if (next == null)
                return false;
            if (binding.DrivenType != null && next is Vector2 driven)
                next = ProjectDriven(data, schema, binding, driven);

            Type storageType = binding.StorageType ?? binding.ValueType;
            if (binding.ArrayIndex >= 0)
            {
                if (ReadManagedValue(data, schema, binding.RawKey) is not Array current ||
                    binding.ArrayIndex >= current.Length)
                    return false;
                // Some frameworks reuse their default array across instances. Never mutate it,
                // another object's data or a history snapshot through a shared reference.
                Array copy = (Array)current.Clone();
                copy.SetValue(next, binding.ArrayIndex);
                next = copy;
            }
            List<(string Key, Type Type, object Before, object After)> writes = new()
            { (binding.RawKey, storageType, ReadManagedValue(data, schema, binding.RawKey), next) };
            // A driving vector changes the orientation of its dependent radii in POM's data
            // protocol. Apply that rule inside the same authored transaction, never during capture.
            foreach (Binding dependent in bindings)
                if (dependent.DrivingKey == binding.RawKey && dependent.DrivenType != "relativeLine" &&
                    dependent.InspectorSupported && dependent.CurrentValue is Vector2 radius)
                    writes.Add((dependent.RawKey, typeof(Vector2), radius,
                        next is Vector2 driver ? PerpendicularRadius(driver, radius) : ProjectDriven(data, schema, dependent, radius)));
            int attempted = 0;
            try
            {
                for (; attempted < writes.Count; attempted++)
                {
                    var write = writes[attempted];
                    schema.Setters.GetOrAdd(write.Type, type => schema.SetValue.MakeGenericMethod(type))
                        .Invoke(data, new[] { write.Key, write.After });
                }
            }
            catch
            {
                for (int i = Math.Min(attempted, writes.Count - 1); i >= 0; i--)
                    try
                    {
                        var write = writes[i];
                        schema.Setters.GetOrAdd(write.Type, type => schema.SetValue.MakeGenericMethod(type))
                            .Invoke(data, new[] { write.Key, write.Before });
                    }
                    catch (Exception restore) { Core.EditorDiagnostics.LogSource?.LogWarning("Managed field rollback failed: " + restore); }
                throw;
            }
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning(
                "DevTool managed object mutation failed for " +
                binding.DisplayName + ": " + error);
            return false;
        }
    }

    public ObjectInspectorCoverage GetCoverage(PlacedObject target)
    {
        PlacedObject.Data data = target?.data;
        if (data == null)
            return ObjectInspectorCoverage.Conservative();

        DataSchema schema = Schemas.GetOrAdd(data.GetType(), BuildSchema);
        if (!schema.Valid ||
            !TryBuildBindings(data, schema, out List<Binding> bindings))
            return ObjectInspectorCoverage.Conservative();

        return BuildCoverage(data, schema, bindings);
    }

    private static ObjectInspectorCoverage BuildCoverage(PlacedObject.Data data, DataSchema schema, List<Binding> bindings, HashSet<string> coveredMembers = null)
    {

        int supported = 0;
        int unsupported = 0;
        bool inspectorComplete = true;
        bool gizmoComplete = true;

        for (int i = 0; i < bindings.Count; i++)
        {
            Binding binding = bindings[i];
            if (binding.InspectorSupported)
                supported++;
            else
            {
                inspectorComplete = false;
                unsupported++;
            }

            if (!binding.GizmoSupported)
                gizmoComplete = false;
        }

        // Managed fields are not necessarily the whole story. A derived Data type can append its
        // own authored public members and a custom Representation can edit those. If we can see a
        // simple extra public member that is not represented by a managed key, stay conservative
        // and report the unsupported surface without constructing its editor.
        if (coveredMembers == null)
        {
            coveredMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bindings.Count; i++) coveredMembers.Add(bindings[i].RawKey);
            IncludeBackedMemberNames(data, schema, coveredMembers);
        }
        if (HasUncoveredAuthoredMembers(data.GetType(), coveredMembers))
        {
            inspectorComplete = false;
            unsupported++;
        }

        return new ObjectInspectorCoverage(
            inspectorComplete,
            gizmoComplete,
            supported,
            unsupported);
    }

    public bool TryBuildGizmoValue(
        PlacedObject target,
        string key,
        float relativeX,
        float relativeY,
        bool snap,
        out EditorPropertyValue value)
    {
        value = default;
        PlacedObject.Data data = target?.data;
        if (data == null || string.IsNullOrEmpty(key) || !Finite(relativeX) || !Finite(relativeY))
            return false;

        DataSchema schema = Schemas.GetOrAdd(data.GetType(), BuildSchema);
        if (!schema.Valid ||
            !TryBuildBindings(data, schema, out List<Binding> bindings) ||
            !TryFindBinding(bindings, key, out Binding binding) ||
            !binding.InspectorSupported ||
            !binding.GizmoSupported ||
            binding.GizmoShape == EditorPropertyGizmoShape.None)
            return false;

        if (binding.IsIntVector)
        {
            Vector2 relative = new(relativeX, relativeY);
            if (binding.GizmoShape == EditorPropertyGizmoShape.Direction4)
            {
                float angle = Custom.VecToDeg(relative);
                angle -= ((angle + 45f + 360f) % 90f) - 45f;
                relative = Custom.DegToVec(angle) * 20f;
            }
            else if (binding.GizmoShape == EditorPropertyGizmoShape.Direction8)
            {
                float angle = Custom.VecToDeg(relative);
                angle -= ((angle + 22.5f + 360f) % 45f) - 22.5f;
                relative = Custom.DegToVec(angle);
                if (Mathf.Abs(relative.x) > 0.5f) relative.x = Mathf.Sign(relative.x);
                if (Mathf.Abs(relative.y) > 0.5f) relative.y = Mathf.Sign(relative.y);
                relative *= 20f;
            }

            Vector2 world = target.pos + relative;
            IntVector2 ownTile = new(
                Mathf.FloorToInt(world.x / 20f),
                Mathf.FloorToInt(world.y / 20f));
            IntVector2 parentTile = new(
                Mathf.FloorToInt(target.pos.x / 20f),
                Mathf.FloorToInt(target.pos.y / 20f));
            IntVector2 delta = ownTile - parentTile;
            value = new EditorPropertyValue(
                EditorPropertyKind.Vector2,
                x: delta.x,
                y: delta.y);
            return true;
        }

        if (binding.ValueType == typeof(Vector2))
        {
            Vector2 next = new(relativeX, relativeY);
            if (binding.DrivenType == "relativeLine" &&
                ReadManagedValue(data, schema, binding.DrivingKey) is Vector2 driver) next -= driver;
            if (binding.DrivenType != null) next = ProjectDriven(data, schema, binding, next);
            value = new EditorPropertyValue(
                EditorPropertyKind.Vector2,
                x: next.x,
                y: next.y);
            return true;
        }

        return false;
    }

    internal static void ClearSchemaCache()
    {
        Schemas.Clear();
        DescriptorBindings = new ConditionalWeakTable<object, Binding>();
        FailedFields = new ConditionalWeakTable<object, HashSet<string>>();
    }

    private static DataSchema BuildSchema(Type dataType)
    {
        MemberInfo fieldsMember =
            (MemberInfo)FindField(dataType, "fields") ??
            FindProperty(dataType, "fields");

        MethodInfo getValue = FindGenericMethod(dataType, "GetValue", 1);
        MethodInfo setValue = FindGenericMethod(dataType, "SetValue", 2);

        if (fieldsMember == null || getValue == null || setValue == null)
            return new DataSchema(null, null, null);

        Type fieldsType = fieldsMember switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => null
        };

        if (fieldsType == null ||
            (!fieldsType.IsArray && !typeof(IEnumerable).IsAssignableFrom(fieldsType)))
            return new DataSchema(null, null, null);

        return new DataSchema(fieldsMember, getValue, setValue,
            (MemberInfo)FindField(dataType, "fieldInfosByKey") ?? FindProperty(dataType, "fieldInfosByKey"));
    }

    private static bool TryBuildBindings(
        object data,
        DataSchema schema,
        out List<Binding> bindings)
    {
        bindings = new List<Binding>();
        try
        {
            object collection = ReadMember(schema.FieldsMember, data);
            if (collection is not IEnumerable enumerable)
                return false;
            int fieldCount = 0;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (object descriptor in enumerable)
            {
                if (++fieldCount > 4096) throw new InvalidOperationException("Managed fields exceed the 4096-field capture budget.");
                if (descriptor == null)
                    continue;

                string rawKey = "__descriptor" + fieldCount;
                try
                {
                    rawKey = ReadString(descriptor, "key") ?? rawKey;
                    if (string.IsNullOrWhiteSpace(rawKey)) throw new InvalidOperationException("Managed field key is empty.");
                    if (!keys.Add(rawKey)) throw new InvalidOperationException("Duplicate managed field key: " + rawKey);
                    object current = ReadManagedValue(data, schema, rawKey);
                    ValidateCurrentValue(current);
                    if (!DescriptorBindings.TryGetValue(descriptor, out Binding metadata))
                    {
                        object defaultValue = ReadNamedMember(descriptor, "DefaultValue");
                        metadata = BuildBinding(descriptor, rawKey, null,
                            current?.GetType() ?? defaultValue?.GetType());
                        if (HasCustomControlBehavior(descriptor, schema.FieldsMember.DeclaringType.DeclaringType))
                        {
                            // A custom control may normalize values or update sibling fields.
                            // Keep its value visible until a native semantic adapter is registered.
                            metadata.Kind = EditorPropertyKind.ReadOnly;
                            metadata.RequiresOriginalControl = true;
                            metadata.InspectorSupported = false; metadata.GizmoSupported = false;
                            metadata.GizmoShape = EditorPropertyGizmoShape.None;
                        }
                        DescriptorBindings.Add(descriptor, metadata);
                    }

                    if (current is Vector2[] vectors && !metadata.RequiresOriginalControl)
                    {
                        if (vectors.Length > 4096) throw new InvalidOperationException("Managed vector array exceeds the 4096-point capture budget.");
                        for (int index = 0; index < vectors.Length; index++) ValidateCurrentValue(vectors[index]);
                        bool includeParent = ReadNamedMember(descriptor, "IncludeParent") is bool include && include;
                        string representation = ReadString(descriptor, "RepresentationType");
                        bool nativeGeometry = representation is "Chain" or "Polygon";
                        for (int index = includeParent ? 1 : 0; index < vectors.Length; index++)
                        {
                            Binding point = metadata.WithValue(vectors[index]);
                            point.Key += "[" + index + "]";
                            point.DisplayName += " " + (index + 1);
                            point.ArrayIndex = index;
                            point.StorageType = typeof(Vector2[]);
                            point.ValueType = typeof(Vector2);
                            point.Kind = EditorPropertyKind.Vector2;
                            point.InspectorSupported = true;
                            point.GizmoSupported = nativeGeometry;
                            point.GizmoShape = nativeGeometry ? EditorPropertyGizmoShape.Line : EditorPropertyGizmoShape.None;
                            bindings.Add(point);
                        }
                        if (vectors.Length <= (includeParent ? 1 : 0))
                            bindings.Add(metadata.WithValue(current));
                    }
                    else
                    {
                        Binding field = metadata.WithValue(current);
                        if (field.Kind == EditorPropertyKind.Enum && TryReadListOptions(descriptor, out string[] liveOptions))
                        {
                            field.Options = liveOptions;
                            field.InspectorSupported = liveOptions.Length > 0;
                        }
                        bindings.Add(field);
                    }
                }
                catch (Exception error)
                {
                    ReportFieldFailure(data, rawKey, error);
                    bindings.RemoveAll(binding => binding.RawKey == rawKey);
                    bindings.Add(new Binding
                    {
                        RawKey = rawKey, Key = "managed." + rawKey, DisplayName = Humanize(rawKey),
                        Kind = EditorPropertyKind.ReadOnly, GizmoSupported = false,
                        CurrentValue = "<read failed: " + RootMessage(error) + ">"
                    });
                }
            }

            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning(
                "DevTool managed object protocol capture failed: " + error);
            bindings.Add(new Binding { RawKey = "__captureFailure", Key = "managed.__captureFailure",
                DisplayName = "Managed fields", Kind = EditorPropertyKind.ReadOnly, GizmoSupported = false,
                CurrentValue = "<read failed: " + RootMessage(error) + ">" });
            return true;
        }
    }

    private static void ReportFieldFailure(object data, string key, Exception error)
    {
        if (FailedFields.GetValue(data, _ => new HashSet<string>()).Add(key))
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool managed object field '" + key + "' failed: " + error);
    }

    private static void ValidateCurrentValue(object value)
    {
        bool valid = value switch
        {
            float scalar => Finite(scalar),
            double scalar => !double.IsNaN(scalar) && !double.IsInfinity(scalar),
            Vector2 vector => Finite(vector.x) && Finite(vector.y),
            Color color => Finite(color.r) && Finite(color.g) && Finite(color.b) && Finite(color.a),
            _ => true
        };
        if (!valid) throw new InvalidOperationException("Managed field contains non-finite author data; editing and gizmo projection are unavailable.");
    }

    private static Binding BuildBinding(
        object descriptor,
        string rawKey,
        object current,
        Type valueType)
    {
        Binding binding = new()
        {
            Descriptor = descriptor,
            RawKey = rawKey,
            Key = "managed." + rawKey,
            DisplayName = ReadString(descriptor, "displayName") ?? Humanize(rawKey),
            ValueType = valueType,
            CurrentValue = current,
            Step = 0.01f
        };

        if (string.IsNullOrWhiteSpace(binding.DisplayName))
            binding.DisplayName = Humanize(rawKey);

        string descriptorName = descriptor.GetType().Name;
        object controlType = ReadNamedMember(descriptor, "controlType");
        string geometry = controlType?.ToString()?.ToLowerInvariant() ?? string.Empty;
        object drivenType = ReadNamedMember(descriptor, "drivenControlType");

        if (valueType == typeof(float))
        {
            binding.Kind = EditorPropertyKind.Float;
            binding.InspectorSupported = true;
            ResolveRange(descriptor, binding, integer: false);
        }
        else if (valueType == typeof(int))
        {
            binding.Kind = EditorPropertyKind.Integer;
            binding.InspectorSupported = true;
            ResolveRange(descriptor, binding, integer: true);
        }
        else if (valueType == typeof(bool))
        {
            binding.Kind = EditorPropertyKind.Boolean;
            binding.InspectorSupported = true;
        }
        else if (valueType == typeof(string))
        {
            binding.Kind = EditorPropertyKind.String;
            binding.InspectorSupported = true;
        }
        else if (valueType == typeof(Vector2))
        {
            binding.Kind = EditorPropertyKind.Vector2;
            binding.InspectorSupported = true;
            binding.GizmoShape = ResolveGeometry(geometry);
            binding.GizmoScale = 1f;

            if (drivenType != null)
            {
                binding.DrivenType = drivenType.ToString();
                binding.DrivingKey = ReadString(descriptor, "keyOfOther");
                binding.GizmoSupported = !string.IsNullOrEmpty(binding.DrivingKey) && binding.DrivenType is
                    "relativeLine" or "perpendicularLine" or "perpendicularOval" or "rectangle";
                binding.GizmoShape = binding.GizmoSupported ? EditorPropertyGizmoShape.Line : EditorPropertyGizmoShape.None;
            }
        }
        else if (valueType == typeof(IntVector2))
        {
            binding.Kind = EditorPropertyKind.Vector2;
            binding.InspectorSupported = true;
            binding.IsIntVector = true;
            binding.GizmoShape = ResolveGeometry(geometry);
            binding.GizmoScale = 20f;
        }
        else if (valueType == typeof(Color))
        {
            binding.Kind = EditorPropertyKind.Color;
            binding.InspectorSupported = true;
        }
        else if (valueType?.IsEnum == true)
        {
            binding.Kind = EditorPropertyKind.Enum;
            // Managed enum fields may intentionally expose only a subset of the CLR enum. Prefer
            // the descriptor's own list contract so DryCycle cannot create a value the original
            // control would reject.
            binding.Options = TryReadListOptions(descriptor, out string[] enumOptions)
                ? enumOptions
                : Enum.GetNames(valueType);
            binding.InspectorSupported = binding.Options.Length > 0;
        }
        else if (valueType != null &&
                 !valueType.IsArray &&
                 TryReadListOptions(descriptor, out string[] options))
        {
            // ExtEnum-style fields expose their legal values through the same list protocol used by
            // the original control. Setting is delegated back to descriptor.FromString().
            binding.Kind = EditorPropertyKind.Enum;
            binding.Options = options;
            binding.InspectorSupported = options.Length > 0;
        }
        else
        {
            binding.Kind = EditorPropertyKind.ReadOnly;
            binding.InspectorSupported = false;
            binding.GizmoSupported = false;
        }

        // A vector descriptor that explicitly says "none" intentionally has no scene gizmo.
        if ((valueType == typeof(Vector2) || valueType == typeof(IntVector2)) &&
            drivenType == null && string.Equals(geometry, "none", StringComparison.OrdinalIgnoreCase))
            binding.GizmoShape = EditorPropertyGizmoShape.None;

        // Array/list fields commonly create multi-handle geometry. Do not lie about coverage until
        // the next phase's chain/polygon compiler is present.
        if (valueType?.IsArray == true ||
            descriptorName.IndexOf("Vector2List", StringComparison.OrdinalIgnoreCase) >= 0 ||
            descriptorName.IndexOf("Vector2Array", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            binding.InspectorSupported = false;
            binding.GizmoSupported = false;
            binding.Kind = EditorPropertyKind.ReadOnly;
        }

        return binding;
    }

    private static EditorPropertySnapshot Capture(Binding binding)
    {
        string group = binding.GizmoShape != EditorPropertyGizmoShape.None
            ? "Geometry"
            : "Fields";

        float x = 0f;
        float y = 0f;
        float z = 0f;
        float w = 0f;
        int integer = 0;
        bool boolean = false;
        string text = string.Empty;

        object current = binding.CurrentValue;
        switch (binding.Kind)
        {
            case EditorPropertyKind.Float:
                x = Convert.ToSingle(current, CultureInfo.InvariantCulture);
                break;

            case EditorPropertyKind.Integer:
                integer = Convert.ToInt32(current, CultureInfo.InvariantCulture);
                break;

            case EditorPropertyKind.Boolean:
                boolean = Convert.ToBoolean(current, CultureInfo.InvariantCulture);
                break;

            case EditorPropertyKind.String:
                text = current?.ToString() ?? string.Empty;
                break;

            case EditorPropertyKind.Vector2:
                if (current is Vector2 vector)
                {
                    x = vector.x;
                    y = vector.y;
                }
                else if (current is IntVector2 intVector)
                {
                    x = intVector.x;
                    y = intVector.y;
                }
                break;

            case EditorPropertyKind.Color:
                if (current is Color color)
                {
                    x = color.r;
                    y = color.g;
                    z = color.b;
                    w = color.a;
                }
                break;

            case EditorPropertyKind.Enum:
                text = current?.ToString() ?? string.Empty;
                integer = IndexOf(binding.Options, text);
                break;

            default:
                text = SerializeDescriptorValue(binding);
                break;
        }

        return new EditorPropertySnapshot
        {
            Key = binding.Key,
            DisplayName = binding.DisplayName,
            Group = group,
            Source = "Managed object protocol",
            Kind = binding.Kind,
            GizmoHint = binding.ArrayIndex < 0 && binding.DrivenType == null && binding.GizmoShape != EditorPropertyGizmoShape.None
                ? EditorPropertyGizmoHint.RelativePoint
                : EditorPropertyGizmoHint.None,
            GizmoShape = binding.GizmoShape,
            GizmoScale = binding.GizmoScale,
            HasRange = binding.HasRange,
            Min = binding.Min,
            Max = binding.Max,
            Step = binding.Step,
            Options = binding.Options ?? Array.Empty<string>(),
            X = x,
            Y = y,
            Z = z,
            W = w,
            IntegerValue = integer,
            BooleanValue = boolean,
            StringValue = text
        };
    }

    private static object ConvertEditorValue(
        Binding binding,
        EditorPropertyValue value)
    {
        switch (binding.Kind)
        {
            case EditorPropertyKind.Float:
                if (value.Kind != EditorPropertyKind.Float) return null;
                float number = value.X;
                if (!Finite(number)) return null;
                if (binding.HasRange)
                    number = Mathf.Clamp(number, binding.Min, binding.Max);
                return number;

            case EditorPropertyKind.Integer:
                if (value.Kind != EditorPropertyKind.Integer) return null;
                int integer = value.Integer;
                if (binding.HasRange)
                    integer = Mathf.Clamp(
                        integer,
                        Mathf.RoundToInt(binding.Min),
                        Mathf.RoundToInt(binding.Max));
                return integer;

            case EditorPropertyKind.Boolean:
                return value.Kind == EditorPropertyKind.Boolean
                    ? value.Boolean
                    : null;

            case EditorPropertyKind.String:
                return value.Kind == EditorPropertyKind.String
                    ? value.Text ?? string.Empty
                    : null;

            case EditorPropertyKind.Vector2:
                if (value.Kind != EditorPropertyKind.Vector2) return null;
                if (!Finite(value.X) || !Finite(value.Y)) return null;
                if (binding.IsIntVector)
                {
                    if ((double)value.X < int.MinValue || (double)value.X > int.MaxValue ||
                        (double)value.Y < int.MinValue || (double)value.Y > int.MaxValue) return null;
                    return new IntVector2(
                        Mathf.RoundToInt(value.X),
                        Mathf.RoundToInt(value.Y));
                }
                return new Vector2(value.X, value.Y);

            case EditorPropertyKind.Color:
                return value.Kind == EditorPropertyKind.Color && Finite(value.X) && Finite(value.Y) && Finite(value.Z) && Finite(value.W)
                    ? new Color(value.X, value.Y, value.Z, value.W)
                    : null;

            case EditorPropertyKind.Enum:
                if (value.Kind != EditorPropertyKind.Enum ||
                    value.Integer < 0 ||
                    value.Integer >= binding.Options.Length)
                    return null;

                string selected = binding.Options[value.Integer];
                if (binding.ValueType?.IsEnum == true)
                    return Enum.Parse(binding.ValueType, selected);

                MethodInfo parser = FindMethod(
                    binding.Descriptor.GetType(),
                    "FromString",
                    typeof(string));
                return parser?.Invoke(binding.Descriptor, new object[] { selected });
        }

        return null;
    }

    private static void ResolveRange(
        object descriptor,
        Binding binding,
        bool integer)
    {
        if (!TryReadNumber(descriptor, "min", out float min) ||
            !TryReadNumber(descriptor, "max", out float max))
            return;
        if (!Finite(min) || !Finite(max)) throw new InvalidOperationException("Managed field range contains non-finite bounds.");

        binding.HasRange = true;
        binding.Min = Math.Min(min, max);
        binding.Max = Math.Max(min, max);
        if (!integer && TryReadNumber(descriptor, "increment", out float step) && !Finite(step))
            throw new InvalidOperationException("Managed field increment is not finite.");
        binding.Step = integer
            ? 1f
            : TryReadNumber(descriptor, "increment", out float increment)
                ? Math.Max(0.000001f, Math.Abs(increment))
                : 0.01f;
    }

    private static EditorPropertyGizmoShape ResolveGeometry(string value)
    {
        switch (value)
        {
            case "line":
                return EditorPropertyGizmoShape.Line;
            case "circle":
                return EditorPropertyGizmoShape.Circle;
            case "rect":
                return EditorPropertyGizmoShape.Rectangle;
            case "tile":
                return EditorPropertyGizmoShape.Tile;
            case "fourdir":
                return EditorPropertyGizmoShape.Direction4;
            case "eightdir":
                return EditorPropertyGizmoShape.Direction8;
            default:
                return EditorPropertyGizmoShape.None;
        }
    }

    private static bool TryReadListOptions(
        object descriptor,
        out string[] options)
    {
        options = Array.Empty<string>();
        MethodInfo lowest = FindMethod(descriptor.GetType(), "LowestItem");
        MethodInfo highest = FindMethod(descriptor.GetType(), "HighestItem");
        MethodInfo values = FindMethod(
            descriptor.GetType(),
            "GetValues",
            typeof(int),
            typeof(int));
        if (lowest == null || highest == null || values == null)
            return false;

        // A present-but-failing list protocol is a field failure, not permission
        // to offer every CLR enum value. The caller records the original exception
        // and retains a read-only field plus the original representation boundary.
        int start = Convert.ToInt32(lowest.Invoke(descriptor, null), CultureInfo.InvariantCulture);
        int end = Convert.ToInt32(highest.Invoke(descriptor, null), CultureInfo.InvariantCulture);
        long count = (long)end - start;
        if (count < 0 || count > 512) throw new InvalidOperationException("Invalid managed field option count: " + count);
        object result = values.Invoke(descriptor, new object[] { start, end });
        if (result is not IEnumerable sequence) throw new InvalidOperationException("Managed field options are not enumerable.");
        var items = new List<string>();
        foreach (object item in sequence)
        {
            if (items.Count >= 512 || item is not string text)
                throw new InvalidOperationException("Invalid managed field option entry.");
            items.Add(text);
        }
        options = items.ToArray();
        return true;
    }

    private static bool HasUncoveredAuthoredMembers(
        Type dataType,
        HashSet<string> keys)
    {

        Type current = dataType;
        while (current != null && current != typeof(PlacedObject.Data))
        {
            FieldInfo[] fields = current.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic ||
                    string.Equals(field.Name, "fields", StringComparison.Ordinal) ||
                    string.Equals(field.Name, "panelPos", StringComparison.Ordinal) ||
                    keys.Contains(field.Name))
                    continue;

                if (IsSimpleAuthoredType(field.FieldType))
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsSimpleAuthoredType(Type type) =>
        type == typeof(float) ||
        type == typeof(int) ||
        type == typeof(bool) ||
        type == typeof(string) ||
        type == typeof(Vector2) ||
        type == typeof(IntVector2) ||
        type == typeof(Color) ||
        type?.IsEnum == true;

    private static bool TryFindBinding(
        List<Binding> bindings,
        string key,
        out Binding binding)
    {
        for (int i = 0; i < bindings.Count; i++)
        {
            if (string.Equals(bindings[i].Key, key, StringComparison.Ordinal))
            {
                binding = bindings[i];
                return true;
            }
        }

        binding = null;
        return false;
    }

    private static object ReadManagedValue(
        object data,
        DataSchema schema,
        string key)
    {
        return schema.GetValue.Invoke(data, new object[] { key });
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void IncludeBackedMemberNames(object data, DataSchema schema, HashSet<string> names)
    {
        if (schema.BackedFieldsMember == null) return;
        if (ReadMember(schema.BackedFieldsMember, data) is not IDictionary map) return;
        foreach (DictionaryEntry entry in map)
            if (entry.Key is string key && names.Contains(key) && entry.Value is FieldInfo field &&
                !field.IsStatic && field.DeclaringType.IsInstanceOfType(data))
                names.Add(field.Name);
    }

    // Compile the declared data geometry ourselves; never ask a mod to construct editor nodes.
    private static Vector2 ProjectDriven(PlacedObject.Data data, DataSchema schema, Binding binding, Vector2 input)
    {
        if (binding.DrivenType == "relativeLine") return input;
        if (ReadManagedValue(data, schema, binding.DrivingKey) is not Vector2 other)
            throw new InvalidOperationException("Driving vector is unavailable: " + binding.DrivingKey);
        return PerpendicularRadius(other, input);
    }

    private static Vector2 PerpendicularRadius(Vector2 other, Vector2 input)
    {
        double length = Math.Sqrt((double)input.x * input.x + (double)input.y * input.y);
        double driverLength = Math.Sqrt((double)other.x * other.x + (double)other.y * other.y);
        Vector2 result = driverLength < 0.00001 ? Vector2.zero :
            new Vector2((float)(-other.y / driverLength * length), (float)(other.x / driverLength * length));
        ValidateCurrentValue(result);
        return result;
    }

    internal static EditorObjectGizmoSnapshot CaptureCompositeGeometry(PlacedObject target, int objectIndex)
    {
        if (target?.data == null) return EditorObjectGizmoSnapshot.Empty;
        DataSchema schema = Schemas.GetOrAdd(target.data.GetType(), BuildSchema);
        if (!schema.Valid || !TryBuildBindings(target.data, schema, out List<Binding> bindings))
            return EditorObjectGizmoSnapshot.Empty;
        List<EditorObjectGizmoHandleSnapshot> handles = new();
        List<EditorObjectLineSegmentSnapshot> lines = new();
        HashSet<string> emitted = new(StringComparer.Ordinal);
        foreach (Binding binding in bindings)
        {
            try
            {
                if (binding.DrivenType != null && binding.GizmoSupported && binding.InspectorSupported &&
                    binding.CurrentValue is Vector2 radius &&
                    ReadManagedValue(target.data, schema, binding.DrivingKey) is Vector2 driver)
                {
                    Vector2 side = ProjectDriven(target.data, schema, binding, radius);
                    Vector2 anchor = target.pos + (binding.DrivenType == "relativeLine" ? driver : Vector2.zero);
                    Vector2 end = anchor + side;
                    if (!Finite(end.x) || !Finite(end.y)) continue;
                    handles.Add(new EditorObjectGizmoHandleSnapshot { Id = "property:" + binding.Key,
                        X = end.x, Y = end.y, AnchorX = anchor.x, AnchorY = anchor.y,
                        DrawAnchorLine = binding.DrivenType is "relativeLine" or "perpendicularLine" });
                    void Line(Vector2 a, Vector2 b)
                    {
                        if (Finite(a.x) && Finite(a.y) && Finite(b.x) && Finite(b.y))
                            lines.Add(new EditorObjectLineSegmentSnapshot { X0 = a.x, Y0 = a.y, X1 = b.x, Y1 = b.y });
                    }
                    if (binding.DrivenType == "rectangle")
                    {
                        Line(target.pos, target.pos + driver); Line(target.pos + driver, target.pos + driver + side);
                        Line(target.pos + driver + side, target.pos + side); Line(target.pos + side, target.pos);
                    }
                    else if (binding.DrivenType == "perpendicularOval")
                    {
                        Vector2 previous = target.pos + driver;
                        for (int i = 1; i <= 48; i++)
                        {
                            double angle = i * Math.PI / 24;
                            Vector2 point = target.pos + driver * (float)Math.Cos(angle) + side * (float)Math.Sin(angle);
                            Line(previous, point); previous = point;
                        }
                    }
                    continue;
                }
                if (binding.ArrayIndex < 0 || !binding.GizmoSupported || !emitted.Add(binding.RawKey)) continue;
                Vector2[] points = ReadManagedValue(target.data, schema, binding.RawKey) as Vector2[];
                if (points == null || points.Length == 0) continue;
                bool includeParent = ReadNamedMember(binding.Descriptor, "IncludeParent") is true;
                bool polygon = ReadString(binding.Descriptor, "RepresentationType") == "Polygon";
                Vector2 At(int i) => target.pos + (i == 0 && includeParent ? Vector2.zero : points[i]);
                bool valid = true;
                for (int i = 0; i < points.Length; i++)
                { Vector2 point = At(i); if (!Finite(point.x) || !Finite(point.y)) { valid = false; break; } }
                if (!valid) continue;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 point = At(i);
                    if (!(includeParent && i == 0))
                        handles.Add(new EditorObjectGizmoHandleSnapshot {
                            Id = "property:managed." + binding.RawKey + "[" + i + "]",
                            X = point.x, Y = point.y, AnchorX = target.pos.x, AnchorY = target.pos.y });
                    if (i == 0 && (!polygon || points.Length < 2)) continue;
                    Vector2 previous = At(i == 0 ? points.Length - 1 : i - 1);
                    lines.Add(new EditorObjectLineSegmentSnapshot { X0 = previous.x, Y0 = previous.y, X1 = point.x, Y1 = point.y });
                }
            }
            catch (Exception error) { ReportFieldFailure(target.data, binding.RawKey, error); }
        }
        return new EditorObjectGizmoSnapshot { ObjectIndex = objectIndex,
            ObjectStableId = ObjectPresentationIdentity.Get(target), Handles = handles.ToArray(), Lines = lines.ToArray() };
    }

    private static bool HasCustomControlBehavior(object descriptor, Type framework)
    {
        if (framework == null) return false;
        foreach (MethodInfo method in descriptor.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (method.IsVirtual && method.DeclaringType.DeclaringType != framework &&
                method.Name is "ParseFromText" or "NewFactor" or "Next" or "Prev" or "FromString")
                return true;
        return false;
    }

    private static object ReadMember(MemberInfo member, object target) =>
        member switch
        {
            FieldInfo field => field.GetValue(target),
            PropertyInfo property => property.GetValue(target, null),
            _ => null
        };

    private static object ReadNamedMember(object target, string name)
    {
        if (target == null || string.IsNullOrEmpty(name))
            return null;

        FieldInfo field = FindField(target.GetType(), name);
        if (field != null)
            return field.GetValue(target);

        PropertyInfo property = FindProperty(target.GetType(), name);
        return property?.GetValue(target, null);
    }

    private static string ReadString(object target, string name) =>
        ReadNamedMember(target, name)?.ToString();

    private static bool TryReadNumber(
        object target,
        string name,
        out float value)
    {
        value = 0f;
        object raw = ReadNamedMember(target, name);
        if (raw == null)
            return false;

        try
        {
            value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
            type = type.BaseType;
        }

        return null;
    }

    private static PropertyInfo FindProperty(Type type, string name)
    {
        while (type != null)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null)
                return property;
            type = type.BaseType;
        }

        return null;
    }

    private static MethodInfo FindGenericMethod(
        Type type,
        string name,
        int parameterCount)
    {
        while (type != null)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == name &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    parameters.Length == parameterCount &&
                    parameters.Length > 0 &&
                    parameters[0].ParameterType == typeof(string))
                    return method;
            }

            type = type.BaseType;
        }

        return null;
    }

    private static MethodInfo FindMethod(
        Type type,
        string name,
        params Type[] parameters)
    {
        while (type != null)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
                null,
                parameters ?? Type.EmptyTypes,
                null);
            if (method != null)
                return method;
            type = type.BaseType;
        }

        return null;
    }

    private static string SerializeDescriptorValue(Binding binding)
    {
        if (binding?.Descriptor == null)
            return binding?.CurrentValue?.ToString() ?? string.Empty;

        try
        {
            MethodInfo serializer = FindMethod(
                binding.Descriptor.GetType(),
                "ToString",
                typeof(object));
            return serializer?.Invoke(
                       binding.Descriptor,
                       new[] { binding.CurrentValue })?.ToString()
                   ?? binding.CurrentValue?.ToString()
                   ?? string.Empty;
        }
        catch
        {
            return binding.CurrentValue?.ToString() ?? string.Empty;
        }
    }

    private static int IndexOf(string[] values, string value)
    {
        if (values == null || values.Length == 0)
            return 0;

        for (int i = 0; i < values.Length; i++)
            if (string.Equals(values[i], value, StringComparison.Ordinal))
                return i;
        return 0;
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        System.Text.StringBuilder text = new();
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (i > 0 &&
                char.IsUpper(current) &&
                !char.IsUpper(value[i - 1]))
                text.Append(' ');
            text.Append(current);
        }

        return text.ToString();
    }

    private static string RootMessage(Exception error)
    {
        Exception current = error;
        while (current is TargetInvocationException invocation &&
               invocation.InnerException != null)
            current = invocation.InnerException;
        return current?.Message ?? "unknown error";
    }
}

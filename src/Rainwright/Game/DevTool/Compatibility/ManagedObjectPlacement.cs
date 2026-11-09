using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DryCycle.DevUI.DevTool.Compatibility;

/// <summary>
/// Optional POM model/runtime protocol. Never constructs a Page, Representation or editor control.
/// Calls the registered runtime factory so custom factories and single-instance rules remain valid.
/// Discovery runs on placement, not on the render thread or on each frame.
/// </summary>
internal static class ManagedObjectPlacement
{
    private sealed class Protocol
    {
        internal MethodInfo Resolve, Data, Runtime;
        internal FieldInfo SingleInstance, ObjectType;
    }
    private sealed class Binding
    {
        internal object Factory;
        internal Protocol Protocol;
        internal UpdatableAndDeletable Runtime;
        internal bool Attempted;
        internal bool SingleInstance;
        internal global::Room Room;
        internal Type RuntimeType;
        internal readonly List<UpdatableAndDeletable> Owned = new();
    }
    private static readonly List<Protocol> protocols = new();
    private static int assemblyCount = -1;
    private static ConditionalWeakTable<PlacedObject, Binding> bindings = new();
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> runtimeLinks = new();

    internal static bool InitializeData(PlacedObject target)
    {
        if (target?.type == null) return false;
        Discover();
        // Embedded/forked POM implementations can use a different namespace. A model that
        // already came from GenerateEmptyData identifies its own framework without a type scan.
        for (Type dataType = target.data?.GetType(); dataType != null && dataType != typeof(PlacedObject.Data); dataType = dataType.BaseType)
            AddProtocol(dataType.DeclaringType);
        foreach (Protocol protocol in protocols)
        {
            object factory = Invoke(protocol.Resolve, null, target.type);
            if (factory == null) continue;
            // GenerateEmptyData hooks normally already supplied the final data. Do not initialize
            // it twice: some factories allocate random seeds or retain external author state.
            if (target.data == null || target.data.GetType() == typeof(PlacedObject.Data))
                target.data = Invoke(protocol.Data, factory, target) as PlacedObject.Data ?? target.data;
            if (!bindings.TryGetValue(target, out Binding existing) || !ReferenceEquals(existing.Factory, factory))
            {
                bindings.Remove(target);
                bindings.Add(target, new Binding { Factory = factory, Protocol = protocol,
                    RuntimeType = protocol.ObjectType?.GetValue(factory) as Type,
                    SingleInstance = protocol.SingleInstance?.GetValue(factory) is true });
            }
            return true;
        }
        return false;
    }

    internal static bool HasRegistration(PlacedObject.Type type)
    {
        Discover();
        foreach (Protocol protocol in protocols)
            if (Invoke(protocol.Resolve, null, type) != null) return true;
        return false;
    }

    private static void Discover()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (assemblyCount != assemblies.Length)
        {
            protocols.Clear();
            foreach (Assembly assembly in assemblies)
            {
                Type framework = assembly.GetType("Pom.Pom", false) ?? assembly.GetType("POM.POM", false);
                AddProtocol(framework);
            }
            assemblyCount = assemblies.Length;
        }
    }

    private static void AddProtocol(Type framework)
    {
        Type manager = framework?.GetNestedType("ManagedObjectType", BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo resolve = framework?.GetMethod("GetManagerForType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(PlacedObject.Type) }, null);
        MethodInfo data = manager?.GetMethod("MakeEmptyData", new[] { typeof(PlacedObject) });
        MethodInfo runtime = manager?.GetMethod("MakeObject", new[] { typeof(PlacedObject), typeof(global::Room) });
        if (resolve == null || data == null || runtime == null) return;
        foreach (Protocol known in protocols) if (known.Resolve == resolve) return;
        protocols.Add(new Protocol { Resolve = resolve, Data = data, Runtime = runtime,
            SingleInstance = manager.GetField("singleInstance", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            ObjectType = manager.GetField("objectType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) });
    }

    internal static void EnsureRuntime(global::Room room, PlacedObject target)
    {
        if (room == null || target == null) return;
        if (!bindings.TryGetValue(target, out Binding binding))
        {
            if (!InitializeData(target)) return;
            bindings.TryGetValue(target, out binding);
        }
        if (binding.Room != null && !ReferenceEquals(binding.Room, room))
        { binding.Runtime = null; binding.Owned.Clear(); binding.Attempted = false; }
        binding.Room = room;
        if (binding.Attempted) return;
        if (TryAdopt(room, target, binding)) return;
        HashSet<UpdatableAndDeletable> before = room.updateList == null ? null : new(room.updateList);
        bool first = room.abstractRoom.firstTimeRealized;
        try
        {
            room.abstractRoom.firstTimeRealized = true;
            binding.Runtime = Invoke(binding.Protocol.Runtime, binding.Factory, target, room) as UpdatableAndDeletable;
            if (binding.Runtime != null && room.updateList?.Contains(binding.Runtime) != true) room.AddObject(binding.Runtime);
            if (room.updateList != null)
                foreach (UpdatableAndDeletable item in room.updateList)
                    if (before?.Contains(item) != true) binding.Owned.Add(item);
            binding.Attempted = true;
        }
        catch
        {
            // A factory can AddObject helpers before throwing. Roll back its synchronous additions,
            // preserve the original exception, and leave pre-existing room objects untouched.
            if (room.updateList != null && before != null)
                foreach (UpdatableAndDeletable item in room.updateList.ToArray())
                    if (!before.Contains(item))
                        try { item.Destroy(); room.RemoveObject(item); }
                        catch (Exception cleanup) { Core.EditorDiagnostics.LogSource?.LogWarning("Managed runtime rollback failed: " + cleanup); }
            binding.Runtime = null; binding.Owned.Clear();
            throw;
        }
        finally { room.abstractRoom.firstTimeRealized = first; }
    }

    internal static void AdoptRuntime(global::Room room, PlacedObject target)
    {
        if (room == null || target == null) return;
        if (!bindings.TryGetValue(target, out Binding binding))
        {
            if (!InitializeData(target)) return;
            bindings.TryGetValue(target, out binding);
        }
        if (!binding.Attempted && TryAdopt(room, target, binding)) binding.Room = room;
    }

    private static bool TryAdopt(global::Room room, PlacedObject target, Binding binding)
    {
        if (room.updateList == null) return false;
        foreach (UpdatableAndDeletable item in room.updateList)
        {
            if (item == null || item.slatedForDeletetion) continue;
            if (binding.RuntimeType != null && !binding.RuntimeType.IsInstanceOfType(item)) continue;
            bool matches = binding.SingleInstance && binding.RuntimeType?.IsInstanceOfType(item) == true;
            if (!matches)
                foreach (FieldInfo link in runtimeLinks.GetOrAdd(item.GetType(), FindRuntimeLinks))
                {
                    object value = link.GetValue(item);
                    if (ReferenceEquals(value, target) || target.data != null && ReferenceEquals(value, target.data))
                    { matches = true; break; }
                }
            if (!matches) continue;
            binding.Runtime = item; binding.Attempted = true;
            return true;
        }
        return false;
    }

    private static FieldInfo[] FindRuntimeLinks(Type type)
    {
        List<FieldInfo> result = new();
        for (; type != null && type != typeof(UpdatableAndDeletable); type = type.BaseType)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (typeof(PlacedObject).IsAssignableFrom(field.FieldType) || typeof(PlacedObject.Data).IsAssignableFrom(field.FieldType)) result.Add(field);
        return result.ToArray();
    }

    internal static void RemoveRuntime(global::Room room, PlacedObject target)
    {
        if (room == null || target == null) return;
        AdoptRuntime(room, target);
        if (!bindings.TryGetValue(target, out Binding binding)) return;
        if (binding.Runtime != null)
        {
            if (binding.SingleInstance && room.roomSettings?.placedObjects != null)
                foreach (PlacedObject sibling in room.roomSettings.placedObjects)
                    if (!ReferenceEquals(sibling, target) && sibling.active && InitializeData(sibling) && bindings.TryGetValue(sibling, out Binding other) &&
                        ReferenceEquals(other.Factory, binding.Factory))
                    {
                        // Room-wide controllers still serve other authored instances. Transfer
                        // ownership so deleting the final instance can remove it exactly once.
                        other.Runtime = binding.Runtime; other.Attempted = true;
                        other.Room = room;
                        other.Owned.AddRange(binding.Owned); binding.Owned.Clear();
                        binding.Runtime = null; binding.Attempted = false;
                        return;
                    }
            binding.Runtime.Destroy();
            room.RemoveObject(binding.Runtime);
            binding.Runtime = null;
        }
        foreach (UpdatableAndDeletable item in binding.Owned)
            if (!item.slatedForDeletetion) { item.Destroy(); room.RemoveObject(item); }
        binding.Owned.Clear();
        binding.Attempted = false; // Undo/redo retains the same authored object identity.
    }

    private static object Invoke(MethodInfo method, object instance, params object[] args)
    {
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    internal static void Reset()
    { protocols.Clear(); assemblyCount = -1; } // Weak ownership survives an editor close/reopen while the room stays alive.
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DevInterface;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Compatibility;

/// <summary>
/// Generic DevInterface semantic bridge.
///
/// This is deliberately not a RegionKit adapter table. It recognizes control protocols:
/// Slider/Cycler/IntegerControl, ButtonWithSelectPanel, actualValue-backed bool/string/color
/// buttons, ExtEnum-valued buttons, text committers, direction pickers and finally arbitrary
/// Buttons through their original Clicked() boundary. A button that opens a custom legacy panel
/// remains visible in ImGui; after it is clicked the newly-created child controls are recursively
/// mirrored on the next publication, so custom panels do not require one adapter per mod/type.
/// </summary>
public static class LegacyDevInterfaceBridge
{
    private sealed class NodeIdentity { internal readonly long Value = Interlocked.Increment(ref nextNodeIdentity); }
    private static long nextNodeIdentity;
    private static readonly ConditionalWeakTable<DevUINode, NodeIdentity> NodeIdentities = new();
    private static readonly ConditionalWeakTable<DevUINode, HashSet<string>> ReadFailures = new();
    private static readonly ConditionalWeakTable<DevUINode, object> SnapshotFailures = new();
    private const int NodeBudget = 4096;

    internal static string NodePath(DevUINode node, string path) =>
        (path ?? string.Empty) + "~" + NodeIdentities.GetValue(node, _ => new NodeIdentity()).Value;

    internal static bool IsValidParentChain(DevUINode node)
    {
        // Parent links form one chain: Floyd detection avoids a HashSet allocation
        // for every control capability/position check while retaining the same budget.
        var slow = node;
        var fast = node;
        int count = 0;
        for (var current = node; current != null; current = current.parentNode)
        {
            if (++count > NodeBudget) return false;
            slow = slow?.parentNode;
            fast = fast?.parentNode?.parentNode;
            if (fast != null && ReferenceEquals(slow, fast)) return false;
        }
        return true;
    }

    private static bool IsSafeUpdateTree(DevUINode root)
    {
        var seen = new HashSet<DevUINode>();
        var pending = new Stack<DevUINode>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node == null) continue;
            if (seen.Count >= NodeBudget || !seen.Add(node)) return false;
            if (node.subNodes != null)
                for (int i = 0; i < node.subNodes.Count; i++) pending.Push(node.subNodes[i]);
        }
        return true;
    }

    private readonly struct TraversalFrame
    {
        internal readonly DevUINode Parent;
        internal readonly string Path;
        internal readonly int Index;
        internal TraversalFrame(DevUINode parent, string path, int index) { Parent = parent; Path = path; Index = index; }
    }

    // Iterative DFS preserves original child order and indices without a CLR-stack dependency.
    // Yield one node at a time: external getters can rebuild subNodes during publication.
    internal static IEnumerable<(DevUINode Node, string Path)> WalkChildren(DevUINode root, bool strict = false)
    {
        var seen = new HashSet<DevUINode> { root };
        var stack = new Stack<TraversalFrame>();
        stack.Push(new(root, string.Empty, 0));
        int remaining = NodeBudget;
        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.Parent?.subNodes == null || frame.Index >= frame.Parent.subNodes.Count) continue;
            if (--remaining < 0) throw new InvalidOperationException("Object control tree exceeds the 4096-node capture budget.");
            DevUINode child = frame.Parent.subNodes[frame.Index];
            stack.Push(new(frame.Parent, frame.Path, frame.Index + 1));
            if (child == null) continue;
            if (!seen.Add(child))
            {
                var error = new InvalidOperationException("Cyclic or shared original control node: " + child.IDstring);
                if (strict) throw error;
                ReportReadFailure(child, "control tree cycle", error);
                continue;
            }
            string path = string.IsNullOrEmpty(frame.Path) ? frame.Index.ToString() : frame.Path + "." + frame.Index;
            bool traverse = true;
            try { traverse = ShouldTraverseChild(frame.Parent, child); }
            catch (Exception error) { ReportReadFailure(frame.Parent, "child capability", error); }
            if (!traverse) continue;
            yield return (child, path);
            stack.Push(new(child, path, 0));
        }
    }
    private const string CyclerActionPrefix = "@cycler|";
    private const string ExtEnumActionPrefix = "@ext-enum|";
    private const string IntegerActionPrefix = "@integer|";
    private const string SelectActionPrefix = "@select|";
    private const string PanelSelectActionPrefix = "@panel-select|";

    internal static LegacyControlSnapshot[] Capture(global::DevInterface.DevUI owner, PlacedObject target)
    {
        if (owner?.activePage is not ObjectsPage page || target == null)
            return Array.Empty<LegacyControlSnapshot>();

        PlacedObjectRepresentation representation = FindRepresentation(page, target);
        return representation == null ? Array.Empty<LegacyControlSnapshot>() : CaptureRoot(representation);
    }

    // Only operates on an already visible, explicitly selected original Objects page.
    // Native mode has no path that materializes a page or representation through this bridge.
    internal static bool RunOnVisibleObject(global::DevInterface.DevUI owner, PlacedObject target,
        Func<PlacedObjectRepresentation, bool> action)
    {
        PlacedObjectRepresentation representation = ResolveLiveRepresentation(owner, target);
        return representation != null && action != null && action(representation);
    }

    private static PlacedObjectRepresentation ResolveLiveRepresentation(
        global::DevInterface.DevUI owner,
        PlacedObject target) =>
        FindRepresentation(owner?.activePage as ObjectsPage, target);

    /// <summary>
    /// Generic entry point used by coverage/tests and by future page-level mirrors. The root itself
    /// is treated as a container; paths are relative to its subNodes collection.
    /// </summary>
    internal static LegacyControlSnapshot[] CaptureRoot(DevUINode root)
    {
        if (root == null) return Array.Empty<LegacyControlSnapshot>();

        List<LegacyControlSnapshot> result = new();
        try
        {
            foreach (var entry in WalkChildren(root))
            {
                try { SnapshotFailures.Remove(entry.Node); CaptureControl(entry.Node, entry.Path, result); }
                catch (Exception error)
                {
                    ReportReadFailure(entry.Node, "control snapshot", error);
                    SnapshotFailures.GetValue(entry.Node, _ => new object());
                    result.Add(Snapshot(entry.Path, entry.Node, entry.Node.IDstring ?? "Control", LegacyControlKind.ReadOnly,
                        valueText: "Control unavailable: " + error.GetBaseException().Message));
                }
            }
        }
        catch (Exception error)
        {
            ReportReadFailure(root, "control traversal", error);
            result.Add(Snapshot(string.Empty, root, "Compatibility", LegacyControlKind.ReadOnly,
                valueText: error.GetBaseException().Message));
        }
        return result.ToArray();
    }

    public static string CyclerAction(string path, int selectedIndex) =>
        CyclerActionPrefix + selectedIndex + "|" + (path ?? string.Empty);

    public static string ExtEnumAction(string path, int selectedIndex) =>
        ExtEnumActionPrefix + selectedIndex + "|" + (path ?? string.Empty);

    public static string IntegerAction(string path, int change) =>
        IntegerActionPrefix + change + "|" + (path ?? string.Empty);

    public static string SelectAction(string path, int selectedIndex) =>
        SelectActionPrefix + selectedIndex + "|" + (path ?? string.Empty);

    public static string PanelSelectAction(string path, int selectedIndex) =>
        PanelSelectActionPrefix + selectedIndex + "|" + (path ?? string.Empty);

    /// <summary>
    /// Structural capability probe. No namespace or concrete third-party type is involved.
    /// </summary>
    internal static bool CanAdaptNode(DevUINode node)
    {
        try { return CanAdaptNodeCore(node); }
        catch (Exception error) { ReportReadFailure(node, "capability discovery", error); return false; }
    }

    private static bool CanAdaptNodeCore(DevUINode node)
    {
        if (node == null) return false;
        if (node is Slider || node is Cycler || node is IntegerControl || node is ArrowButton) return true;
        if (node is ButtonWithSelectPanel) return true; // combo when discoverable, generic button otherwise
        if (CanAdaptBoolean(node) || CanAdaptExtEnum(node) || CanAdaptPanelSelect(node) ||
            CanAdaptColorSelect(node) || CanAdaptText(node) || CanAdaptDirection(node))
            return true;
        return node is Button button && !IsInfrastructureButton(button);
    }

    internal static bool IsAtomicAdaptedControl(DevUINode node)
    {
        try { return IsAtomicAdaptedControlCore(node); }
        catch (Exception error) { ReportReadFailure(node, "child discovery", error); return false; }
    }

    private static bool IsAtomicAdaptedControlCore(DevUINode node)
    {
        // Panels created by a semantic control are authoring UI, not its nub/label chrome.
        // Keep diagnostics and capture recursive when any such child is present.
        if (node?.subNodes != null)
            for (int i = 0; i < node.subNodes.Count; i++)
                if (ShouldTraverseControlChild(node, node.subNodes[i])) return false;
        if (node is Slider || node is Cycler || node is IntegerControl) return true;
        if (node is ButtonWithSelectPanel select && CanAdaptSelect(select)) return true;
        return CanAdaptBoolean(node) || CanAdaptExtEnum(node) || CanAdaptPanelSelect(node) ||
               CanAdaptColorSelect(node) || CanAdaptText(node) || CanAdaptDirection(node);
    }

    internal static bool ShouldTraverseControlChild(DevUINode parent, DevUINode child)
    {
        if (child == null) return false;
        if (parent is ButtonWithSelectPanel select && CanAdaptSelect(select) && ReferenceEquals(child, select.selectPanel)) return false;
        if (child is Panel) return true;
        if (child is DevUILabel && !CanAdaptText(child)) return false;
        if (child is Slider.SliderNub) return false;
        if (child.GetType().Assembly == typeof(DevUINode).Assembly &&
            (child is ArrowButton || child is Handle ||
             child is Button && (parent is Slider || parent is Cycler || parent is IntegerControl)))
            return false;
        return true;
    }

    internal static bool ShouldTraverseChild(DevUINode parent, DevUINode child) =>
        !(parent is Slider || parent is Cycler || parent is IntegerControl ||
          parent is ButtonWithSelectPanel select && CanAdaptSelect(select) ||
          CanAdaptBoolean(parent) || CanAdaptExtEnum(parent) || CanAdaptPanelSelect(parent) ||
          CanAdaptColorSelect(parent) || CanAdaptText(parent) || CanAdaptDirection(parent)) ||
        ShouldTraverseControlChild(parent, child);

    /// <summary>
    /// Conservative structural proof used by the headless host. A Representation is considered
    /// fully mirrored only when every descendant is either a known semantic control, a passive
    /// container/label, or a world-space Handle. Unknown custom DevUINodes remain a compatibility
    /// gap even when their PlacedObject.Data happens to be fully reflected.
    /// </summary>
    internal static bool HasUnsupportedNodes(DevUINode root, out string example)
    {
        example = string.Empty;
        if (root == null)
        {
            example = "missing representation";
            return true;
        }

        // The Representation itself is an authoring node too. A third-party subclass can put
        // interaction directly in Update() without creating a child control; validating only
        // descendants would incorrectly certify that object as fully migrated.
        try
        {
            if (!IsStructurallyCoveredNode(root))
            {
                example = "root " + root.GetType().FullName;
                return true;
            }
            foreach (var entry in WalkChildren(root, strict: true))
            {
                if (!IsStructurallyCoveredNode(entry.Node))
                {
                    example = entry.Path + " " + entry.Node.GetType().FullName;
                    return true;
                }
            }
            return false;
        }
        catch (Exception error)
        {
            ReportReadFailure(root, "compatibility audit", error);
            example = "control audit failed: " + error.GetBaseException().Message;
            return true;
        }
    }
    private static bool IsStructurallyCoveredNode(DevUINode node)
    {
        if (node == null)
            return true;
        if (SnapshotFailures.TryGetValue(node, out _)) return false;

        if (CanAdaptNode(node))
            return true;

        // Non-authoring infrastructure is intentionally not mirrored as a user control.
        if (node is Button infrastructureButton && IsInfrastructureButton(infrastructureButton))
            return true;

        // Containers are covered by recursively mirroring their descendants, but a subclass may
        // also hide authoring input directly in Update(). Only framework Update implementations are
        // assumed safe. Unknown custom Update() logic keeps the explicit legacy escape hatch.
        if (node is Page)
            return true;
        if (node is PlacedObjectRepresentation representation)
            return UsesOnlyFrameworkUpdate(
                representation,
                typeof(PlacedObjectRepresentation),
                typeof(Handle),
                typeof(PositionedDevUINode),
                typeof(DevUINode));
        if (node is Panel panel)
            return UsesOnlyFrameworkUpdate(
                panel,
                typeof(Panel),
                typeof(RectangularDevUINode),
                typeof(PositionedDevUINode),
                typeof(DevUINode));
        if (node is Handle handle)
            return UsesOnlyFrameworkUpdate(
                handle,
                typeof(Handle),
                typeof(PositionedDevUINode),
                typeof(DevUINode));
        if (node is DevUILabel)
            return true;

        // Composite control frameworks commonly wrap real Buttons/Sliders/Handles in a plain
        // PositionedDevUINode. That wrapper is safe to recurse through only when it has no custom
        // per-frame Update() semantics of its own. This covers structural managed button/select and
        // multi-point holders without naming any ecosystem or concrete third-party type.
        if (node is PositionedDevUINode positioned && IsPassiveCompositeNode(positioned))
            return true;

        // Anything else is deliberately conservative. In particular, a custom PositionedDevUINode
        // that overrides Update() may implement mouse/keyboard interaction without inheriting
        // Button/Handle. Treating it as passive would silently discard third-party authoring
        // semantics, so it must keep the explicit legacy fallback available.
        return false;
    }

    private static bool IsPassiveCompositeNode(PositionedDevUINode node) =>
        UsesOnlyFrameworkUpdate(
            node,
            typeof(RectangularDevUINode),
            typeof(PositionedDevUINode),
            typeof(DevUINode));

    private static bool UsesOnlyFrameworkUpdate(
        DevUINode node,
        params Type[] allowedDeclaringTypes)
    {
        if (node == null)
            return false;

        MethodInfo update;
        try
        {
            update = node.GetType().GetMethod(
                nameof(DevUINode.Update),
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
        }
        catch
        {
            // Reflection ambiguity/failure is itself a reason not to claim full compatibility.
            return false;
        }

        if (update == null)
            return true;

        // A non-virtual method with the same name cannot participate in DevUINode's virtual update
        // dispatch. Walk back to the inherited virtual implementation instead of treating a harmless
        // method hide as an authoring surface.
        Type declaring = update.DeclaringType;
        if (!update.IsVirtual)
        {
            Type current = node.GetType().BaseType;
            while (current != null && typeof(DevUINode).IsAssignableFrom(current))
            {
                MethodInfo inherited;
                try
                {
                    inherited = current.GetMethod(
                        nameof(DevUINode.Update),
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);
                }
                catch
                {
                    return false;
                }
                if (inherited != null && inherited.IsVirtual)
                {
                    declaring = inherited.DeclaringType;
                    break;
                }
                current = current.BaseType;
            }
        }

        for (int i = 0; i < allowedDeclaringTypes.Length; i++)
            if (declaring == allowedDeclaringTypes[i])
                return true;

        return false;
    }

    internal static bool CanAdaptBoolean(DevUINode node) =>
        node is Button && TryReadBoolMember(node, "actualValue", out _);

    internal static bool CanAdaptExtEnum(DevUINode node) =>
        TryReadExtEnum(node, out _, out _, out _, out _);

    /// <summary>
    /// Kept for the existing coverage API. Any ordinary Button is a valid semantic boundary:
    /// Clicked() is the legacy contract. Composite buttons are not assumed to be leaf controls;
    /// their newly-created child panels are recursively scanned as well.
    /// </summary>
    internal static bool IsTerminalSemanticButton(DevUINode node) =>
        node is ArrowButton ||
        (node is Button button && !IsInfrastructureButton(button));

    internal static bool CanAdaptSelect(ButtonWithSelectPanel button) =>
        // Never invoke a panel factory during read-only publication. Custom panels retain
        // their actual buttons/search/paging callbacks instead of being flattened into a combo.
        button?.selectPanel?.GetType() == typeof(SelectPanel) && button.selectPanel.items?.Length > 0;

    internal static bool CanAdaptPanelSelect(DevUINode node) =>
        node is Button &&
        FindValueCommitMethod(node.GetType(), typeof(string)) != null &&
        TryReadStringOptions(node, "values", out string[] options) && options.Length > 0 &&
        TryReadStringMember(node, "actualValue", out _);

    internal static bool CanAdaptColorSelect(DevUINode node) =>
        node is Button && FindValueCommitMethod(node.GetType(), typeof(Color)) != null && TryReadColor(node, out _);

    private static MethodInfo FindValueCommitMethod(Type type, Type valueType) =>
        FindCommitMethod(type, "SetValue", new[] { valueType });

    private static MethodInfo FindCommitMethod(Type type, string name, Type[] parameters)
    {
        var method = FindMethodInHierarchy(type, name, parameters);
        return method?.ReturnType == typeof(void) || method?.ReturnType == typeof(bool) ? method : null;
    }

    internal static bool CanAdaptText(DevUINode node) =>
        node != null && FindTextCommitMethod(node.GetType()) != null && TryReadTextValue(node, out _);

    internal static bool CanAdaptDirection(DevUINode node) =>
        node != null && TryReadDirection(node, out _) && CanWriteDirection(node);

    internal static bool ClickButton(global::DevInterface.DevUI owner, PlacedObject target, string path) =>
        ClickButton(owner, ResolveLiveRepresentation(owner, target), path);

    internal static bool ClickButton(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path)
    {
        if (TryParseCompositeAction(path, CyclerActionPrefix, out int cyclerIndex, out string cyclerPath))
            return SetCycler(owner, representation, cyclerPath, cyclerIndex);
        if (TryParseCompositeAction(path, ExtEnumActionPrefix, out int extEnumIndex, out string extEnumPath))
            return SetExtEnum(owner, representation, extEnumPath, extEnumIndex);
        if (TryParseCompositeAction(path, IntegerActionPrefix, out int integerChange, out string integerPath))
            return IncrementInteger(owner, representation, integerPath, integerChange);
        if (TryParseCompositeAction(path, SelectActionPrefix, out int selectedIndex, out string selectPath))
            return SetSelect(owner, representation, selectPath, selectedIndex);
        if (TryParseCompositeAction(path, PanelSelectActionPrefix, out int panelIndex, out string panelPath))
            return SetPanelSelect(owner, representation, panelPath, panelIndex);

        if (representation == null) return false;
        DevUINode node = ResolveNode(representation, path);

        try
        {
            // Button and ArrowButton are separate vanilla classes but expose the same semantic
            // Clicked() boundary. Delegate through the original virtual method so custom numeric
            // controllers, pagers and mod-defined arrow actions keep their own parent-signal logic.
            if (node is Button button && !IsInfrastructureButton(button))
                button.Clicked();
            else if (node is ArrowButton arrowButton)
                arrowButton.Clicked();
            else
                return false;

            node.Refresh();

            // Several third-party Panels defer structural work until their next Update after a
            // child signal (pagination, file pickers, searchable lists). The headless host
            // intentionally has no vanilla per-frame page loop, so advance only the direct polling
            // parent once after an explicit semantic action.
            SynchronizePollingParent(owner, node);
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic button invocation failed: " + error);
            return false;
        }
    }

    internal static bool SetSlider(global::DevInterface.DevUI owner, PlacedObject target, string path, float factor) =>
        SetSlider(owner, ResolveLiveRepresentation(owner, target), path, factor);

    internal static bool SetSlider(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        float factor)
    {
        if (!Finite(factor) || representation == null || ResolveNode(representation, path) is not Slider slider) return false;

        try
        {
            factor = Mathf.Clamp01(factor);

            // Some custom sliders expose a second semantic drag method while intentionally making
            // vanilla NubDragged a no-op. Prefer any compatible NubDragged2(float) contract, then
            // fall back to the vanilla virtual method. This is structural, not RegionKit-specific.
            if (!TryInvokeSemanticSliderDrag(slider, factor))
            {
                slider.NubDragged(factor);
                slider.RefreshNubPos(factor);
            }

            slider.Refresh();
            SynchronizePollingParent(owner, slider);
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic slider mutation failed: " + error);
            return false;
        }
    }

    internal static bool ResetLegacyBoolean(global::DevInterface.DevUI owner, PlacedObject target, string path, bool desired) =>
        ResetLegacyBoolean(owner, ResolveLiveRepresentation(owner, target), path, desired);

    internal static bool ResetLegacyBoolean(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        bool desired)
    {
        if (representation == null) return false;
        DevUINode node = ResolveNode(representation, path);
        if (!CanAdaptBoolean(node) || node is not Button button) return false;

        try
        {
            if (TryReadBoolMember(node, "actualValue", out bool current) && current == desired) return true;
            button.Clicked();
            button.Refresh();
            SynchronizePollingParent(owner, button);
            if (TryReadBoolMember(node, "actualValue", out bool after) && after == desired) return true;

            // Opening a helper panel is not a value commit. Its original child controls
            // are captured next; never force author data to make the checkbox appear successful.
            return false;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic boolean mutation failed: " + error);
            return false;
        }
    }

    internal static bool ResetSlider(global::DevInterface.DevUI owner, PlacedObject target, string path) =>
        ResetSlider(owner, ResolveLiveRepresentation(owner, target), path);

    internal static bool ResetSlider(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path)
    {
        if (representation == null) return false;
        if (ResolveNode(representation, path) is not Slider slider || !slider.inheritButton) return false;

        try
        {
            slider.ClickedResetToInherent();
            slider.Refresh();
            SynchronizePollingParent(owner, slider);
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic slider reset failed: " + error);
            return false;
        }
    }

    internal static bool SetCycler(global::DevInterface.DevUI owner, PlacedObject target, string path, int selectedIndex) =>
        SetCycler(owner, ResolveLiveRepresentation(owner, target), path, selectedIndex);

    internal static bool SetCycler(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        int selectedIndex)
    {
        if (representation == null || ResolveNode(representation, path) is not Cycler cycler) return false;
        if (cycler.alternatives == null || selectedIndex < 0 || selectedIndex >= cycler.alternatives.Count) return false;

        try
        {
            cycler.currentAlternative = selectedIndex;
            string selected = cycler.alternatives[selectedIndex] ?? string.Empty;
            cycler.Text = (cycler.baseName ?? string.Empty) + selected;
            PropagateSignal(cycler, DevUISignalType.ButtonClick, selected);
            SynchronizePollingParent(owner, cycler);
            cycler.Refresh();
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic cycler mutation failed: " + error);
            return false;
        }
    }

    internal static bool SetExtEnum(global::DevInterface.DevUI owner, PlacedObject target, string path, int selectedIndex) =>
        SetExtEnum(owner, ResolveLiveRepresentation(owner, target), path, selectedIndex);

    internal static bool SetExtEnum(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        int selectedIndex)
    {
        if (representation == null) return false;
        DevUINode node = ResolveNode(representation, path);
        if (!TryReadExtEnum(node, out Type enumType, out string memberName, out string[] options, out _)) return false;
        if (selectedIndex < 0 || selectedIndex >= options.Length) return false;

        try
        {
            object parsed = ExtEnumBase.Parse(enumType, options[selectedIndex], false);
            if (!TryWriteMember(node, memberName, parsed)) return false;
            node.Refresh();
            if (node is Button button)
                PropagateSignal(button, DevUISignalType.ButtonClick, options[selectedIndex]);
            SynchronizePollingParent(owner, node);
            return TryReadExtEnum(node, out _, out _, out _, out int actualIndex) && actualIndex == selectedIndex;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic ExtEnum mutation failed: " + error);
            return false;
        }
    }

    internal static bool IncrementInteger(global::DevInterface.DevUI owner, PlacedObject target, string path, int change) =>
        IncrementInteger(owner, ResolveLiveRepresentation(owner, target), path, change);

    internal static bool IncrementInteger(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        int change)
    {
        if (change == 0) return false;
        if (representation == null || ResolveNode(representation, path) is not IntegerControl control) return false;

        try
        {
            control.Increment(change);
            control.Refresh();
            SynchronizePollingParent(owner, control);
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic integer mutation failed: " + error);
            return false;
        }
    }

    internal static bool SetSelect(global::DevInterface.DevUI owner, PlacedObject target, string path, int selectedIndex) =>
        SetSelect(owner, ResolveLiveRepresentation(owner, target), path, selectedIndex);

    internal static bool SetSelect(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        int selectedIndex)
    {
        if (representation == null || ResolveNode(representation, path) is not ButtonWithSelectPanel button) return false;

        string[] options = ReadSelectOptions(button);
        if (selectedIndex < 0 || selectedIndex >= options.Length) return false;

        try
        {
            CloseSelectPanel(button);
            button.OnValueChange(options[selectedIndex]);
            button.Refresh();
            SynchronizePollingParent(owner, button);
            return true;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic select mutation failed: " + error);
            return false;
        }
    }

    internal static bool SetPanelSelect(global::DevInterface.DevUI owner, PlacedObject target, string path, int selectedIndex) =>
        SetPanelSelect(owner, ResolveLiveRepresentation(owner, target), path, selectedIndex);

    internal static bool SetPanelSelect(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        int selectedIndex)
    {
        if (representation == null) return false;
        DevUINode node = ResolveNode(representation, path);
        if (!CanAdaptPanelSelect(node) || node is not Button button) return false;
        if (!TryReadStringOptions(node, "values", out string[] options) ||
            selectedIndex < 0 || selectedIndex >= options.Length)
            return false;

        try
        {
            string selected = options[selectedIndex] ?? string.Empty;
            MethodInfo commit = FindValueCommitMethod(node.GetType(), typeof(string));
            if (commit == null || commit.Invoke(node, new object[] { selected }) is false) return false;
            button.Refresh();
            SynchronizePollingParent(owner, button);
            return TryReadStringMember(node, "actualValue", out string actual) &&
                   string.Equals(actual, selected, StringComparison.Ordinal);
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic option mutation failed: " + error);
            return false;
        }
    }

    internal static bool SetText(global::DevInterface.DevUI owner, PlacedObject target, string path, string value) =>
        SetText(owner, ResolveLiveRepresentation(owner, target), path, value);

    internal static bool SetText(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        string value)
    {
        if (representation == null) return false;
        DevUINode node = ResolveNode(representation, path);
        MethodInfo commit = FindTextCommitMethod(node?.GetType());
        if (commit == null || !TryReadTextValue(node, out _)) return false;

        try
        {
            if (commit.Invoke(node, new object[] { value ?? string.Empty, true }) is false) return false;
            node.Refresh();

            // RegionKit FilePicker/SearchableSelectPanel are representative of a broader pattern:
            // the text control commits immediately, while the parent Panel notices the new value in
            // Update() and rebuilds its temporary children. Advance that parent only on mutation.
            SynchronizePollingParent(owner, node);
            return TryReadTextValue(node, out string actual) &&
                   string.Equals(actual, value ?? string.Empty, StringComparison.Ordinal);
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic text mutation failed: " + error);
            return false;
        }
    }

    internal static bool SetDirection(global::DevInterface.DevUI owner, PlacedObject target, string path, float x, float y) =>
        SetDirection(owner, ResolveLiveRepresentation(owner, target), path, x, y);

    internal static bool SetDirection(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        float x,
        float y)
    {
        if (representation == null) return false;
        DevUINode node = ResolveNode(representation, path);
        if (!CanAdaptDirection(node)) return false;

        try
        {
            if (!TryNormalizeDirection(new Vector2(x, y), out Vector2 direction)) return false;
            if (!TryWriteDirection(node, direction)) return false;
            node.Refresh();
            SynchronizePollingParent(owner, node);
            return TryReadDirection(node, out Vector2 actual) && Vector2.Dot(actual, direction) > 0.999f;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic direction mutation failed: " + error);
            return false;
        }
    }

    internal static bool SetColor(
        global::DevInterface.DevUI owner,
        PlacedObject target,
        string path,
        float r,
        float g,
        float b,
        float a) =>
        SetColor(owner, ResolveLiveRepresentation(owner, target), path, r, g, b, a);

    internal static bool SetColor(
        global::DevInterface.DevUI owner,
        PlacedObjectRepresentation representation,
        string path,
        float r,
        float g,
        float b,
        float a)
    {
        if (!Finite(r) || !Finite(g) || !Finite(b) || !Finite(a) || representation == null) return false;
        DevUINode node = ResolveNode(representation, path);
        if (!CanAdaptColorSelect(node) || node is not Button button) return false;

        try
        {
            Color color = new(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
            MethodInfo commit = FindValueCommitMethod(node.GetType(), typeof(Color));
            if (commit == null || commit.Invoke(node, new object[] { color }) is false) return false;
            button.Refresh();
            SynchronizePollingParent(owner, button);
            return TryReadColor(node, out Color actual) && ColorDistanceSquared(actual, color) < 0.000001f;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool generic color mutation failed: " + error);
            return false;
        }
    }

    private static void CaptureControl(DevUINode node, string path, List<LegacyControlSnapshot> output)
    {
        if (!IsValidParentChain(node)) throw new InvalidOperationException("Original control has a cyclic or oversized parent chain.");
        if (node is Slider slider)
        {
            output.Add(Snapshot(path, node, SliderTitle(slider), LegacyControlKind.Slider,
                valueText: SafeNumberText(slider), factor: SliderFactor(slider), canReset: slider.inheritButton));
        }
        else if (node is Cycler cycler)
        {
            output.Add(Snapshot(path, node, CyclerTitle(cycler), LegacyControlKind.Cycler,
                valueText: CyclerValue(cycler), selectedIndex: cycler.currentAlternative, options: CyclerOptions(cycler)));
        }
        else if (TryReadExtEnum(node, out _, out _, out string[] extEnumOptions, out int extEnumIndex))
        {
            output.Add(Snapshot(path, node, ChildLabelOrSemanticTitle(node, "Type"), LegacyControlKind.ExtEnum,
                valueText: extEnumIndex >= 0 && extEnumIndex < extEnumOptions.Length ? extEnumOptions[extEnumIndex] : string.Empty,
                selectedIndex: extEnumIndex, options: extEnumOptions));
        }
        else if (node is IntegerControl integerControl)
        {
            output.Add(Snapshot(path, node, IntegerTitle(integerControl), LegacyControlKind.Integer,
                valueText: SafeIntegerValue(integerControl)));
        }
        else if (node is ButtonWithSelectPanel select && CanAdaptSelect(select))
        {
            string[] options = ReadSelectOptions(select);
            string current = select.Text ?? string.Empty;
            output.Add(Snapshot(path, node, SelectTitle(select), LegacyControlKind.Select,
                valueText: current, selectedIndex: FindOption(options, current), options: options));
        }
        else if (CanAdaptBoolean(node) && TryReadBoolMember(node, "actualValue", out bool booleanValue))
        {
            output.Add(Snapshot(path, node, SemanticTitle(node, "Enabled"), LegacyControlKind.Boolean,
                valueText: node is Button boolButton ? boolButton.Text ?? string.Empty : string.Empty,
                booleanValue: booleanValue));
        }
        else if (CanAdaptPanelSelect(node) && TryReadStringOptions(node, "values", out string[] panelOptions) &&
                 TryReadStringMember(node, "actualValue", out string panelValue))
        {
            output.Add(Snapshot(path, node, SemanticTitle(node, "Select"), LegacyControlKind.PanelSelect,
                valueText: panelValue, selectedIndex: FindOption(panelOptions, panelValue), options: panelOptions));
        }
        else if (CanAdaptColorSelect(node) && TryReadColor(node, out Color color))
        {
            output.Add(Snapshot(path, node, SemanticTitle(node, "Color"), LegacyControlKind.Color,
                x: color.r, y: color.g, z: color.b, w: color.a));
        }
        else if (CanAdaptDirection(node) && TryReadDirection(node, out Vector2 direction))
        {
            output.Add(Snapshot(path, node, SemanticTitle(node, "Direction"), LegacyControlKind.Direction,
                x: direction.x, y: direction.y));
        }
        else if (CanAdaptText(node) && TryReadTextValue(node, out string textValue))
        {
            output.Add(Snapshot(path, node, SemanticTitle(node, "Text"), LegacyControlKind.Text,
                valueText: textValue));
        }
        else if (node is Button button && !IsInfrastructureButton(button))
        {
            output.Add(Snapshot(path, node,
                string.IsNullOrWhiteSpace(button.Text) ? button.IDstring ?? "Button" : button.Text,
                LegacyControlKind.Button));
            // Not atomic: arbitrary Buttons may create a custom child panel. If such a panel
            // is already open, recurse into it and mirror its controls too.
        }
        else if (node is ArrowButton)
        {
            output.Add(Snapshot(
                path,
                node,
                SemanticTitle(node, "Arrow"),
                LegacyControlKind.Button));
        }

    }

    private static LegacyControlSnapshot Snapshot(
        string path,
        DevUINode node,
        string label,
        LegacyControlKind kind,
        string valueText = "",
        float factor = 0f,
        float x = 0f,
        float y = 0f,
        float z = 0f,
        float w = 0f,
        bool booleanValue = false,
        bool canReset = false,
        int selectedIndex = -1,
        string[] options = null)
    {
        return new LegacyControlSnapshot
        {
            Path = NodePath(node, path),
            Id = node?.IDstring ?? string.Empty,
            Label = string.IsNullOrWhiteSpace(label) ? node?.IDstring ?? "Control" : label.Trim(),
            RuntimeType = node?.GetType().FullName ?? string.Empty,
            PanelPath = FindPanelPath(node, path),
            PanelTitle = FindPanelTitle(node),
            Kind = kind,
            ValueText = valueText ?? string.Empty,
            Factor = factor,
            X = x,
            Y = y,
            Z = z,
            W = w,
            BooleanValue = booleanValue,
            CanReset = canReset,
            SelectedIndex = selectedIndex,
            Options = options == null ? Array.Empty<string>() : CloneOptions(options)
        };
    }

    private static string FindPanelPath(DevUINode node, string path)
    {
        string currentPath = path ?? string.Empty;
        HashSet<DevUINode> seen = new();
        for (DevUINode parent = node?.parentNode; parent != null && seen.Add(parent); parent = parent.parentNode)
        {
            int dot = currentPath.LastIndexOf('.');
            currentPath = dot < 0 ? string.Empty : currentPath.Substring(0, dot);
            if (parent is Panel) return currentPath;
        }
        return string.Empty;
    }

    private static string FindPanelTitle(DevUINode node)
    {
        HashSet<DevUINode> seen = new();
        for (DevUINode parent = node?.parentNode; parent != null && seen.Add(parent); parent = parent.parentNode)
            if (parent is Panel panel)
                return panel.fLabels?.Count > 0 ? panel.fLabels[0]?.text ?? panel.IDstring : panel.IDstring ?? string.Empty;
        return string.Empty;
    }

    private static bool IsInfrastructureButton(Button button)
    {
        string id = button?.IDstring ?? string.Empty;
        return id == "Save_Settings" || id == "Save_Specific" || id == "Collapse" ||
               id == "Prev_Button" || id == "Next_Button" || id == "Export_Sandbox";
    }

    private static string SliderTitle(Slider slider)
    {
        try
        {
            if (slider.subNodes.Count > 0 && slider.subNodes[0] is DevUILabel title && !string.IsNullOrWhiteSpace(title.Text))
                return title.Text;
        }
        catch { }
        return slider.IDstring ?? "Slider";
    }

    private static string SafeNumberText(Slider slider)
    {
        try { return slider.NumberText ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static float SliderFactor(Slider slider)
    {
        try
        {
            if (TryReadNumericMember(slider, "actualValue", out float actual) &&
                TryReadNumericMember(slider, "minValue", out float min) &&
                TryReadNumericMember(slider, "maxValue", out float max) &&
                Math.Abs(max - min) > 0.000001f)
                return Mathf.InverseLerp(min, max, actual);

            int nubIndex = slider.inheritButton ? 3 : 2;
            if (nubIndex < 0 || nubIndex >= slider.subNodes.Count || slider.subNodes[nubIndex] is not Slider.SliderNub nub)
                return 0f;
            return Mathf.Clamp01((nub.pos.x - slider.SliderStartCoord) / 92f);
        }
        catch { return 0f; }
    }

    private static bool TryInvokeSemanticSliderDrag(Slider slider, float factor)
    {
        if (slider == null) return false;
        MethodInfo method = FindMethodInHierarchy(slider.GetType(), "NubDragged2", new[] { typeof(float) });
        if (method == null || method.ReturnType != typeof(void)) return false;
        method.Invoke(slider, new object[] { factor });
        return true;
    }

    private static bool TryReadExtEnum(
        DevUINode node,
        out Type enumType,
        out string memberName,
        out string[] options,
        out int selectedIndex)
    {
        enumType = null;
        memberName = string.Empty;
        options = Array.Empty<string>();
        selectedIndex = -1;
        if (node is not Button) return false;

        object current = ReadMember(node, "Type");
        if (current is ExtEnumBase)
            memberName = "Type";
        else
        {
            current = ReadMember(node, "actualValue");
            if (current is ExtEnumBase)
                memberName = "actualValue";
        }

        if (current is not ExtEnumBase extEnum) return false;
        enumType = extEnum.GetType();
        try
        {
            options = ExtEnumBase.GetNames(enumType) ?? Array.Empty<string>();
            selectedIndex = FindOption(options, extEnum.value);
            if (selectedIndex < 0) selectedIndex = extEnum.Index;
            return options.Length > 0;
        }
        catch
        {
            enumType = null;
            memberName = string.Empty;
            options = Array.Empty<string>();
            selectedIndex = -1;
            return false;
        }
    }

    private static bool TryReadNumericMember(object instance, string name, out float value)
    {
        value = 0f;
        object raw = ReadMember(instance, name);
        if (raw == null) return false;
        try
        {
            value = Convert.ToSingle(raw);
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
        catch { return false; }
    }

    private static MethodInfo FindTextCommitMethod(Type type) =>
        FindCommitMethod(type, "TrySetValue", new[] { typeof(string), typeof(bool) });

    private static MethodInfo FindMethodInHierarchy(Type type, string name, Type[] parameters) =>
        LegacyControlReflection.Method(type, name, parameters);

    private static bool TryReadTextValue(object instance, out string value) =>
        TryReadStringMember(instance, "actualValue", out value);

    private static bool TryReadStringMember(object instance, string name, out string value)
    {
        value = string.Empty;
        object raw = ReadMember(instance, name);
        if (raw is not string text) return false;
        value = text;
        return true;
    }

    private static bool TryReadStringOptions(object instance, string name, out string[] value)
    {
        value = Array.Empty<string>();
        object raw = ReadMember(instance, name);
        if (raw is string[] strings)
        {
            value = CloneOptions(strings);
            return true;
        }

        if (raw is not IEnumerable enumerable || raw is string) return false;
        List<string> result = new();
        int read = 0;
        foreach (object item in enumerable)
        {
            if (++read > NodeBudget) throw new InvalidOperationException("Original control options exceed the 4096-item budget.");
            if (item == null) continue;
            if (item is not string text) throw new InvalidOperationException("Original control options must be strings.");
            result.Add(text);
        }
        value = result.ToArray();
        return value.Length > 0;
    }

    private static bool TryReadBoolMember(object instance, string name, out bool value)
    {
        value = false;
        object raw = ReadMember(instance, name);
        if (raw is not bool boolean) return false;
        value = boolean;
        return true;
    }

    private static bool TryReadColor(object instance, out Color color)
    {
        color = Color.white;
        object raw = ReadMember(instance, "actualValue");
        if (raw is not Color value) return false;
        if (!Finite(value.r) || !Finite(value.g) || !Finite(value.b) || !Finite(value.a))
            throw new InvalidOperationException("Original control color contains non-finite components.");
        color = value;
        return true;
    }

    private static object ReadMember(object instance, string name)
    {
        if (instance == null || string.IsNullOrEmpty(name)) return null;
        FieldInfo field = FindFieldInHierarchy(instance.GetType(), name);
        if (field != null) return field.GetValue(instance);
        PropertyInfo property = FindPropertyInHierarchy(instance.GetType(), name);
        if (property != null && property.GetIndexParameters().Length == 0 && property.CanRead)
            return property.GetValue(instance, null);
        return null;
    }

    internal static void ReportReadFailure(DevUINode node, string operation, Exception error)
    {
        if (node == null) return;
        if (ReadFailures.GetValue(node, _ => new HashSet<string>()).Add(operation))
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning(
                "DevTool original control " + node.GetType().FullName + " " + operation + " failed: " + error);
    }

    private static bool TryWriteMember(object instance, string name, object value)
    {
        if (instance == null || string.IsNullOrEmpty(name)) return false;
        FieldInfo field = FindFieldInHierarchy(instance.GetType(), name);
        if (field != null && !field.IsInitOnly && (value == null || field.FieldType.IsInstanceOfType(value)))
        {
            field.SetValue(instance, value);
            return true;
        }

        PropertyInfo property = FindPropertyInHierarchy(instance.GetType(), name);
        if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0 &&
            (value == null || property.PropertyType.IsInstanceOfType(value)))
        {
            property.SetValue(instance, value, null);
            return true;
        }
        return false;
    }

    private static bool TryReadDirection(object instance, out Vector2 direction)
    {
        direction = Vector2.up;
        if (instance == null) return false;

        object direct = ReadMember(instance, "Dir");
        if (direct is Vector2 vector)
        {
            return TryNormalizeDirection(vector, out direction);
        }

        object handle = ReadMember(instance, "handle");
        object nested = ReadMember(handle, "Dir");
        if (nested is not Vector2 handleVector) return false;
        return TryNormalizeDirection(handleVector, out direction);
    }

    private static bool TryNormalizeDirection(Vector2 value, out Vector2 direction)
    {
        direction = Vector2.up;
        if (!Finite(value.x) || !Finite(value.y)) return false;
        double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y);
        if (length > .000001) direction = new Vector2((float)(value.x / length), (float)(value.y / length));
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool CanWriteDirection(DevUINode node)
    {
        if (node == null) return false;
        if (CanWriteVector2Member(node, "Dir")) return true;
        object handle = ReadMember(node, "handle");
        return handle != null && CanWriteVector2Member(handle, "Dir");
    }

    private static bool TryWriteDirection(DevUINode node, Vector2 value)
    {
        if (TryWriteVector2Member(node, "Dir", value)) return true;
        object handle = ReadMember(node, "handle");
        return handle != null && TryWriteVector2Member(handle, "Dir", value);
    }

    private static bool CanWriteVector2Member(object instance, string name)
    {
        if (instance == null) return false;
        FieldInfo field = FindFieldInHierarchy(instance.GetType(), name);
        if (field != null && !field.IsInitOnly && field.FieldType == typeof(Vector2)) return true;
        PropertyInfo property = FindPropertyInHierarchy(instance.GetType(), name);
        return property != null && property.PropertyType == typeof(Vector2) && property.CanWrite &&
               property.GetIndexParameters().Length == 0;
    }

    private static bool TryWriteVector2Member(object instance, string name, Vector2 value)
    {
        if (instance == null) return false;
        FieldInfo field = FindFieldInHierarchy(instance.GetType(), name);
        if (field != null && !field.IsInitOnly && field.FieldType == typeof(Vector2))
        {
            field.SetValue(instance, value);
            return true;
        }
        PropertyInfo property = FindPropertyInHierarchy(instance.GetType(), name);
        if (property != null && property.PropertyType == typeof(Vector2) && property.CanWrite &&
            property.GetIndexParameters().Length == 0)
        {
            property.SetValue(instance, value, null);
            return true;
        }
        return false;
    }

    private static PropertyInfo FindPropertyInHierarchy(Type type, string name) =>
        LegacyControlReflection.Property(type, name);

    private static FieldInfo FindFieldInHierarchy(Type type, string name) =>
        LegacyControlReflection.Field(type, name);

    private static string SemanticTitle(DevUINode node, string fallback)
    {
        string child = ChildLabel(node);
        if (!string.IsNullOrEmpty(child)) return child;
        string id = node?.IDstring ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id)) return fallback;
        return id.Replace('_', ' ').Trim();
    }

    private static string ChildLabelOrSemanticTitle(DevUINode node, string fallback)
    {
        string child = ChildLabel(node);
        return string.IsNullOrEmpty(child) ? SemanticTitle(node, fallback) : child;
    }

    private static string ChildLabel(DevUINode node)
    {
        if (node?.subNodes == null) return string.Empty;
        for (int i = 0; i < node.subNodes.Count; i++)
        {
            if (node.subNodes[i] is DevUILabel label && !string.IsNullOrWhiteSpace(label.Text))
                return label.Text.Trim().TrimEnd(':').Trim();
        }
        return string.Empty;
    }

    private static string CyclerTitle(Cycler cycler)
    {
        if (!string.IsNullOrWhiteSpace(cycler?.baseName))
            return cycler.baseName.Trim().TrimEnd(':').Trim();
        return cycler?.IDstring ?? "Cycler";
    }

    private static string CyclerValue(Cycler cycler)
    {
        if (cycler?.alternatives == null || cycler.currentAlternative < 0 || cycler.currentAlternative >= cycler.alternatives.Count)
            return string.Empty;
        return cycler.alternatives[cycler.currentAlternative] ?? string.Empty;
    }

    private static string[] CyclerOptions(Cycler cycler)
    {
        if (cycler?.alternatives == null || cycler.alternatives.Count == 0) return Array.Empty<string>();
        string[] options = new string[cycler.alternatives.Count];
        for (int i = 0; i < options.Length; i++) options[i] = cycler.alternatives[i] ?? string.Empty;
        return options;
    }

    private static string IntegerTitle(IntegerControl control)
    {
        try
        {
            if (control.subNodes.Count > 0 && control.subNodes[0] is DevUILabel title && !string.IsNullOrWhiteSpace(title.Text))
                return title.Text;
        }
        catch { }
        return control?.IDstring ?? "Integer";
    }

    private static string SafeIntegerValue(IntegerControl control)
    {
        try { return control.NumberLabelText ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SelectTitle(ButtonWithSelectPanel button) => SemanticTitle(button, "Select");

    private static string[] ReadSelectOptions(ButtonWithSelectPanel button)
    {
        return button?.selectPanel?.items == null ? Array.Empty<string>() : CloneOptions(button.selectPanel.items);
    }

    private static void CloseSelectPanel(ButtonWithSelectPanel button)
    {
        SelectPanel panel = button?.selectPanel;
        if (button == null || panel == null) return;
        button.subNodes.Remove(panel);
        panel.ClearSprites();
        button.selectPanel = null;
    }

    private static string[] CloneOptions(string[] source)
    {
        if (source == null || source.Length == 0) return Array.Empty<string>();
        if (source.Length > NodeBudget) throw new InvalidOperationException("Original control options exceed the 4096-item budget.");
        string[] copy = new string[source.Length];
        for (int i = 0; i < copy.Length; i++) copy[i] = source[i] ?? string.Empty;
        return copy;
    }

    private static int FindOption(string[] options, string value)
    {
        if (options == null) return -1;
        for (int i = 0; i < options.Length; i++)
            if (string.Equals(options[i], value, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static bool TryParseCompositeAction(string encoded, string prefix, out int argument, out string path)
    {
        argument = 0;
        path = string.Empty;
        if (string.IsNullOrEmpty(encoded) || string.IsNullOrEmpty(prefix) ||
            !encoded.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        int separator = encoded.IndexOf('|', prefix.Length);
        if (separator < 0 || !int.TryParse(encoded.Substring(prefix.Length, separator - prefix.Length), out argument))
            return false;
        path = separator + 1 < encoded.Length ? encoded.Substring(separator + 1) : string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static void PropagateSignal(DevUINode source, DevUISignalType type, string message)
    {
        DevUINode current = source;
        var visited = new HashSet<DevUINode>();
        while (current != null)
        {
            if (visited.Count >= NodeBudget || !visited.Add(current)) throw new InvalidOperationException("Invalid original control signal parent chain.");
            current = current.parentNode;
            if (current is not IDevUISignals signals) continue;
            signals.Signal(type, source, message ?? string.Empty);
            return;
        }
    }

    private static void SynchronizePollingParent(global::DevInterface.DevUI owner, DevUINode node)
    {
        if (owner == null || node?.parentNode == null) return;
        if (!IsSafeUpdateTree(node.parentNode)) throw new InvalidOperationException("Original polling window has a cyclic or oversized control tree.");

        bool oldMouseClick = owner.mouseClick;
        bool oldMouseDown = owner.mouseDown;
        DevUINode oldDraggedNode = owner.draggedNode;
        try
        {
            // This is a semantic synchronization pass, not a synthetic legacy-input frame. Disable
            // mouse transitions and drag ownership so hidden Panels can process deferred tree/value
            // state without accidentally reacting to the real cursor behind the ImGui frontend.
            owner.mouseClick = false;
            owner.mouseDown = false;
            owner.draggedNode = null;
            node.parentNode.Update();
        }
        finally
        {
            owner.mouseClick = oldMouseClick;
            owner.mouseDown = oldMouseDown;
            owner.draggedNode = oldDraggedNode;
        }
    }

    private static float ColorDistanceSquared(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        float da = a.a - b.a;
        return dr * dr + dg * dg + db * db + da * da;
    }

    private static PlacedObjectRepresentation FindRepresentation(ObjectsPage page, PlacedObject target)
    {
        if (page == null || target == null) return null;
        return FindRepresentationRecursive(page, target);
    }

    private static PlacedObjectRepresentation FindRepresentationRecursive(DevUINode root, PlacedObject target)
    {
        if (root == null) return null;
        if (root is PlacedObjectRepresentation representation && ReferenceEquals(representation.pObj, target)) return representation;
        try
        {
            foreach (var entry in WalkChildren(root))
                if (entry.Node is PlacedObjectRepresentation candidate && ReferenceEquals(candidate.pObj, target)) return candidate;
        }
        catch (Exception error) { ReportReadFailure(root, "representation lookup", error); }
        return null;
    }
    internal static DevUINode ResolveNode(DevUINode root, string path)
    {
        if (root == null || string.IsNullOrWhiteSpace(path)) return null;
        long identity = 0;
        int marker = path.LastIndexOf('~');
        if (marker >= 0)
        {
            if (!long.TryParse(path.Substring(marker + 1), out identity) || identity <= 0) return null;
            path = path.Substring(0, marker);
        }
        string[] parts = path.Split('.');
        if (parts.Length > NodeBudget) return null;
        DevUINode current = root;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int index) || index < 0 || current.subNodes == null || index >= current.subNodes.Count)
                return null;
            current = current.subNodes[index];
            if (current == null) return null;
        }
        return (identity == 0 || NodeIdentities.TryGetValue(current, out var actual) && actual.Value == identity) &&
            IsValidParentChain(current) && IsSafeUpdateTree(current) ? current : null;
    }
}

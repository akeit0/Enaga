using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Okojo.Annotations;
using Okojo.Objects;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;
using Okojo.Runtime;
using Okojo;
namespace Enaga.React.OkojoRuntime;

public sealed partial class OkojoNodeReactHost
{
    private readonly HashSet<JsObject> dirtyHostNodes = [];
    private readonly Dictionary<string, JsObject> hostNodeLookup = new(StringComparer.Ordinal);
    private readonly FlowLayoutScratchArena flowLayoutScratch = new();
    private readonly FlushTraversalScratch flushTraversalScratch = new();
    private HostInstanceShapeCache? hostInstanceShapeCache;
    private bool requiresFullSceneFlush = true;

    private JsObject CreateHostInstanceObject(
        HostNodeKind kind,
        string type,
        string runtimeId,
        string? publicId,
        JsValue propsValue,
        string? text)
    {
        var realm = runtime?.MainRealm ?? throw new InvalidOperationException("Native runtime is not initialized.");
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var children = new JsArray(realm);
        var shapeCache = GetHostInstanceShapeCache(realm, atoms);
        var instance = new JsUserDataObject<HostInstanceState>(text is null ? shapeCache.ElementShape : shapeCache.TextShape)
        {
            UserData = new HostInstanceState(kind, runtimeId, children)
        };
        var childrenValue = JsValue.FromObject(children);

        instance.SetNamedSlotUnchecked(HostInstanceShapeCache.RuntimeIdSlot, JsValue.FromString(runtimeId));
        instance.SetNamedSlotUnchecked(
            HostInstanceShapeCache.PublicIdSlot,
            publicId is null ? JsValue.Undefined : JsValue.FromString(publicId));
        instance.SetNamedSlotUnchecked(HostInstanceShapeCache.TypeSlot, JsValue.FromString(type));
        instance.SetNamedSlotUnchecked(HostInstanceShapeCache.ParentSlot, JsValue.Null);
        instance.SetNamedSlotUnchecked(HostInstanceShapeCache.PropsSlot, propsValue);
        instance.SetNamedSlotUnchecked(HostInstanceShapeCache.ChildrenSlot, childrenValue);
        instance.SetNamedSlotUnchecked(HostInstanceShapeCache.HiddenSlot, JsValue.False);
        if (text is not null)
            instance.SetNamedSlotUnchecked(HostInstanceShapeCache.TextSlot, JsValue.FromString(text));
        UpdateHostResolvedState(instance, propsValue);
        hostNodeLookup[runtimeId] = instance;
        return instance;
    }

    private HostInstanceShapeCache GetHostInstanceShapeCache(JsRealm realm, ReactAppPropertyAtoms atoms)
    {
        if (hostInstanceShapeCache is { } cache && ReferenceEquals(cache.ElementShape.Owner, realm))
            return cache;

        cache = HostInstanceShapeCache.Create(realm, atoms);
        hostInstanceShapeCache = cache;
        return cache;
    }

    private static HostNodeKind ResolveHostNodeKind(string type)
    {
        return type switch
        {
            "Scene" => HostNodeKind.Scene,
            "View" => HostNodeKind.View,
            "ScrollView" => HostNodeKind.ScrollView,
            "Text" => HostNodeKind.Text,
            "TextInput" => HostNodeKind.TextInput,
            "Image" => HostNodeKind.Image,
            "Spacer" => HostNodeKind.Spacer,
            "__text__" => HostNodeKind.RawText,
            _ => HostNodeKind.Unknown
        };
    }

    private HostNodeKind GetHostNodeKind(JsObject node)
    {
        if (GetHostInstanceState(node) is { } state)
            return state.Kind;

        return ResolveHostNodeKind(GetStringProperty(node, propertyAtoms!.Type) ?? string.Empty);
    }

    private static HostInstanceState? GetHostInstanceState(JsObject node)
    {
        return node is JsUserDataObject<HostInstanceState> hostInstance ? hostInstance.UserData : null;
    }

    private JsObject? GetHostParent(JsObject node)
    {
        var state = GetHostInstanceState(node);
        if (state?.Parent is not null)
            return state.Parent;

        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        return TryGetObjectProperty(node, atoms.Parent);
    }

    private string? GetHostRuntimeId(JsObject node)
    {
        var state = GetHostInstanceState(node);
        if (state?.RuntimeId is { Length: > 0 } runtimeId)
            return runtimeId;

        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        return GetStringProperty(node, atoms.RuntimeId);
    }

    private void MarkFullSceneFlush()
    {
        requiresFullSceneFlush = true;
        dirtyHostNodes.Clear();
    }

    private void ResetAfterCommit(JsObject rootChildren, string backgroundColor)
    {
        if (requiresFullSceneFlush)
        {
            ResetScene(backgroundColor);
            FlushChildren(rootChildren, "root", 0, 0, Width, Height);
            requiresFullSceneFlush = false;
            dirtyHostNodes.Clear();
            return;
        }

        if (dirtyHostNodes.Count > 0 && !FlushPendingDirtyRoots())
        {
            MarkFullSceneFlush();
            ResetScene(backgroundColor);
            FlushChildren(rootChildren, "root", 0, 0, Width, Height);
        }

        requiresFullSceneFlush = false;
        dirtyHostNodes.Clear();
    }

    private bool FlushPendingDirtyRoots()
    {
        foreach (var dirtyRoot in dirtyHostNodes)
        {
            if (HasDirtyAncestor(dirtyRoot))
                continue;

            if (!TryResolveDirtyFlushContext(dirtyRoot, out var context))
                return false;

            FlushNode(
                dirtyRoot,
                context.ParentId,
                context.ParentLeft,
                context.ParentTop,
                context.ParentWidth,
                context.ParentHeight,
                context.OverrideFrame);
        }

        return true;
    }

    private bool HasDirtyAncestor(JsObject node)
    {
        var current = GetHostParent(node);
        while (current is not null)
        {
            if (dirtyHostNodes.Contains(current))
                return true;

            current = GetHostParent(current);
        }

        return false;
    }

    private void CommitHostUpdate(JsObject instance, JsValue propsValue, string? publicId, bool layoutAffected)
    {
        var realm = runtime?.MainRealm ?? throw new InvalidOperationException("Native runtime is not initialized.");
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        instance.TrySetPropertyByAtom(realm, atoms.PublicId, publicId is null ? JsValue.Undefined : JsValue.FromString(publicId));
        instance.TrySetPropertyByAtom(realm, atoms.Props, propsValue);
        UpdateHostResolvedState(instance, propsValue);
        MarkHostNodeDirty(instance, layoutAffected);
    }

    private void UpdateHostResolvedState(JsObject node, JsValue propsValue)
    {
        if (GetHostInstanceState(node) is not { } state)
            return;

        var props = propsValue.TryGetObject(out var propsObject) ? propsObject : null;
        var style = TryGetObjectProperty(props, propertyAtoms!.Style);
        state.Props = props;
        state.Style = style;
        state.ResolvedLayout = ResolveHostResolvedLayout(props, style);
        state.HotMeasureState = ResolveHostHotMeasureState(state.Kind, props, style);
        state.ColdState = ResolveHostColdState(state.Kind, props, style);
    }

    private HostResolvedLayout ResolveHostResolvedLayout(JsObject? props, JsObject? style)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var isFlowLayout = IsFlowLayoutStyle(style, atoms);
        var (left, leftUnit) = ResolveFrameScalar(props, style, atoms.Left, LayoutValueUnitFlags.LeftPercent);
        var (top, topUnit) = ResolveFrameScalar(props, style, atoms.Top, LayoutValueUnitFlags.TopPercent);
        var (right, rightUnit) = ResolveFrameScalar(props, style, atoms.Right, LayoutValueUnitFlags.RightPercent);
        var (bottom, bottomUnit) = ResolveFrameScalar(props, style, atoms.Bottom, LayoutValueUnitFlags.BottomPercent);
        var (width, widthUnit) = ResolveFrameScalar(props, style, atoms.Width, LayoutValueUnitFlags.WidthPercent);
        var (height, heightUnit) = ResolveFrameScalar(props, style, atoms.Height, LayoutValueUnitFlags.HeightPercent);
        var (minWidth, minWidthUnit) = ResolveFrameScalar(props, style, atoms.MinWidth, LayoutValueUnitFlags.MinWidthPercent);
        var (maxWidth, maxWidthUnit) = ResolveFrameScalar(props, style, atoms.MaxWidth, LayoutValueUnitFlags.MaxWidthPercent);
        var (minHeight, minHeightUnit) = ResolveFrameScalar(props, style, atoms.MinHeight, LayoutValueUnitFlags.MinHeightPercent);
        var (maxHeight, maxHeightUnit) = ResolveFrameScalar(props, style, atoms.MaxHeight, LayoutValueUnitFlags.MaxHeightPercent);
        return new HostResolvedLayout(
            new HostFrameProps(
                left,
                top,
                right,
                bottom,
                width,
                height,
                minWidth,
                maxWidth,
                minHeight,
                maxHeight,
                ResolvePositionMode(props, style, atoms, defaultPositionMode),
                ParseCrossAlignment(GetStringProperty(props, atoms.AlignSelf) ?? GetStyleStringProperty(style, atoms.AlignSelf), CrossAlignment.Auto),
                leftUnit | topUnit | rightUnit | bottomUnit | widthUnit | heightUnit | minWidthUnit | maxWidthUnit | minHeightUnit | maxHeightUnit),
            ResolveMarginInsets(style, atoms),
            ResolvePaddingInsets(style, atoms),
            Math.Max(0, GetNullableStyleFloatProperty(style, atoms.BorderWidth) ?? 0),
            ResolveBoxSizing(style, atoms),
            isFlowLayout,
            isFlowLayout ? ResolveFlexDirection(style, atoms) : FlexDirection.Column,
            isFlowLayout ? ResolveFlexWrap(style, atoms) : FlexWrap.NoWrap,
            ResolveLayoutDirection(style, atoms),
            isFlowLayout ? ResolveAlignItems(style, atoms) : CrossAlignment.Stretch,
            isFlowLayout ? ResolveJustifyContent(style, atoms) : MainAxisJustification.Start,
            isFlowLayout ? GetStyleFloatProperty(style, atoms.Gap) : 0);
    }

    private HostHotMeasureState ResolveHostHotMeasureState(HostNodeKind kind, JsObject? props, JsObject? style)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var flexGrow = ReadNonNegativeFloat(props, atoms.FlexGrow)
            ?? ReadNonNegativeStyleFloat(style, atoms.FlexGrow);
        var legacyFlex = ReadNonNegativeFloat(props, atoms.Flex)
            ?? ReadNonNegativeStyleFloat(style, atoms.Flex)
            ?? 0;
        if (!flexGrow.HasValue)
            flexGrow = legacyFlex;

        var flexShrink = ReadNonNegativeFloat(props, atoms.FlexShrink)
            ?? ReadNonNegativeStyleFloat(style, atoms.FlexShrink)
            ?? 0;
        var (flexBasis, flexBasisUnit) = ResolveFrameScalar(props, style, atoms.FlexBasis, LayoutValueUnitFlags.FlexBasisPercent);

        var flags = HostMeasureFlags.None;
        if (GetBoolProperty(props, atoms.Wrap) || GetStyleBoolProperty(style, atoms.Wrap))
            flags |= HostMeasureFlags.Wrap;
        if (GetBoolProperty(props, atoms.Multiline) || GetStyleBoolProperty(style, atoms.Multiline))
            flags |= HostMeasureFlags.Multiline;
        if (flexBasisUnit == LayoutValueUnitFlags.FlexBasisPercent)
            flags |= HostMeasureFlags.FlexBasisPercent;

        return new HostHotMeasureState(
            kind,
            flexGrow ?? 0,
            flexShrink,
            flexBasis,
            GetNullableFloatProperty(props, atoms.FontSize)
                ?? GetNullableStyleFloatProperty(style, atoms.FontSize)
                ?? 18,
            GetIntProperty(props, atoms.FontWeight, GetStyleIntProperty(style, atoms.FontWeight, 400)),
            GetNullableFloatProperty(props, atoms.LineHeight)
                ?? GetNullableStyleFloatProperty(style, atoms.LineHeight)
                ?? 22,
            flags);
    }

    private HostColdState ResolveHostColdState(HostNodeKind kind, JsObject? props, JsObject? style)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        return new HostColdState(
            GetStringProperty(props, atoms.FontFamily) ?? GetStyleStringProperty(style, atoms.FontFamily),
            kind == HostNodeKind.Text ? GetStringProperty(props, atoms.Content) : null,
            kind == HostNodeKind.TextInput ? GetStringProperty(props, atoms.Value) ?? string.Empty : null,
            kind == HostNodeKind.TextInput ? GetStringProperty(props, atoms.Placeholder) ?? string.Empty : null,
            kind == HostNodeKind.Image ? GetStringProperty(props, atoms.Source) ?? string.Empty : null,
            kind == HostNodeKind.Image ? GetStringProperty(props, atoms.PlaceholderSource) : null);
    }

    private bool HasLayoutAffectingHostPropChange(JsValue oldPropsValue, JsValue newPropsValue)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var oldProps = oldPropsValue.TryGetObject(out var oldPropsObject) ? oldPropsObject : null;
        var newProps = newPropsValue.TryGetObject(out var newPropsObject) ? newPropsObject : null;
        var oldStyle = TryGetObjectProperty(oldProps, atoms.Style);
        var newStyle = TryGetObjectProperty(newProps, atoms.Style);

        return !(LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Left)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Top)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Right)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Bottom)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Width)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Height)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MinWidth)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MaxWidth)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MinHeight)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MaxHeight)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Position)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Flex)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FlexBasis)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FlexGrow)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FlexShrink)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.AlignSelf)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Margin)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MarginHorizontal)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MarginVertical)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MarginLeft)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MarginTop)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MarginRight)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.MarginBottom)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Padding)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.PaddingHorizontal)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.PaddingVertical)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.PaddingLeft)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.PaddingTop)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.PaddingRight)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.PaddingBottom)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.BorderWidth)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.BoxSizing)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FlexDirection)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FlexWrap)
                  && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Direction)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Gap)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.AlignItems)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.JustifyContent)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.Wrap)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FontSize)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FontFamily)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.FontWeight)
                 && LayoutRelevantValuesEqual(oldProps, newProps, oldStyle, newStyle, atoms.TextAlign));
    }

    private static bool LayoutRelevantValuesEqual(JsObject? oldProps, JsObject? newProps, JsObject? oldStyle, JsObject? newStyle, int atom)
    {
        return JsValue.SameValue(GetPropertyOrUndefined(oldProps, atom), GetPropertyOrUndefined(newProps, atom))
               && JsValue.SameValue(GetPropertyOrUndefined(oldStyle, atom), GetPropertyOrUndefined(newStyle, atom));
    }

    private static JsValue GetPropertyOrUndefined(JsObject? obj, int atom)
    {
        return obj is null ? JsValue.Undefined : GetProperty(obj, atom);
    }

    private void CommitTextUpdate(JsObject textInstance, string oldText, string newText)
    {
        var realm = runtime?.MainRealm ?? throw new InvalidOperationException("Native runtime is not initialized.");
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        textInstance.TrySetPropertyByAtom(realm, atoms.Text, JsValue.FromString(newText));

        var parent = GetHostParent(textInstance);
        while (parent is not null && GetHostNodeKind(parent) != HostNodeKind.Text)
            parent = GetHostParent(parent);

        if (parent is not null)
        {
            MarkHostNodeDirty(parent, HasTextLayoutChange(parent, oldText, newText));
            return;
        }

        MarkFullSceneFlush();
    }

    private void SetNodeHidden(JsObject instance, bool hidden)
    {
        var realm = runtime?.MainRealm ?? throw new InvalidOperationException("Native runtime is not initialized.");
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        instance.TrySetPropertyByAtom(realm, atoms.Hidden, hidden ? JsValue.True : JsValue.False);
        MarkFullSceneFlush();
    }

    private bool AppendChildNode(JsObject parent, JsObject child)
    {
        var children = GetOrCreateChildrenArray(parent);
        if (TryFindChildIndex(children, child, out _))
            return false;

        RemoveNodeFromCurrentParent(child);
        children.SetElement(children.Length, JsValue.FromObject(child));
        SetNodeParent(child, parent);
        RegisterNodeRecursive(child);
        MarkFullSceneFlush();
        return true;
    }

    private bool InsertChildBeforeNode(JsObject parent, JsObject child, JsObject beforeChild)
    {
        var children = GetOrCreateChildrenArray(parent);
        if (!TryFindChildIndex(children, beforeChild, out var beforeIndex))
            return AppendChildNode(parent, child);

        var existingIndex = TryFindChildIndex(children, child, out var foundIndex) ? foundIndex : -1;
        if (existingIndex == beforeIndex - 1)
            return false;

        RemoveNodeFromCurrentParent(child);
        if (!TryGetChildrenArray(parent, out children))
            children = GetOrCreateChildrenArray(parent);

        if (!TryFindChildIndex(children, beforeChild, out beforeIndex))
            return AppendChildNode(parent, child);

        InsertChildAt(children, beforeIndex, child);
        SetNodeParent(child, parent);
        RegisterNodeRecursive(child);
        MarkFullSceneFlush();
        return true;
    }

    private bool RemoveChildNode(JsObject parent, JsObject child)
    {
        if (!TryGetChildrenArray(parent, out var children) || !TryFindChildIndex(children, child, out var index))
            return false;

        RemoveChildAt(children, index);
        SetNodeParent(child, parent: null);
        RemoveNodeRegistryRecursive(child);
        MarkFullSceneFlush();
        return true;
    }

    private void ClearChildNodes(JsObject parent)
    {
        if (!TryGetChildrenArray(parent, out var children))
        {
            MarkFullSceneFlush();
            return;
        }

        var length = GetArrayLength(children);
        for (var index = 0; index < length; index++)
        {
            if (!children.TryGetElement((uint)index, out var childValue) || !childValue.TryGetObject(out var child))
                continue;

            SetNodeParent(child, parent: null);
            RemoveNodeRegistryRecursive(child);
        }

        children.TrySetPropertyByAtom(runtime!.MainRealm, AtomTable.IdLength, JsValue.FromInt32(0));
        MarkFullSceneFlush();
    }

    private string? GetParentRuntimeId(string runtimeId)
    {
        if (string.IsNullOrEmpty(runtimeId) || !hostNodeLookup.TryGetValue(runtimeId, out var node))
            return null;

        var parent = GetHostParent(node);
        return parent is null ? null : GetHostRuntimeId(parent);
    }

    private void RegisterNodeRecursive(JsObject node)
    {
        var runtimeId = GetHostRuntimeId(node);
        if (!string.IsNullOrEmpty(runtimeId))
            hostNodeLookup[runtimeId] = node;

        if (!TryGetChildrenArray(node, out var children))
            return;

        EnumerateChildren(children, RegisterNodeRecursive);
    }

    private void RemoveNodeRegistryRecursive(JsObject node)
    {
        var runtimeId = GetHostRuntimeId(node);
        if (!string.IsNullOrEmpty(runtimeId))
            hostNodeLookup.Remove(runtimeId);

        if (!TryGetChildrenArray(node, out var children))
            return;

        EnumerateChildren(children, RemoveNodeRegistryRecursive);
    }

    private void RemoveNodeFromCurrentParent(JsObject child)
    {
        var currentParent = GetHostParent(child);
        if (currentParent is null || !TryGetChildrenArray(currentParent, out var currentChildren))
            return;

        if (!TryFindChildIndex(currentChildren, child, out var index))
            return;

        RemoveChildAt(currentChildren, index);
        SetNodeParent(child, parent: null);
    }

    private void SetNodeParent(JsObject child, JsObject? parent)
    {
        var realm = runtime?.MainRealm ?? throw new InvalidOperationException("Native runtime is not initialized.");
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        if (GetHostInstanceState(child) is { } state)
            state.Parent = parent;
        child.TrySetPropertyByAtom(realm, atoms.Parent, parent is null ? JsValue.Null : JsValue.FromObject(parent));
    }

    private JsArray GetOrCreateChildrenArray(JsObject owner)
    {
        var realm = runtime?.MainRealm ?? throw new InvalidOperationException("Native runtime is not initialized.");
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        if (TryGetChildrenArray(owner, out var children))
            return children;

        children = new JsArray(realm);
        if (GetHostInstanceState(owner) is { } state)
            state.Children = children;
        owner.TrySetPropertyByAtom(realm, atoms.Children, JsValue.FromObject(children));
        return children;
    }

    private bool TryGetChildrenArray(JsObject owner, out JsArray children)
    {
        if (GetHostInstanceState(owner) is { Children: not null } state)
        {
            children = state.Children;
            return true;
        }

        children = null!;
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        if (TryGetObjectProperty(owner, atoms.Children) is not JsArray childArray)
            return false;

        children = childArray;
        if (GetHostInstanceState(owner) is { } ownerState)
            ownerState.Children = childArray;
        return true;
    }

    private static void InsertChildAt(JsArray children, int index, JsObject child)
    {
        var length = GetArrayLength(children);
        for (var cursor = length; cursor > index; cursor--)
        {
            children.TryGetElement((uint)(cursor - 1), out var previous);
            children.SetElement((uint)cursor, previous);
        }

        children.SetElement((uint)index, JsValue.FromObject(child));
    }

    private static void RemoveChildAt(JsArray children, int index)
    {
        var length = GetArrayLength(children);
        if (index < 0 || index >= length)
            return;

        for (var cursor = index; cursor < length - 1; cursor++)
        {
            children.TryGetElement((uint)(cursor + 1), out var next);
            children.SetElement((uint)cursor, next);
        }

        children.DeleteElement((uint)Math.Max(0, length - 1));
        children.TrySetPropertyByAtom(children.Realm, AtomTable.IdLength, JsValue.FromInt32(Math.Max(0, length - 1)));
    }

    private static bool TryFindChildIndex(JsArray children, JsObject child, out int index)
    {
        var values = children.AsReadOnlySpan();
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].TryGetObject(out var current) && ReferenceEquals(current, child))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static void EnumerateChildren(JsArray children, Action<JsObject> visitor)
    {
        var values = children.AsReadOnlySpan();
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].TryGetObject(out var child))
                visitor(child);
        }
    }

    private void MarkHostNodeDirty(JsObject node, bool layoutAffected)
    {
        var kind = GetHostNodeKind(node);
        if (requiresFullSceneFlush || kind == HostNodeKind.Scene)
        {
            if (kind == HostNodeKind.Scene)
                MarkFullSceneFlush();
            return;
        }

        if (!layoutAffected)
        {
            dirtyHostNodes.Add(node);
            return;
        }

        var dirtyRoot = node;
        for (var current = node; current is not null;)
        {
            if (IsLayoutBoundary(current))
            {
                dirtyRoot = current;
                break;
            }

            current = GetHostParent(current);
        }

        while (ShouldPromoteDirtyRootToParent(dirtyRoot))
        {
            var parent = GetHostParent(dirtyRoot);
            if (parent is null || GetHostNodeKind(parent) == HostNodeKind.Unknown)
                break;

            dirtyRoot = parent;
        }

        dirtyHostNodes.Add(dirtyRoot);
    }

    private bool IsLayoutBoundary(JsObject node)
    {
        if (GetHostNodeKind(node) == HostNodeKind.ScrollView)
            return true;

        return UsesFlowLayout(CreateHostNodeSnapshot(node));
    }

    private bool ShouldPromoteDirtyRootToParent(JsObject node)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var parent = GetHostParent(node);
        if (parent is null || GetHostNodeKind(parent) == HostNodeKind.Unknown)
            return false;

        var parentProps = TryGetObjectProperty(parent, atoms.Props);
        var parentStyle = TryGetObjectProperty(parentProps, atoms.Style);
        if (!IsLayoutBoundary(parent))
            return false;

        return !IsResolvedAxisSize(
            CreateHostNodeSnapshot(node).ResolvedLayout.Frame,
            ResolveParentLayoutAxis(parentStyle, atoms));
    }

    private static bool IsResolvedAxisSize(in HostFrameProps frame, LayoutAxis axis)
    {
        return axis == LayoutAxis.Column
            ? frame.HasHeight || (frame.HasTop && frame.HasBottom)
            : frame.HasWidth || (frame.HasLeft && frame.HasRight);
    }

    private static LayoutAxis ResolveParentLayoutAxis(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return FlexLayout.ResolveAxis(ResolveFlexDirection(style, atoms));
    }

    private void FlushChildren(JsObject children, string parentId, float parentLeft, float parentTop, float parentWidth, float parentHeight)
    {
        if (children is JsArray denseChildren)
        {
            var values = denseChildren.AsReadOnlySpan();
            for (var index = 0; index < values.Length; index++)
                FlushNodeValue(values[index], parentId, parentLeft, parentTop, parentWidth, parentHeight, null);
            return;
        }

        if (!TryGetArrayLength(children, out var length) || length <= 0)
            return;

        for (var index = 0; index < length; index++)
        {
            if (!children.TryGetElement((uint)index, out var value))
                continue;

            FlushNodeValue(value, parentId, parentLeft, parentTop, parentWidth, parentHeight, null);
        }
    }

    private bool FlushDirtyRoots(JsObject dirtyRoots)
    {
        if (dirtyRoots is JsArray denseDirtyRoots)
        {
            var values = denseDirtyRoots.AsReadOnlySpan();
            for (var index = 0; index < values.Length; index++)
            {
                if (!values[index].TryGetObject(out var dirtyRoot) || !TryResolveDirtyFlushContext(dirtyRoot, out var context))
                    return false;

                FlushNode(
                    dirtyRoot,
                    context.ParentId,
                    context.ParentLeft,
                    context.ParentTop,
                    context.ParentWidth,
                    context.ParentHeight,
                    context.OverrideFrame);
            }

            return true;
        }

        if (!TryGetArrayLength(dirtyRoots, out var length))
            return false;

        for (var index = 0; index < length; index++)
        {
            if (!dirtyRoots.TryGetElement((uint)index, out var value) ||
                !value.TryGetObject(out var dirtyRoot) ||
                !TryResolveDirtyFlushContext(dirtyRoot, out var context))
            {
                return false;
            }

            FlushNode(
                dirtyRoot,
                context.ParentId,
                context.ParentLeft,
                context.ParentTop,
                context.ParentWidth,
                context.ParentHeight,
                context.OverrideFrame);
        }

        return true;
    }

    private bool TryResolveDirtyFlushContext(JsObject node, out DirtyFlushContext context)
    {
        context = default;
        var parent = TryGetObjectProperty(node, propertyAtoms!.Parent);
        if (parent is null)
            return false;

        var parentKind = GetHostNodeKind(parent);
        if (parentKind == HostNodeKind.Unknown)
        {
            context = new DirtyFlushContext("root", 0, 0, Width, Height, null);
            return true;
        }

        if (!TryReadLastLayout(parent, out var parentLayout))
            return false;

        var overrideFrame = ResolveDirtyChildOverrideFrame(parent, node, parentLayout);
        context = new DirtyFlushContext(
            parentKind == HostNodeKind.Scene
                ? "root"
                : GetHostRuntimeId(parent) ?? string.Empty,
            overrideFrame.HasValue ? parentLayout.Left : parentLayout.ContentLeft,
            overrideFrame.HasValue ? parentLayout.Top : parentLayout.ContentTop,
            overrideFrame.HasValue ? parentLayout.Width : parentLayout.ContentWidth,
            overrideFrame.HasValue ? parentLayout.Height : parentLayout.ContentHeight,
            overrideFrame);
        return context.ParentId.Length > 0;
    }

    private LayoutFrameData? ResolveDirtyChildOverrideFrame(JsObject parent, JsObject child, HostLayoutCacheData parentLayout)
    {
        if (!TryReadLastLayout(child, out var childLayout))
            return null;

        if (CreateHostNodeSnapshot(child).ResolvedLayout.Frame.Position == PositionMode.Absolute)
            return null;

        if (!UsesFlowLayout(CreateHostNodeSnapshot(parent)))
            return null;

        return new LayoutFrameData(
            childLayout.Left - parentLayout.Left,
            childLayout.Top - parentLayout.Top,
            childLayout.Width,
            childLayout.Height);
    }

    private bool HasTextLayoutChange(JsObject parent, string oldText, string newText)
    {
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return false;

        if (!TryReadLastLayout(parent, out _))
            return true;

        var previousSize = ResolveTextIntrinsicSize(oldText, parent);
        var nextSize = ResolveTextIntrinsicSize(newText, parent);
        return previousSize.Width != nextSize.Width || previousSize.Height != nextSize.Height;
    }

    private TextIntrinsicSize ResolveTextIntrinsicSize(string text, JsObject host)
    {
        var snapshot = CreateHostNodeSnapshot(host);
        var frame = snapshot.ResolvedLayout.Frame;
        var hot = snapshot.HotMeasureState;
        var cold = snapshot.ColdState;
        var fontSize = hot.FontSize;
        var fontFamily = cold.FontFamily;
        var fontWeight = hot.FontWeight;
        var wrap = hot.Wrap;
        var hasLastLayout = TryReadLastLayout(host, out var lastLayout);
        var width = frame.HasWidth
            ? (!frame.IsWidthPercent
                ? frame.Width
                : hasLastLayout
                    ? lastLayout.Width
                    : 0)
            : MeasureTextWidth(text, fontSize, fontFamily, fontWeight);
        var height = frame.HasHeight
            ? (!frame.IsHeightPercent
                ? frame.Height
                : hasLastLayout
                    ? lastLayout.Height
                    : 0)
            : (
                wrap
                    ? MeasureTextHeight(text, width, fontSize, fontFamily, fontWeight)
                    : (float)Math.Ceiling(fontSize * 1.35f));
        return new TextIntrinsicSize(
            Math.Max(0, (int)Math.Ceiling(width)),
            Math.Max(0, (int)Math.Ceiling(height)));
    }

    private bool TryReadLastLayout(JsObject node, out HostLayoutCacheData layout)
    {
        if (GetHostInstanceState(node) is { HasLayoutCache: true } state)
        {
            layout = state.LayoutCache;
            return true;
        }

        layout = default;
        return false;
    }

    private HostLayoutCacheData? FlushNodeValue(
        in JsValue nodeValue,
        string parentId,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        LayoutFrameData? overrideFrame)
    {
        return !nodeValue.TryGetObject(out var node)
            ? null
            : FlushNode(node, parentId, parentLeft, parentTop, parentWidth, parentHeight, overrideFrame);
    }

    private HostLayoutCacheData? FlushNode(
        JsObject node,
        string parentId,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        LayoutFrameData? overrideFrame)
    {
        flushTraversalScratch.Clear();
        flushTraversalScratch.Push(new FlushTraversalWorkItem
        {
            Stage = FlushTraversalStage.Enter,
            Node = node,
            ParentId = parentId,
            ParentLeft = parentLeft,
            ParentTop = parentTop,
            ParentWidth = parentWidth,
            ParentHeight = parentHeight,
            OverrideFrame = overrideFrame,
            ParentFinalizeIndex = -1,
            IsRoot = true
        });

        HostLayoutCacheData? rootLayout = null;
        while (flushTraversalScratch.TryPop(out var workItem))
        {
            if (workItem.Stage == FlushTraversalStage.Finalize)
            {
                var finalizedLayout = workItem.Layout with
                {
                    ContentWidth = Math.Max(
                        workItem.Layout.ContentWidth,
                        ResolveMeasuredContentWidth(workItem.MaxRight, workItem.Layout.ContentLeft, workItem.PaddingRight)),
                    ContentHeight = Math.Max(
                        workItem.Layout.ContentHeight,
                        ResolveMeasuredContentHeight(workItem.MaxBottom, workItem.Layout.ContentTop, workItem.PaddingBottom))
                };
                WriteLastLayout(workItem.Node, finalizedLayout);
                if (workItem.Kind == HostNodeKind.ScrollView)
                {
                    ScrollView(
                        workItem.ParentId,
                        workItem.RuntimeId,
                        finalizedLayout.Left,
                        finalizedLayout.Top,
                        finalizedLayout.Width,
                        finalizedLayout.Height,
                        workItem.Style,
                        finalizedLayout.ContentWidth,
                        finalizedLayout.ContentHeight);
                }

                if (workItem.IsRoot)
                    rootLayout = finalizedLayout;

                continue;
            }

            if (GetBoolProperty(workItem.Node, propertyAtoms!.Hidden))
                continue;

            var kind = GetHostNodeKind(workItem.Node);
            if (kind == HostNodeKind.Unknown || kind == HostNodeKind.RawText)
                continue;

            if (kind == HostNodeKind.Scene)
            {
                var sceneLayout = new HostLayoutCacheData(0, 0, workItem.ParentWidth, workItem.ParentHeight, 0, 0, workItem.ParentWidth, workItem.ParentHeight);
                WriteLastLayout(workItem.Node, sceneLayout);

                var props = TryGetObjectProperty(workItem.Node, propertyAtoms.Props);
                ResetScene(GetStringProperty(props, propertyAtoms.BackgroundColor) ?? "#08111f");
                if (workItem.IsRoot)
                    rootLayout = sceneLayout;

                PushChildTraversalItems(
                    TryGetObjectProperty(workItem.Node, propertyAtoms.Children),
                    GetArrayLength(TryGetObjectProperty(workItem.Node, propertyAtoms.Children)),
                    "root",
                    0,
                    0,
                    workItem.ParentWidth,
                    workItem.ParentHeight,
                    sceneLayout,
                    childFrames: default,
                    parentFinalizeIndex: -1);
                continue;
            }

            if (kind == HostNodeKind.Spacer)
            {
                var spacerLayout = new HostLayoutCacheData(
                    workItem.ParentLeft + (workItem.OverrideFrame?.Left ?? 0),
                    workItem.ParentTop + (workItem.OverrideFrame?.Top ?? 0),
                    workItem.OverrideFrame?.Width ?? 0,
                    workItem.OverrideFrame?.Height ?? 0,
                    workItem.ParentLeft + (workItem.OverrideFrame?.Left ?? 0),
                    workItem.ParentTop + (workItem.OverrideFrame?.Top ?? 0),
                    workItem.OverrideFrame?.Width ?? 0,
                    workItem.OverrideFrame?.Height ?? 0);
                WriteLastLayout(workItem.Node, spacerLayout);
                AccumulateParentMaxBottom(workItem.ParentFinalizeIndex, spacerLayout);
                if (workItem.IsRoot)
                    rootLayout = spacerLayout;
                continue;
            }

            var snapshot = CreateHostNodeSnapshot(workItem.Node);
            var resolvedLayout = snapshot.ResolvedLayout;
            LayoutFrameData frame;
            if (workItem.OverrideFrame.HasValue)
            {
                frame = workItem.OverrideFrame.Value;
            }
            else
            {
                var measurement = MeasureHostNodeSize(snapshot, workItem.ParentWidth, workItem.ParentHeight, 0, 0);
                frame = ResolveFrameMetrics(
                    measurement.Frame,
                    workItem.ParentWidth,
                    workItem.ParentHeight,
                    measurement.Width,
                    measurement.Height,
                    resolvedLayout.Margin);
            }

            var left = workItem.ParentLeft + frame.Left;
            var top = workItem.ParentTop + frame.Top;
            var width = frame.Width;
            var height = frame.Height;

            switch (kind)
            {
                case HostNodeKind.View:
                    PushViewTraversal(workItem, snapshot, left, top, width, height, ref rootLayout);
                    break;
                case HostNodeKind.ScrollView:
                    PushScrollViewTraversal(workItem, snapshot, left, top, width, height, ref rootLayout);
                    break;
                case HostNodeKind.Text:
                    {
                        var layout = FlushTextNode(workItem.ParentId, workItem.Node, snapshot, left, top, width, height);
                        AccumulateParentMaxBottom(workItem.ParentFinalizeIndex, layout);
                        if (workItem.IsRoot)
                            rootLayout = layout;
                        break;
                    }
                case HostNodeKind.TextInput:
                    {
                        var layout = FlushTextInputNode(workItem.ParentId, workItem.Node, snapshot, left, top, width, height);
                        AccumulateParentMaxBottom(workItem.ParentFinalizeIndex, layout);
                        if (workItem.IsRoot)
                            rootLayout = layout;
                        break;
                    }
                case HostNodeKind.Image:
                    {
                        var layout = FlushImageNode(workItem.ParentId, workItem.Node, snapshot, left, top, width, height);
                        AccumulateParentMaxBottom(workItem.ParentFinalizeIndex, layout);
                        if (workItem.IsRoot)
                            rootLayout = layout;
                        break;
                    }
            }
        }

        return rootLayout;
    }

    private void PushViewTraversal(
        in FlushTraversalWorkItem workItem,
        HostNodeSnapshot snapshot,
        float left,
        float top,
        float width,
        float height,
        ref HostLayoutCacheData? rootLayout)
    {
        View(workItem.ParentId, snapshot.RuntimeId, left, top, width, height, snapshot.Style);
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var resolvedLayout = snapshot.ResolvedLayout;
        var padding = resolvedLayout.Padding;
        var layout = new HostLayoutCacheData(
            left,
            top,
            width,
            height,
            left + padding.Left,
            top + padding.Top,
            Math.Max(0, width - padding.Left - padding.Right),
            Math.Max(0, height - padding.Top - padding.Bottom));
        WriteLastLayout(workItem.Node, layout);
        AccumulateParentMaxBottom(workItem.ParentFinalizeIndex, layout);

        var childCount = GetArrayLength(snapshot.Children);
        if (childCount <= 0)
        {
            if (workItem.IsRoot)
                rootLayout = layout;
            return;
        }

        if (!UsesFlowLayout(snapshot))
        {
            if (workItem.IsRoot)
                rootLayout = layout;

            PushChildTraversalItems(
                snapshot.Children,
                childCount,
                snapshot.RuntimeId,
                left,
                top,
                width,
                height,
                layout,
                childFrames: default,
                parentFinalizeIndex: -1);
            return;
        }

        var finalizeIndex = flushTraversalScratch.Push(new FlushTraversalWorkItem
        {
            Stage = FlushTraversalStage.Finalize,
            Node = workItem.Node,
            ParentId = workItem.ParentId,
            RuntimeId = snapshot.RuntimeId,
            Style = snapshot.Style,
            Layout = layout,
            PaddingRight = padding.Right,
            PaddingBottom = padding.Bottom,
            MaxRight = layout.ContentLeft,
            MaxBottom = layout.ContentTop,
            ParentFinalizeIndex = workItem.ParentFinalizeIndex,
            IsRoot = workItem.IsRoot,
            Kind = HostNodeKind.View
        });

        var axis = resolvedLayout.Axis;
        var alignItems = resolvedLayout.AlignItems;
        var justifyContent = resolvedLayout.JustifyContent;
        var gap = resolvedLayout.Gap;
        var scratchMark = flowLayoutScratch.Mark();
        try
        {
            var requestBuffer = flowLayoutScratch.AllocateRequests(childCount);
            var childFrames = flowLayoutScratch.AllocateFrames(childCount);
            var preparedCount = PrepareHostFlowLayoutChildren(
                snapshot.Children,
                axis,
                layout.ContentWidth,
                layout.ContentHeight,
                alignItems,
                requestBuffer);
            LayoutHostFlowChildren(
                resolvedLayout.FlexDirection,
                resolvedLayout.Direction,
                resolvedLayout.FlexWrap,
                width,
                height,
                gap,
                alignItems,
                justifyContent,
                padding.Left,
                padding.Top,
                padding.Right,
                padding.Bottom,
                requestBuffer[..preparedCount],
                childFrames[..preparedCount]);
            PushChildTraversalItems(
                snapshot.Children,
                preparedCount,
                snapshot.RuntimeId,
                left,
                top,
                width,
                height,
                layout,
                childFrames[..preparedCount],
                finalizeIndex);
        }
        finally
        {
            flowLayoutScratch.Rewind(scratchMark);
        }
    }

    private void PushScrollViewTraversal(
        in FlushTraversalWorkItem workItem,
        HostNodeSnapshot snapshot,
        float left,
        float top,
        float width,
        float height,
        ref HostLayoutCacheData? rootLayout)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var resolvedLayout = snapshot.ResolvedLayout;
        var padding = resolvedLayout.Padding;
        var layout = new HostLayoutCacheData(
            left,
            top,
            width,
            height,
            left + padding.Left,
            top + padding.Top,
            Math.Max(0, width - padding.Left - padding.Right),
            Math.Max(0, height - padding.Top - padding.Bottom));
        WriteLastLayout(workItem.Node, layout);
        ScrollView(workItem.ParentId, snapshot.RuntimeId, left, top, width, height, snapshot.Style, layout.ContentWidth, layout.ContentHeight);
        AccumulateParentMaxBottom(workItem.ParentFinalizeIndex, layout);

        var childCount = GetArrayLength(snapshot.Children);
        if (childCount <= 0)
        {
            ScrollView(workItem.ParentId, snapshot.RuntimeId, left, top, width, height, snapshot.Style, layout.ContentWidth, layout.ContentHeight);
            if (workItem.IsRoot)
                rootLayout = layout;
            return;
        }

        var finalizeIndex = flushTraversalScratch.Push(new FlushTraversalWorkItem
        {
            Stage = FlushTraversalStage.Finalize,
            Node = workItem.Node,
            ParentId = workItem.ParentId,
            RuntimeId = snapshot.RuntimeId,
            Style = snapshot.Style,
            Layout = layout,
            PaddingRight = padding.Right,
            PaddingBottom = padding.Bottom,
            MaxRight = layout.ContentLeft,
            MaxBottom = layout.ContentTop,
            ParentFinalizeIndex = workItem.ParentFinalizeIndex,
            IsRoot = workItem.IsRoot,
            Kind = HostNodeKind.ScrollView
        });

        if (UsesFlowLayout(snapshot))
        {
            var axis = resolvedLayout.Axis;
            var alignItems = resolvedLayout.AlignItems;
            var justifyContent = resolvedLayout.JustifyContent;
            var gap = resolvedLayout.Gap;
            var scratchMark = flowLayoutScratch.Mark();
            try
            {
                var requestBuffer = flowLayoutScratch.AllocateRequests(childCount);
                var childFrames = flowLayoutScratch.AllocateFrames(childCount);
                var preparedCount = PrepareHostFlowLayoutChildren(
                    snapshot.Children,
                    axis,
                    layout.ContentWidth,
                    layout.ContentHeight,
                    alignItems,
                    requestBuffer);
                LayoutHostFlowChildren(
                    resolvedLayout.FlexDirection,
                    resolvedLayout.Direction,
                    resolvedLayout.FlexWrap,
                    width,
                    height,
                    gap,
                    alignItems,
                    justifyContent,
                    padding.Left,
                    padding.Top,
                    padding.Right,
                    padding.Bottom,
                    requestBuffer[..preparedCount],
                    childFrames[..preparedCount]);
                PushChildTraversalItems(
                    snapshot.Children,
                    preparedCount,
                    snapshot.RuntimeId,
                    left,
                    top,
                    width,
                    height,
                    layout,
                    childFrames[..preparedCount],
                    finalizeIndex);
            }
            finally
            {
                flowLayoutScratch.Rewind(scratchMark);
            }
        }
        else
        {
            PushChildTraversalItems(
                snapshot.Children,
                childCount,
                snapshot.RuntimeId,
                left,
                top,
                width,
                height,
                layout,
                childFrames: default,
                finalizeIndex);
        }
    }

    private void PushChildTraversalItems(
        JsObject? children,
        int childCount,
        string parentId,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        HostLayoutCacheData parentLayout,
        ReadOnlySpan<LayoutFrameData?> childFrames,
        int parentFinalizeIndex)
    {
        if (children is null || childCount <= 0)
            return;

        if (children is JsArray denseChildren)
        {
            var values = denseChildren.AsReadOnlySpan();
            var length = Math.Min(childCount, values.Length);
            for (var index = length - 1; index >= 0; index--)
            {
                if (!values[index].TryGetObject(out var child))
                    continue;

                PushChildTraversalItem(
                    child,
                    parentId,
                    parentLeft,
                    parentTop,
                    parentWidth,
                    parentHeight,
                    parentLayout,
                    childFrames.IsEmpty ? null : childFrames[index],
                    parentFinalizeIndex);
            }

            return;
        }

        for (var index = childCount - 1; index >= 0; index--)
        {
            if (!children.TryGetElement((uint)index, out var childValue) || !childValue.TryGetObject(out var child))
                continue;

            PushChildTraversalItem(
                child,
                parentId,
                parentLeft,
                parentTop,
                parentWidth,
                parentHeight,
                parentLayout,
                childFrames.IsEmpty ? null : childFrames[index],
                parentFinalizeIndex);
        }
    }

    private void PushChildTraversalItem(
        JsObject child,
        string parentId,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        HostLayoutCacheData parentLayout,
        LayoutFrameData? childFrame,
        int parentFinalizeIndex)
    {
        var effectiveParentFinalizeIndex = ChildAffectsParentExtent(child) ? parentFinalizeIndex : -1;
        flushTraversalScratch.Push(new FlushTraversalWorkItem
        {
            Stage = FlushTraversalStage.Enter,
            Node = child,
            ParentId = parentId,
            ParentLeft = childFrame is null ? parentLayout.ContentLeft : parentLeft,
            ParentTop = childFrame is null ? parentLayout.ContentTop : parentTop,
            ParentWidth = childFrame is null ? parentLayout.ContentWidth : parentWidth,
            ParentHeight = childFrame is null ? parentLayout.ContentHeight : parentHeight,
            OverrideFrame = childFrame,
            ParentFinalizeIndex = effectiveParentFinalizeIndex,
            IsRoot = false
        });
    }

    private bool ChildAffectsParentExtent(JsObject child)
    {
        var kind = GetHostNodeKind(child);
        if (kind is HostNodeKind.Unknown or HostNodeKind.RawText)
            return false;

        return CreateHostNodeSnapshot(child).ResolvedLayout.Frame.Position != PositionMode.Absolute;
    }

    private void AccumulateParentMaxBottom(int parentFinalizeIndex, HostLayoutCacheData layout)
    {
        if (parentFinalizeIndex < 0)
            return;

        ref var parentItem = ref flushTraversalScratch.GetReference(parentFinalizeIndex);
        parentItem.MaxRight = Math.Max(parentItem.MaxRight, layout.Left + layout.Width);
        parentItem.MaxBottom = Math.Max(parentItem.MaxBottom, layout.Top + layout.Height);
    }

    private HostLayoutCacheData FlushTextNode(
        string parentId,
        JsObject node,
        HostNodeSnapshot snapshot,
        float left,
        float top,
        float width,
        float height)
    {
        var textLayout = new HostLayoutCacheData(left, top, width, height, left, top, width, height);
        WriteLastLayout(node, textLayout);
        Text(parentId, snapshot.RuntimeId, left, top, width, height, ResolveTextContent(snapshot), snapshot.Style);
        return textLayout;
    }

    private HostLayoutCacheData FlushTextInputNode(
        string parentId,
        JsObject node,
        HostNodeSnapshot snapshot,
        float left,
        float top,
        float width,
        float height)
    {
        var textInputLayout = new HostLayoutCacheData(left, top, width, height, left, top, width, height);
        WriteLastLayout(node, textInputLayout);
        TextInputNode(
            parentId,
            snapshot.RuntimeId,
            left,
            top,
            width,
            height,
            snapshot.ColdState.TextInputValue ?? string.Empty,
            snapshot.ColdState.TextInputPlaceholder ?? string.Empty,
            snapshot.Style);
        return textInputLayout;
    }

    private HostLayoutCacheData FlushImageNode(
        string parentId,
        JsObject node,
        HostNodeSnapshot snapshot,
        float left,
        float top,
        float width,
        float height)
    {
        var imageLayout = new HostLayoutCacheData(left, top, width, height, left, top, width, height);
        WriteLastLayout(node, imageLayout);
        Image(
            parentId,
            snapshot.RuntimeId,
            left,
            top,
            width,
            height,
            snapshot.ColdState.ImageSource ?? string.Empty,
            snapshot.ColdState.ImagePlaceholderSource,
            snapshot.Style);
        return imageLayout;
    }

    private HostNodeSnapshot CreateHostNodeSnapshot(JsObject node)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var state = GetHostInstanceState(node);
        var props = state?.Props ?? TryGetObjectProperty(node, atoms.Props) ?? new JsPlainObject(runtime!.MainRealm);
        var style = state?.Style ?? TryGetObjectProperty(props, atoms.Style);
        var kind = GetHostNodeKind(node);
        var runtimeId = GetHostRuntimeId(node) ?? string.Empty;
        return new HostNodeSnapshot(
            node,
            props,
            style,
            TryGetChildrenArray(node, out var children) ? children : null,
            kind,
            runtimeId,
            state?.ResolvedLayout ?? ResolveHostResolvedLayout(props, style),
            state?.HotMeasureState ?? ResolveHostHotMeasureState(kind, props, style),
            state?.ColdState ?? ResolveHostColdState(kind, props, style));
    }

    private bool UsesFlowLayout(in HostNodeSnapshot snapshot)
    {
        return snapshot.ResolvedLayout.IsFlowLayout
               || snapshot.Kind is HostNodeKind.View or HostNodeKind.ScrollView
               && HasImplicitFlowLayoutChildren(snapshot.Children);
    }

    private bool HasImplicitFlowLayoutChildren(JsObject? children)
    {
        if (children is null)
            return false;

        if (children is JsArray denseChildren)
        {
            var values = denseChildren.AsReadOnlySpan();
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index].TryGetObject(out var child) && ChildParticipatesInImplicitFlow(child))
                    return true;
            }

            return false;
        }

        if (!TryGetArrayLength(children, out var length) || length <= 0)
            return false;

        for (var index = 0; index < length; index++)
        {
            if (children.TryGetElement((uint)index, out var childValue)
                && childValue.TryGetObject(out var child)
                && ChildParticipatesInImplicitFlow(child))
            {
                return true;
            }
        }

        return false;
    }

    private bool ChildParticipatesInImplicitFlow(JsObject child)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        if (GetBoolProperty(child, atoms.Hidden))
            return false;

        var kind = GetHostNodeKind(child);
        if (kind is HostNodeKind.Unknown or HostNodeKind.RawText)
            return false;

        return true;
    }

    private HostNodeMeasurement MeasureHostNodeSize(
        HostNodeSnapshot node,
        float availableWidth,
        float availableHeight,
        float stretchWidth,
        float stretchHeight)
    {
        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var resolvedLayout = node.ResolvedLayout;
        var margin = resolvedLayout.Margin;
        var resolvedAvailableWidth = Math.Max(0, availableWidth - margin.Left - margin.Right);
        var resolvedAvailableHeight = Math.Max(0, availableHeight - margin.Top - margin.Bottom);
        var resolvedStretchWidth = Math.Max(0, stretchWidth - margin.Left - margin.Right);
        var resolvedStretchHeight = Math.Max(0, stretchHeight - margin.Top - margin.Bottom);
        var widthBasis = resolvedStretchWidth > 0 ? resolvedStretchWidth : resolvedAvailableWidth;
        var heightBasis = resolvedStretchHeight > 0 ? resolvedStretchHeight : resolvedAvailableHeight;
        var frame = ResolveBoxSizingFrame(resolvedLayout.Frame, resolvedLayout.Padding, resolvedLayout.BorderWidth, resolvedLayout.BoxSizing, widthBasis, heightBasis);
        var resolvedLeft = LayoutValue.Resolve(frame.Left, frame.IsLeftPercent, widthBasis);
        var resolvedTop = LayoutValue.Resolve(frame.Top, frame.IsTopPercent, heightBasis);
        var resolvedRight = LayoutValue.Resolve(frame.Right, frame.IsRightPercent, widthBasis);
        var resolvedBottom = LayoutValue.Resolve(frame.Bottom, frame.IsBottomPercent, heightBasis);
        var resolvedWidth = LayoutValue.Resolve(frame.Width, frame.IsWidthPercent, widthBasis);
        var resolvedHeight = LayoutValue.Resolve(frame.Height, frame.IsHeightPercent, heightBasis);

        var width = frame.HasWidth
            ? resolvedWidth
            : frame.HasRight
                ? Math.Max(0, widthBasis - (frame.HasLeft ? resolvedLeft : 0) - resolvedRight)
                : resolvedStretchWidth;
        var height = frame.HasHeight
            ? resolvedHeight
            : frame.HasBottom
                ? Math.Max(0, heightBasis - (frame.HasTop ? resolvedTop : 0) - resolvedBottom)
                : resolvedStretchHeight;

        if (node.Kind == HostNodeKind.Text && height <= 0)
        {
            var text = ResolveTextContent(node);
            var hot = node.HotMeasureState;
            var cold = node.ColdState;
            var fontSize = hot.FontSize;
            var fontWeight = hot.FontWeight;
            var fontFamily = cold.FontFamily;
            var wrap = hot.Wrap;
            if (width <= 0 && !wrap)
                width = MeasureTextWidth(text, fontSize, fontFamily, fontWeight);

            height = wrap
                ? MeasureTextHeight(text, width, fontSize, fontFamily, fontWeight)
                : (float)Math.Ceiling(fontSize * 1.35f);
        }

        if (node.Kind == HostNodeKind.TextInput)
        {
            if (height <= 0)
            {
                var hot = node.HotMeasureState;
                var lineHeight = hot.LineHeight;
                var multiline = hot.Multiline;
                height = multiline ? lineHeight * 3 + 20 : Math.Max(40, lineHeight + 18);
            }

            if (width <= 0)
                width = resolvedStretchWidth > 0 ? resolvedStretchWidth : resolvedAvailableWidth;
        }

        if (node.Kind is HostNodeKind.View or HostNodeKind.ScrollView)
        {
            var padding = resolvedLayout.Padding;
            if (!frame.HasWidth)
                width = Math.Max(width, padding.Left + padding.Right);

            if (!frame.HasHeight)
                height = Math.Max(height, padding.Top + padding.Bottom);

            if (UsesFlowLayout(node))
            {
                var axis = resolvedLayout.Axis;
                var alignItems = resolvedLayout.AlignItems;
                var gap = resolvedLayout.Gap;
                var measuredAvailableWidth = frame.HasWidth ? width : widthBasis;
                var measuredAvailableHeight = frame.HasHeight ? height : heightBasis;
                var innerWidth = Math.Max(0, measuredAvailableWidth - padding.Left - padding.Right);
                var innerHeight = Math.Max(0, measuredAvailableHeight - padding.Top - padding.Bottom);
                var measured = default(LayoutOutput);
                var childCount = GetArrayLength(node.Children);
                if (childCount > 0)
                {
                    var scratchMark = flowLayoutScratch.Mark();
                    var requestBuffer = flowLayoutScratch.AllocateRequests(childCount);
                    try
                    {
                        var preparedCount = PrepareHostFlowLayoutChildren(node.Children, axis, innerWidth, innerHeight, alignItems, requestBuffer);
                        measured = stackLayoutCalculator!.ComputeFlexLayout(
                            CreateHostFlowInput(
                                frame.HasWidth ? measuredAvailableWidth : null,
                                frame.HasHeight ? measuredAvailableHeight : null,
                                measuredAvailableWidth,
                                measuredAvailableHeight,
                                LayoutRunMode.ComputeSize),
                            CreateHostFlowContainerStyle(
                                resolvedLayout.FlexDirection,
                                resolvedLayout.Direction,
                                resolvedLayout.FlexWrap,
                                gap,
                                alignItems,
                                resolvedLayout.JustifyContent,
                                padding),
                            requestBuffer[..preparedCount],
                            []);
                    }
                    finally
                    {
                        flowLayoutScratch.Rewind(scratchMark);
                    }
                }
                if (!frame.HasWidth)
                {
                    width = stretchWidth > 0
                        ? measuredAvailableWidth
                        : measured.Size.Width;
                }

                if (!frame.HasHeight)
                {
                    height = stretchHeight > 0
                        ? measuredAvailableHeight
                        : measured.Size.Height;
                }
            }
        }

        return new HostNodeMeasurement(
            ClampMeasuredSize(width, frame.MinWidth, frame.IsMinWidthPercent, frame.MaxWidth, frame.IsMaxWidthPercent, widthBasis),
            ClampMeasuredSize(height, frame.MinHeight, frame.IsMinHeightPercent, frame.MaxHeight, frame.IsMaxHeightPercent, heightBasis),
            frame);
    }

    private int PrepareHostFlowLayoutChildren(
        JsObject? children,
        LayoutAxis axis,
        float width,
        float height,
        CrossAlignment alignItems,
        Span<LayoutChildRequest> results)
    {
        if (children is null)
            return 0;

        if (children is JsArray denseChildren)
        {
            var values = denseChildren.AsReadOnlySpan();
            for (var index = 0; index < values.Length; index++)
                results[index] = PrepareHostFlowLayoutChild(values[index], axis, width, height, alignItems);
            return values.Length;
        }

        if (!TryGetArrayLength(children, out var length) || length <= 0)
            return 0;

        for (var index = 0; index < length; index++)
        {
            if (!children.TryGetElement((uint)index, out var childValue))
            {
                results[index] = LayoutChildRequest.Invalid;
                continue;
            }

            results[index] = PrepareHostFlowLayoutChild(childValue, axis, width, height, alignItems);
        }

        return length;
    }

    private LayoutChildRequest CreateHostFlowRequest(
        HostNodeSnapshot snapshot,
        in HostFrameProps frame,
        EdgeInsets margin,
        float patchedWidth,
        float patchedHeight)
    {
        var hot = snapshot.HotMeasureState;
        var cold = snapshot.ColdState;
        var text = snapshot.Kind == HostNodeKind.Text
            ? ResolveTextContent(snapshot)
            : null;
        var units = frame.Units;
        if (LayoutValue.IsSet(patchedWidth))
            units &= ~LayoutValueUnitFlags.WidthPercent;
        if (LayoutValue.IsSet(patchedHeight))
            units &= ~LayoutValueUnitFlags.HeightPercent;
        if (hot.IsFlexBasisPercent)
            units |= LayoutValueUnitFlags.FlexBasisPercent;
        var usesRelativeOffsets = frame.Position == PositionMode.Relative;

        return new LayoutChildRequest(
            Kind: LayoutChildKind.Element,
            Left: usesRelativeOffsets ? frame.Left : LayoutValue.Unset,
            Top: usesRelativeOffsets ? frame.Top : LayoutValue.Unset,
            Right: usesRelativeOffsets ? frame.Right : LayoutValue.Unset,
            Bottom: usesRelativeOffsets ? frame.Bottom : LayoutValue.Unset,
            Width: LayoutValue.IsSet(patchedWidth) ? patchedWidth : frame.Width,
            Height: LayoutValue.IsSet(patchedHeight) ? patchedHeight : frame.Height,
            MinWidth: frame.MinWidth,
            MaxWidth: frame.MaxWidth,
            MinHeight: frame.MinHeight,
            MaxHeight: frame.MaxHeight,
            MarginLeft: margin.Left,
            MarginTop: margin.Top,
            MarginRight: margin.Right,
            MarginBottom: margin.Bottom,
            Text: text,
            FontSize: hot.FontSize,
            FontFamily: cold.FontFamily,
            FontWeight: hot.FontWeight,
            Wrap: hot.Wrap,
            AlignSelf: frame.AlignSelf,
            FlexGrow: hot.FlexGrow,
            FlexShrink: hot.FlexShrink,
            FlexBasis: hot.FlexBasis,
            Units: units);
    }

    private void LayoutHostFlowChildren(
        FlexDirection flexDirection,
        LayoutDirection layoutDirection,
        FlexWrap flexWrap,
        float width,
        float height,
        float gap,
        CrossAlignment alignItems,
        MainAxisJustification justifyContent,
        float paddingLeft,
        float paddingTop,
        float paddingRight,
        float paddingBottom,
        ReadOnlySpan<LayoutChildRequest> requests,
        Span<LayoutFrameData?> frames)
    {
        stackLayoutCalculator!.ComputeFlexLayout(
            LayoutInput.Definite(width, height),
            CreateHostFlowContainerStyle(
                flexDirection,
                layoutDirection,
                flexWrap,
                gap,
                alignItems,
                justifyContent,
                new EdgeInsets(paddingLeft, paddingTop, paddingRight, paddingBottom)),
            requests,
            frames);
    }

    private static LayoutInput CreateHostFlowInput(
        float? knownWidth,
        float? knownHeight,
        float availableWidth,
        float availableHeight,
        LayoutRunMode runMode)
    {
        return new LayoutInput(
            new LayoutKnownSize(knownWidth, knownHeight),
            new LayoutKnownSize(availableWidth, availableHeight),
            new LayoutAvailableSize(
                LayoutAvailableSpace.Definite(availableWidth),
                LayoutAvailableSpace.Definite(availableHeight)),
            runMode);
    }

    private static LayoutContainerStyle CreateHostFlowContainerStyle(
        FlexDirection flexDirection,
        LayoutDirection layoutDirection,
        FlexWrap flexWrap,
        float gap,
        CrossAlignment alignItems,
        MainAxisJustification justifyContent,
        EdgeInsets padding)
    {
        return new LayoutContainerStyle(
            flexDirection,
            layoutDirection,
            flexWrap,
            RowGap: gap,
            ColumnGap: gap,
            alignItems,
            justifyContent,
            new LayoutBoxEdges(padding.Left, padding.Top, padding.Right, padding.Bottom));
    }

    private static int GetArrayLength(JsObject? array)
    {
        return array is not null && TryGetArrayLength(array, out var length) ? length : 0;
    }

    private float FlushChildNodes(
        JsObject? children,
        int childCount,
        string parentId,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        HostLayoutCacheData parentLayout,
        ReadOnlySpan<LayoutFrameData?> childFrames = default)
    {
        var maxBottom = parentLayout.ContentTop;
        if (children is null || childCount <= 0)
            return maxBottom;

        if (children is JsArray denseChildren)
        {
            var values = denseChildren.AsReadOnlySpan();
            var length = Math.Min(childCount, values.Length);
            for (var index = 0; index < length; index++)
            {
                var childFrame = childFrames.IsEmpty ? null : childFrames[index];
                var childLayout = FlushNodeValue(
                    values[index],
                    parentId,
                    childFrame is null ? parentLayout.ContentLeft : parentLeft,
                    childFrame is null ? parentLayout.ContentTop : parentTop,
                    childFrame is null ? parentLayout.ContentWidth : parentWidth,
                    childFrame is null ? parentLayout.ContentHeight : parentHeight,
                    childFrame);
                if (childLayout.HasValue)
                    maxBottom = Math.Max(maxBottom, childLayout.Value.Top + childLayout.Value.Height);
            }

            return maxBottom;
        }

        for (var index = 0; index < childCount; index++)
        {
            if (!children.TryGetElement((uint)index, out var childValue))
                continue;

            var childFrame = childFrames.IsEmpty ? null : childFrames[index];
            var childLayout = FlushNodeValue(
                childValue,
                parentId,
                childFrame is null ? parentLayout.ContentLeft : parentLeft,
                childFrame is null ? parentLayout.ContentTop : parentTop,
                childFrame is null ? parentLayout.ContentWidth : parentWidth,
                childFrame is null ? parentLayout.ContentHeight : parentHeight,
                childFrame);
            if (childLayout.HasValue)
                maxBottom = Math.Max(maxBottom, childLayout.Value.Top + childLayout.Value.Height);
        }

        return maxBottom;
    }

    private LayoutChildRequest PrepareHostFlowLayoutChild(
        in JsValue childValue,
        LayoutAxis axis,
        float width,
        float height,
        CrossAlignment alignItems)
    {
        if (!childValue.TryGetObject(out var child))
            return LayoutChildRequest.Invalid;

        var atoms = propertyAtoms ?? throw new InvalidOperationException("Property atoms are not initialized.");
        var kind = GetHostNodeKind(child);
        if (GetBoolProperty(child, atoms.Hidden) || kind == HostNodeKind.RawText)
            return LayoutChildRequest.Invalid;

        if (kind == HostNodeKind.Spacer)
        {
            var props = TryGetObjectProperty(child, atoms.Props);
            var size = GetFloatProperty(props, atoms.Size);
            var flex = ReadNonNegativeFloat(props, atoms.Flex) ?? 1;
            return new LayoutChildRequest(
                Kind: LayoutChildKind.Spacer,
                Size: size,
                FlexGrow: flex);
        }

        var snapshot = CreateHostNodeSnapshot(child);
        var margin = snapshot.ResolvedLayout.Margin;
        var hot = snapshot.HotMeasureState;
        var rawFrame = snapshot.ResolvedLayout.Frame;
        var childGrow = hot.FlexGrow;
        var childAlign = ResolveChildAlign(alignItems, rawFrame.AlignSelf);
        var measured = MeasureHostNodeSize(
            snapshot,
            axis == LayoutAxis.Row && childGrow > 0 && !hot.HasFlexBasis && !rawFrame.HasWidth ? 0 : width,
            axis == LayoutAxis.Column && childGrow > 0 && !hot.HasFlexBasis && !rawFrame.HasHeight ? 0 : height,
            axis == LayoutAxis.Column && childAlign == CrossAlignment.Stretch ? width : 0,
            0);
        var frame = measured.Frame;
        if (frame.Position == PositionMode.Absolute)
            return LayoutChildRequest.Invalid;

        var patchedWidth = !frame.HasWidth && !(axis == LayoutAxis.Row && childGrow > 0 && !hot.HasFlexBasis)
            ? measured.Width
            : LayoutValue.Unset;
        var patchedHeight = !frame.HasHeight && !(axis == LayoutAxis.Column && childGrow > 0 && !hot.HasFlexBasis)
            ? measured.Height
            : LayoutValue.Unset;

        return CreateHostFlowRequest(snapshot, frame, margin, patchedWidth, patchedHeight);
    }

    private void WriteLastLayout(JsObject node, HostLayoutCacheData layout)
    {
        if (GetHostInstanceState(node) is { } state)
        {
            state.LayoutCache = layout;
            state.HasLayoutCache = true;
        }
    }

    private string ResolveTextContent(HostNodeSnapshot snapshot)
    {
        return snapshot.ColdState.DirectTextContent ?? GatherText(snapshot.Node);
    }

    private string GatherText(JsObject node)
    {
        var children = TryGetObjectProperty(node, propertyAtoms!.Children);
        if (children is null)
            return string.Empty;

        if (!TryGetArrayLength(children, out var length) || length <= 0)
            return string.Empty;

        var parts = new List<string>();
        for (var index = 0; index < length; index++)
        {
            if (!children.TryGetElement((uint)index, out var childValue) || !childValue.TryGetObject(out var child))
                continue;

            var kind = GetHostNodeKind(child);
            if (kind == HostNodeKind.RawText)
            {
                var text = GetStringProperty(child, propertyAtoms.Text);
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
                continue;
            }

            if (kind == HostNodeKind.Text)
            {
                var nested = GatherText(child);
                if (nested.Length > 0)
                    parts.Add(nested);
            }
        }

        return parts.Count == 0 ? string.Empty : string.Concat(parts);
    }

    private static (float Value, LayoutValueUnitFlags Unit) ResolveFrameScalar(JsObject? props, JsObject? style, int atom, LayoutValueUnitFlags percentUnit)
    {
        if (TryGetLayoutScalarProperty(props, atom, percentUnit, out var propValue, out var propUnit))
            return (propValue, propUnit);

        if (TryGetStyleLayoutScalarProperty(style, atom, percentUnit, out var styleValue, out var styleUnit))
            return (styleValue, styleUnit);

        return (LayoutValue.Unset, LayoutValueUnitFlags.None);
    }

    private static bool TryGetLayoutScalarProperty(JsObject? obj, int atom, LayoutValueUnitFlags percentUnit, out float value, out LayoutValueUnitFlags unit)
    {
        value = LayoutValue.Unset;
        unit = LayoutValueUnitFlags.None;
        return obj is not null
            && obj.TryGetPropertyByAtom(atom, out var propertyValue)
            && TryParseLayoutScalar(propertyValue, percentUnit, out value, out unit);
    }

    private static bool TryGetStyleLayoutScalarProperty(JsObject? style, int atom, LayoutValueUnitFlags percentUnit, out float value, out LayoutValueUnitFlags unit)
    {
        value = LayoutValue.Unset;
        unit = LayoutValueUnitFlags.None;
        if (style is null)
            return false;

        if (style is JsArray denseStyle)
        {
            var values = denseStyle.AsReadOnlySpan();
            for (var index = values.Length - 1; index >= 0; index--)
            {
                if (values[index].TryGetObject(out var itemStyle)
                    && TryGetStyleLayoutScalarProperty(itemStyle, atom, percentUnit, out value, out unit))
                {
                    return true;
                }
            }

            return false;
        }

        if (TryGetArrayLength(style, out var length))
        {
            for (var index = length - 1; index >= 0; index--)
            {
                if (!style.TryGetElement((uint)index, out var item) || !item.TryGetObject(out var itemStyle))
                    continue;

                if (TryGetStyleLayoutScalarProperty(itemStyle, atom, percentUnit, out value, out unit))
                    return true;
            }

            return false;
        }

        return TryGetLayoutScalarProperty(style, atom, percentUnit, out value, out unit);
    }

    private static bool TryParseLayoutScalar(JsValue value, LayoutValueUnitFlags percentUnit, out float result, out LayoutValueUnitFlags unit)
    {
        unit = LayoutValueUnitFlags.None;
        if (value.IsNumber)
        {
            result = (float)value.NumberValue;
            return true;
        }

        if (value.TryGetString(out var text) && TryParsePercentValue(text, out result))
        {
            unit = percentUnit;
            return true;
        }

        result = LayoutValue.Unset;
        return false;
    }

    private static bool TryParsePercentValue(string text, out float result)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[^1] != '%')
        {
            result = 0;
            return false;
        }

        return float.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static EdgeInsets ResolvePaddingInsets(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        var borderWidth = Math.Max(0, GetNullableStyleFloatProperty(style, atoms.BorderWidth) ?? 0);
        var resolvedPaddingX = GetNullableStyleFloatProperty(style, atoms.PaddingHorizontal)
            ?? GetNullableStyleFloatProperty(style, atoms.Padding)
            ?? 0;
        var resolvedPaddingY = GetNullableStyleFloatProperty(style, atoms.PaddingVertical)
            ?? GetNullableStyleFloatProperty(style, atoms.Padding)
            ?? 0;
        return new EdgeInsets(
            (GetNullableStyleFloatProperty(style, atoms.PaddingLeft) ?? resolvedPaddingX) + borderWidth,
            (GetNullableStyleFloatProperty(style, atoms.PaddingTop) ?? resolvedPaddingY) + borderWidth,
            (GetNullableStyleFloatProperty(style, atoms.PaddingRight) ?? resolvedPaddingX) + borderWidth,
            (GetNullableStyleFloatProperty(style, atoms.PaddingBottom) ?? resolvedPaddingY) + borderWidth);
    }

    private static EdgeInsets ResolveMarginInsets(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        var resolvedMarginX = GetNullableStyleFloatProperty(style, atoms.MarginHorizontal)
            ?? GetNullableStyleFloatProperty(style, atoms.Margin)
            ?? 0;
        var resolvedMarginY = GetNullableStyleFloatProperty(style, atoms.MarginVertical)
            ?? GetNullableStyleFloatProperty(style, atoms.Margin)
            ?? 0;
        return new EdgeInsets(
            GetNullableStyleFloatProperty(style, atoms.MarginLeft) ?? resolvedMarginX,
            GetNullableStyleFloatProperty(style, atoms.MarginTop) ?? resolvedMarginY,
            GetNullableStyleFloatProperty(style, atoms.MarginRight) ?? resolvedMarginX,
            GetNullableStyleFloatProperty(style, atoms.MarginBottom) ?? resolvedMarginY);
    }

    private static bool IsFlowLayoutStyle(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return GetStyleStringProperty(style, atoms.FlexDirection) is not null
               || GetStyleStringProperty(style, atoms.FlexWrap) is not null
               || GetNullableStyleFloatProperty(style, atoms.Gap).HasValue
               || GetStyleStringProperty(style, atoms.AlignItems) is not null
               || GetStyleStringProperty(style, atoms.JustifyContent) is not null;
    }

    private static PositionMode ResolvePositionMode(
        JsObject? props,
        JsObject? style,
        ReactAppPropertyAtoms atoms,
        DefaultPositionMode defaultPositionMode)
    {
        return ParsePositionMode(
            GetStringProperty(props, atoms.Position) ?? GetStyleStringProperty(style, atoms.Position),
            defaultPositionMode == DefaultPositionMode.Static ? PositionMode.Static : PositionMode.Relative);
    }

    private static FlexDirection ResolveFlexDirection(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return GetStyleStringProperty(style, atoms.FlexDirection) switch
        {
            "row" => FlexDirection.Row,
            "row-reverse" => FlexDirection.RowReverse,
            "column-reverse" => FlexDirection.ColumnReverse,
            _ => FlexDirection.Column
        };
    }

    private static FlexWrap ResolveFlexWrap(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return string.Equals(GetStyleStringProperty(style, atoms.FlexWrap), "wrap", StringComparison.Ordinal)
            ? FlexWrap.Wrap
            : FlexWrap.NoWrap;
    }

    private static BoxSizingMode ResolveBoxSizing(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return GetStyleStringProperty(style, atoms.BoxSizing) switch
        {
            "content-box" => BoxSizingMode.ContentBox,
            _ => BoxSizingMode.BorderBox
        };
    }

    private static PositionMode ParsePositionMode(string? value, PositionMode fallback)
    {
        return value switch
        {
            "absolute" => PositionMode.Absolute,
            "static" => PositionMode.Static,
            "relative" => PositionMode.Relative,
            _ => fallback
        };
    }

    private static LayoutDirection ResolveLayoutDirection(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return string.Equals(GetStyleStringProperty(style, atoms.Direction), "rtl", StringComparison.Ordinal)
            ? LayoutDirection.Rtl
            : LayoutDirection.Ltr;
    }

    private static CrossAlignment ResolveAlignItems(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return ParseCrossAlignment(GetStyleStringProperty(style, atoms.AlignItems), CrossAlignment.Stretch);
    }

    private static MainAxisJustification ResolveJustifyContent(JsObject? style, ReactAppPropertyAtoms atoms)
    {
        return ParseJustifyContent(GetStyleStringProperty(style, atoms.JustifyContent), MainAxisJustification.Start);
    }

    private static CrossAlignment ResolveChildAlign(
        CrossAlignment alignItems,
        CrossAlignment alignSelf)
    {
        return alignSelf == CrossAlignment.Auto
            ? alignItems
            : alignSelf;
    }

    private static CrossAlignment ParseCrossAlignment(
        string? value,
        CrossAlignment fallback)
    {
        return value switch
        {
            "start" => CrossAlignment.Start,
            "center" => CrossAlignment.Center,
            "end" => CrossAlignment.End,
            "stretch" => CrossAlignment.Stretch,
            "auto" => CrossAlignment.Auto,
            _ => fallback
        };
    }

    private static MainAxisJustification ParseJustifyContent(
        string? value,
        MainAxisJustification fallback)
    {
        return value switch
        {
            "center" => MainAxisJustification.Center,
            "end" => MainAxisJustification.End,
            "space-between" => MainAxisJustification.SpaceBetween,
            "space-around" => MainAxisJustification.SpaceAround,
            "start" => MainAxisJustification.Start,
            _ => fallback
        };
    }

    private static LayoutFrameData ResolveFrameMetrics(
        in HostFrameProps frame,
        float parentWidth,
        float parentHeight,
        float fallbackWidth,
        float fallbackHeight,
        EdgeInsets margin)
    {
        var usesInsets = frame.Position != PositionMode.Static;
        var resolvedX = ResolveAxisFrame(
            usesInsets ? frame.Left : LayoutValue.Unset,
            usesInsets && frame.IsLeftPercent,
            usesInsets ? frame.Right : LayoutValue.Unset,
            usesInsets && frame.IsRightPercent,
            frame.Width,
            frame.IsWidthPercent,
            Math.Max(0, parentWidth - margin.Left - margin.Right),
            fallbackWidth);
        var resolvedY = ResolveAxisFrame(
            usesInsets ? frame.Top : LayoutValue.Unset,
            usesInsets && frame.IsTopPercent,
            usesInsets ? frame.Bottom : LayoutValue.Unset,
            usesInsets && frame.IsBottomPercent,
            frame.Height,
            frame.IsHeightPercent,
            Math.Max(0, parentHeight - margin.Top - margin.Bottom),
            fallbackHeight);
        return new LayoutFrameData(
            resolvedX.Start + margin.Left,
            resolvedY.Start + margin.Top,
            resolvedX.Size,
            resolvedY.Size);
    }

    private static AxisFrame ResolveAxisFrame(
        float start,
        bool startIsPercent,
        float end,
        bool endIsPercent,
        float size,
        bool sizeIsPercent,
        float parentSize,
        float fallbackSize)
    {
        var resolvedStart = LayoutValue.Resolve(start, startIsPercent, parentSize);
        var resolvedEnd = LayoutValue.Resolve(end, endIsPercent, parentSize);
        var resolvedExplicitSize = LayoutValue.Resolve(size, sizeIsPercent, parentSize);
        var hasSize = LayoutValue.IsSet(size);
        var hasStart = LayoutValue.IsSet(start);
        var hasEnd = LayoutValue.IsSet(end);
        var resolvedSize = hasSize
            ? resolvedExplicitSize
            : (hasEnd ? Math.Max(0, parentSize - (hasStart ? resolvedStart : 0) - resolvedEnd) : fallbackSize);
        var finalStart = hasStart
            ? resolvedStart
            : (hasEnd ? Math.Max(0, parentSize - resolvedSize - resolvedEnd) : 0);
        return new AxisFrame(finalStart, resolvedSize);
    }

    private static HostFrameProps ResolveBoxSizingFrame(
        in HostFrameProps frame,
        in EdgeInsets padding,
        float borderWidth,
        BoxSizingMode boxSizing,
        float widthBasis,
        float heightBasis)
    {
        if (boxSizing != BoxSizingMode.ContentBox)
            return frame;

        var units = frame.Units;
        var horizontalInsets = padding.Left + padding.Right;
        var verticalInsets = padding.Top + padding.Bottom;
        var horizontalBorder = Math.Max(0, borderWidth * 2);
        var verticalBorder = Math.Max(0, borderWidth * 2);
        var horizontalPadding = Math.Max(0, horizontalInsets - horizontalBorder);
        var verticalPadding = Math.Max(0, verticalInsets - verticalBorder);
        var width = frame.Width;
        var height = frame.Height;
        var minWidth = frame.MinWidth;
        var maxWidth = frame.MaxWidth;
        var minHeight = frame.MinHeight;
        var maxHeight = frame.MaxHeight;

        if (frame.HasWidth)
        {
            width = Math.Max(LayoutValue.Resolve(frame.Width, frame.IsWidthPercent, widthBasis) + horizontalBorder, horizontalInsets);
            units &= ~LayoutValueUnitFlags.WidthPercent;
        }

        if (frame.HasHeight)
        {
            height = Math.Max(LayoutValue.Resolve(frame.Height, frame.IsHeightPercent, heightBasis) + verticalBorder, verticalInsets);
            units &= ~LayoutValueUnitFlags.HeightPercent;
        }

        if (frame.HasMinWidth)
        {
            minWidth = Math.Max(LayoutValue.Resolve(frame.MinWidth, frame.IsMinWidthPercent, widthBasis) + horizontalBorder, horizontalPadding + horizontalBorder);
            units &= ~LayoutValueUnitFlags.MinWidthPercent;
        }

        if (frame.HasMaxWidth)
        {
            maxWidth = Math.Max(LayoutValue.Resolve(frame.MaxWidth, frame.IsMaxWidthPercent, widthBasis) + horizontalBorder, horizontalPadding + horizontalBorder);
            units &= ~LayoutValueUnitFlags.MaxWidthPercent;
        }

        if (frame.HasMinHeight)
        {
            minHeight = Math.Max(LayoutValue.Resolve(frame.MinHeight, frame.IsMinHeightPercent, heightBasis) + verticalBorder, verticalPadding + verticalBorder);
            units &= ~LayoutValueUnitFlags.MinHeightPercent;
        }

        if (frame.HasMaxHeight)
        {
            maxHeight = Math.Max(LayoutValue.Resolve(frame.MaxHeight, frame.IsMaxHeightPercent, heightBasis) + verticalBorder, verticalPadding + verticalBorder);
            units &= ~LayoutValueUnitFlags.MaxHeightPercent;
        }

        return new HostFrameProps(
            frame.Left,
            frame.Top,
            frame.Right,
            frame.Bottom,
            width,
            height,
            minWidth,
            maxWidth,
            minHeight,
            maxHeight,
            frame.Position,
            frame.AlignSelf,
            units);
    }

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily, int fontWeight)
    {
        return backendServices.Text.MeasureTextWidth(
            text,
            new SceneTextStyle(fontSize, Font: new SceneFont(fontSize, fontFamily, fontWeight)));
    }

    private float MeasureTextHeight(string text, float width, float fontSize, string? fontFamily, int fontWeight)
    {
        return backendServices.Text.MeasureTextHeight(
            text,
            width,
            new SceneTextStyle(fontSize, WrapText: true, Font: new SceneFont(fontSize, fontFamily, fontWeight)));
    }

    private static float ClampMeasuredSize(float value, float minValue, bool minIsPercent, float maxValue, bool maxIsPercent, float availableSize)
    {
        var result = float.IsFinite(value) ? value : 0;
        var resolvedMin = LayoutValue.Resolve(minValue, minIsPercent, availableSize);
        var resolvedMax = LayoutValue.Resolve(maxValue, maxIsPercent, availableSize);
        if (LayoutValue.IsSet(resolvedMin))
            result = Math.Max(result, Math.Max(0, resolvedMin));

        if (LayoutValue.IsSet(resolvedMax))
            result = Math.Min(result, Math.Max(LayoutValue.IsSet(resolvedMin) ? resolvedMin : 0, resolvedMax));

        return Math.Max(0, result);
    }

    private static float ResolveMeasuredContentHeight(float maxBottom, float contentTop, float paddingBottom)
    {
        return Math.Max(0, maxBottom - contentTop + paddingBottom);
    }

    private static float ResolveMeasuredContentWidth(float maxRight, float contentLeft, float paddingRight)
    {
        return Math.Max(0, maxRight - contentLeft + paddingRight);
    }

    private static float ReadPositiveFloat(JsObject? obj, int atom)
    {
        var value = GetNullableFloatProperty(obj, atom);
        return value.HasValue && value.Value > 0 ? value.Value : 0;
    }

    private static float ReadPositiveStyleFloat(JsObject? style, int atom)
    {
        var value = GetNullableStyleFloatProperty(style, atom);
        return value.HasValue && value.Value > 0 ? value.Value : 0;
    }

    private static float? ReadNonNegativeFloat(JsObject? obj, int atom)
    {
        var value = GetNullableFloatProperty(obj, atom);
        return value.HasValue && value.Value >= 0 ? value.Value : null;
    }

    private static float? ReadNonNegativeStyleFloat(JsObject? style, int atom)
    {
        var value = GetNullableStyleFloatProperty(style, atom);
        return value.HasValue && value.Value >= 0 ? value.Value : null;
    }

    private readonly record struct AxisFrame(float Start, float Size);

    private readonly record struct EdgeInsets(float Left, float Top, float Right, float Bottom);

    private readonly record struct HostLayoutCacheData(
        float Left,
        float Top,
        float Width,
        float Height,
        float ContentLeft,
        float ContentTop,
        float ContentWidth,
        float ContentHeight);

    private readonly record struct TextIntrinsicSize(int Width, int Height);

    [Flags]
    private enum HostMeasureFlags : byte
    {
        None = 0,
        Wrap = 1 << 0,
        Multiline = 1 << 1,
        FlexBasisPercent = 1 << 2
    }

    private readonly struct HostHotMeasureState
    {
        public HostHotMeasureState(
            HostNodeKind Kind,
            float FlexGrow,
            float FlexShrink,
            float FlexBasis,
            float FontSize,
            int FontWeight,
            float LineHeight,
            HostMeasureFlags Flags)
        {
            this.Kind = Kind;
            this.FlexGrow = FlexGrow;
            this.FlexShrink = FlexShrink;
            this.FlexBasis = FlexBasis;
            this.FontSize = FontSize;
            this.FontWeight = FontWeight;
            this.LineHeight = LineHeight;
            this.Flags = Flags;
        }

        public HostNodeKind Kind { get; }
        public float FlexGrow { get; }
        public float FlexShrink { get; }
        public float FlexBasis { get; }
        public float FontSize { get; }
        public int FontWeight { get; }
        public float LineHeight { get; }
        public HostMeasureFlags Flags { get; }
        public bool Wrap => (Flags & HostMeasureFlags.Wrap) != 0;
        public bool Multiline => (Flags & HostMeasureFlags.Multiline) != 0;
        public bool HasFlexBasis => LayoutValue.IsSet(FlexBasis);
        public bool IsFlexBasisPercent => (Flags & HostMeasureFlags.FlexBasisPercent) != 0;
    }

    private readonly struct HostColdState
    {
        public HostColdState(
            string? FontFamily,
            string? DirectTextContent,
            string? TextInputValue,
            string? TextInputPlaceholder,
            string? ImageSource,
            string? ImagePlaceholderSource)
        {
            this.FontFamily = FontFamily;
            this.DirectTextContent = DirectTextContent;
            this.TextInputValue = TextInputValue;
            this.TextInputPlaceholder = TextInputPlaceholder;
            this.ImageSource = ImageSource;
            this.ImagePlaceholderSource = ImagePlaceholderSource;
        }

        public string? FontFamily { get; }
        public string? DirectTextContent { get; }
        public string? TextInputValue { get; }
        public string? TextInputPlaceholder { get; }
        public string? ImageSource { get; }
        public string? ImagePlaceholderSource { get; }
    }

    private readonly struct HostFrameProps
    {
        public HostFrameProps(
            float Left,
            float Top,
            float Right,
            float Bottom,
            float Width,
            float Height,
            float MinWidth,
            float MaxWidth,
            float MinHeight,
            float MaxHeight,
            PositionMode Position,
            CrossAlignment AlignSelf,
            LayoutValueUnitFlags Units)
        {
            this.Left = Left;
            this.Top = Top;
            this.Right = Right;
            this.Bottom = Bottom;
            this.Width = Width;
            this.Height = Height;
            this.MinWidth = MinWidth;
            this.MaxWidth = MaxWidth;
            this.MinHeight = MinHeight;
            this.MaxHeight = MaxHeight;
            this.Position = Position;
            this.AlignSelf = AlignSelf;
            this.Units = Units;
        }

        public float Left { get; }
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
        public float Width { get; }
        public float Height { get; }
        public float MinWidth { get; }
        public float MaxWidth { get; }
        public float MinHeight { get; }
        public float MaxHeight { get; }
        public PositionMode Position { get; }
        public CrossAlignment AlignSelf { get; }
        public LayoutValueUnitFlags Units { get; }

        public bool HasLeft => LayoutValue.IsSet(Left);
        public bool HasTop => LayoutValue.IsSet(Top);
        public bool HasRight => LayoutValue.IsSet(Right);
        public bool HasBottom => LayoutValue.IsSet(Bottom);
        public bool HasWidth => LayoutValue.IsSet(Width);
        public bool HasHeight => LayoutValue.IsSet(Height);
        public bool HasMinWidth => LayoutValue.IsSet(MinWidth);
        public bool HasMaxWidth => LayoutValue.IsSet(MaxWidth);
        public bool HasMinHeight => LayoutValue.IsSet(MinHeight);
        public bool HasMaxHeight => LayoutValue.IsSet(MaxHeight);
        public bool IsLeftPercent => (Units & LayoutValueUnitFlags.LeftPercent) != 0;
        public bool IsTopPercent => (Units & LayoutValueUnitFlags.TopPercent) != 0;
        public bool IsRightPercent => (Units & LayoutValueUnitFlags.RightPercent) != 0;
        public bool IsBottomPercent => (Units & LayoutValueUnitFlags.BottomPercent) != 0;
        public bool IsWidthPercent => (Units & LayoutValueUnitFlags.WidthPercent) != 0;
        public bool IsHeightPercent => (Units & LayoutValueUnitFlags.HeightPercent) != 0;
        public bool IsMinWidthPercent => (Units & LayoutValueUnitFlags.MinWidthPercent) != 0;
        public bool IsMaxWidthPercent => (Units & LayoutValueUnitFlags.MaxWidthPercent) != 0;
        public bool IsMinHeightPercent => (Units & LayoutValueUnitFlags.MinHeightPercent) != 0;
        public bool IsMaxHeightPercent => (Units & LayoutValueUnitFlags.MaxHeightPercent) != 0;
    }

    private readonly record struct HostNodeMeasurement(
        float Width,
        float Height,
        HostFrameProps Frame);

    private readonly struct HostResolvedLayout
    {
        public HostResolvedLayout(
            HostFrameProps Frame,
            EdgeInsets Margin,
            EdgeInsets Padding,
            float BorderWidth,
            BoxSizingMode BoxSizing,
            bool IsFlowLayout,
            FlexDirection FlexDirection,
            FlexWrap FlexWrap,
            LayoutDirection Direction,
            CrossAlignment AlignItems,
            MainAxisJustification JustifyContent,
            float Gap)
        {
            this.Frame = Frame;
            this.Margin = Margin;
            this.Padding = Padding;
            this.BorderWidth = BorderWidth;
            this.BoxSizing = BoxSizing;
            this.IsFlowLayout = IsFlowLayout;
            this.FlexDirection = FlexDirection;
            this.FlexWrap = FlexWrap;
            this.Direction = Direction;
            this.AlignItems = AlignItems;
            this.JustifyContent = JustifyContent;
            this.Gap = Gap;
        }

        public HostFrameProps Frame { get; }
        public EdgeInsets Margin { get; }
        public EdgeInsets Padding { get; }
        public float BorderWidth { get; }
        public BoxSizingMode BoxSizing { get; }
        public bool IsFlowLayout { get; }
        public FlexDirection FlexDirection { get; }
        public FlexWrap FlexWrap { get; }
        public LayoutDirection Direction { get; }
        public LayoutAxis Axis => FlexLayout.ResolveAxis(FlexDirection);
        public CrossAlignment AlignItems { get; }
        public MainAxisJustification JustifyContent { get; }
        public float Gap { get; }
    }

    private readonly record struct HostNodeSnapshot(
        JsObject Node,
        JsObject Props,
        JsObject? Style,
        JsObject? Children,
        HostNodeKind Kind,
        string RuntimeId,
        HostResolvedLayout ResolvedLayout,
        HostHotMeasureState HotMeasureState,
        HostColdState ColdState);

    private readonly record struct DirtyFlushContext(
        string ParentId,
        float ParentLeft,
        float ParentTop,
        float ParentWidth,
        float ParentHeight,
        LayoutFrameData? OverrideFrame);

    private enum FlushTraversalStage : byte
    {
        Enter,
        Finalize
    }

    private struct FlushTraversalWorkItem
    {
        public FlushTraversalStage Stage;
        public JsObject Node;
        public string ParentId;
        public float ParentLeft;
        public float ParentTop;
        public float ParentWidth;
        public float ParentHeight;
        public LayoutFrameData? OverrideFrame;
        public int ParentFinalizeIndex;
        public bool IsRoot;
        public HostNodeKind Kind;
        public string RuntimeId;
        public JsObject? Style;
        public HostLayoutCacheData Layout;
        public float PaddingRight;
        public float PaddingBottom;
        public float MaxRight;
        public float MaxBottom;
    }

    private sealed class FlushTraversalScratch
    {
        private FlushTraversalWorkItem[] buffer = [];
        private int count;

        public void Clear()
        {
            if (count > 0)
                Array.Clear(buffer, 0, count);
            count = 0;
        }

        public int Push(FlushTraversalWorkItem item)
        {
            EnsureCapacity(count + 1);
            buffer[count] = item;
            return count++;
        }

        public bool TryPop(out FlushTraversalWorkItem item)
        {
            if (count == 0)
            {
                item = default;
                return false;
            }

            count--;
            item = buffer[count];
            buffer[count] = default;
            return true;
        }

        public ref FlushTraversalWorkItem GetReference(int index)
        {
            return ref buffer[index];
        }

        private void EnsureCapacity(int requiredLength)
        {
            if (buffer.Length >= requiredLength)
                return;

            Array.Resize(ref buffer, Math.Max(requiredLength, Math.Max(16, buffer.Length * 2)));
        }
    }

    private sealed class FlowLayoutScratchArena
    {
        private LayoutChildRequest[] requestBuffer = [];
        private LayoutFrameData?[] frameBuffer = [];
        private int requestOffset;
        private int frameOffset;

        public ScratchMark Mark() => new(requestOffset, frameOffset);

        public Span<LayoutChildRequest> AllocateRequests(int length)
        {
            if (length <= 0)
                return [];

            EnsureRequestCapacity(requestOffset + length);
            var start = requestOffset;
            requestOffset += length;
            return requestBuffer.AsSpan(start, length);
        }

        public Span<LayoutFrameData?> AllocateFrames(int length)
        {
            if (length <= 0)
                return [];

            EnsureFrameCapacity(frameOffset + length);
            var start = frameOffset;
            frameOffset += length;
            return frameBuffer.AsSpan(start, length);
        }

        public void Rewind(ScratchMark mark)
        {
            if (mark.RequestOffset == 0 && requestOffset > 0)
                Array.Clear(requestBuffer, 0, requestOffset);
            requestOffset = mark.RequestOffset;
            frameOffset = mark.FrameOffset;
        }

        private void EnsureRequestCapacity(int requiredLength)
        {
            if (requestBuffer.Length >= requiredLength)
                return;

            Array.Resize(ref requestBuffer, Math.Max(requiredLength, Math.Max(16, requestBuffer.Length * 2)));
        }

        private void EnsureFrameCapacity(int requiredLength)
        {
            if (frameBuffer.Length >= requiredLength)
                return;

            Array.Resize(ref frameBuffer, Math.Max(requiredLength, Math.Max(16, frameBuffer.Length * 2)));
        }

        public readonly record struct ScratchMark(int RequestOffset, int FrameOffset);
    }

    private sealed class HostInstanceShapeCache
    {
        public const int RuntimeIdSlot = 0;
        public const int PublicIdSlot = 1;
        public const int TypeSlot = 2;
        public const int ParentSlot = 3;
        public const int PropsSlot = 4;
        public const int ChildrenSlot = 5;
        public const int HiddenSlot = 6;
        public const int TextSlot = 7;

        private HostInstanceShapeCache(StaticNamedPropertyLayout elementShape, StaticNamedPropertyLayout textShape)
        {
            ElementShape = elementShape;
            TextShape = textShape;
        }

        public StaticNamedPropertyLayout ElementShape { get; }

        public StaticNamedPropertyLayout TextShape { get; }

        public static HostInstanceShapeCache Create(JsRealm realm, ReactAppPropertyAtoms atoms)
        {
            var elementShape = realm.EmptyShape;
            elementShape = elementShape.GetOrAddTransition(atoms.RuntimeId, JsShapePropertyFlags.Open, out var runtimeIdInfo);
            elementShape = elementShape.GetOrAddTransition(atoms.PublicId, JsShapePropertyFlags.Open, out var publicIdInfo);
            elementShape = elementShape.GetOrAddTransition(atoms.Type, JsShapePropertyFlags.Open, out var typeInfo);
            elementShape = elementShape.GetOrAddTransition(atoms.Parent, JsShapePropertyFlags.Open, out var parentInfo);
            elementShape = elementShape.GetOrAddTransition(atoms.Props, JsShapePropertyFlags.Open, out var propsInfo);
            elementShape = elementShape.GetOrAddTransition(atoms.Children, JsShapePropertyFlags.Open, out var childrenInfo);
            elementShape = elementShape.GetOrAddTransition(atoms.Hidden, JsShapePropertyFlags.Open, out var hiddenInfo);

            Debug.Assert(runtimeIdInfo.Slot == RuntimeIdSlot);
            Debug.Assert(publicIdInfo.Slot == PublicIdSlot);
            Debug.Assert(typeInfo.Slot == TypeSlot);
            Debug.Assert(parentInfo.Slot == ParentSlot);
            Debug.Assert(propsInfo.Slot == PropsSlot);
            Debug.Assert(childrenInfo.Slot == ChildrenSlot);
            Debug.Assert(hiddenInfo.Slot == HiddenSlot);

            var textShape = elementShape.GetOrAddTransition(atoms.Text, JsShapePropertyFlags.Open, out var textInfo);
            Debug.Assert(textInfo.Slot == TextSlot);

            return new HostInstanceShapeCache(elementShape, textShape);
        }
    }

    private sealed class HostInstanceState
    {
        public HostInstanceState(HostNodeKind kind, string runtimeId, JsArray children)
        {
            Kind = kind;
            RuntimeId = runtimeId;
            Children = children;
        }

        public readonly HostNodeKind Kind;

        public readonly string RuntimeId;

        public JsArray Children;

        public JsObject? Parent;

        public JsObject? Props;

        public JsObject? Style;

        public HostResolvedLayout ResolvedLayout;

        public HostHotMeasureState HotMeasureState;

        public HostColdState ColdState;

        public bool HasLayoutCache;

        public HostLayoutCacheData LayoutCache;
    }

    private enum HostNodeKind
    {
        Unknown,
        Scene,
        View,
        ScrollView,
        Text,
        TextInput,
        Image,
        Spacer,
        RawText
    }
}

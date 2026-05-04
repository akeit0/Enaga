using System.Buffers;
using Enaga.Scene;

namespace Enaga.Rendering;

internal static class SceneDamageEstimator
{
    private const float ShadowPadding = 24f;
    private const float BorderPadding = 4f;
    private const double FullFrameAreaThreshold = 0.6;
    private const int MaxRectCountBeforeFullFrame = 12;

    public static ReadOnlySpan<SceneDamageRect> Resolve(
        SceneLayoutCommit? previousCommit,
        SceneLayoutCommit nextCommit,
        ReadOnlySpan<SceneDamageRect> sourceDirtyRects,
        SceneDamageReason damageReasons,
        int width,
        int height,
        bool forceFullFrame,
        SceneDamageRectBufferWriter resultBuffer,
        SceneDamageRectBufferWriter scratchBuffer)
    {
        var viewportWidth = Math.Max(1, width);
        var viewportHeight = Math.Max(1, height);
        if (forceFullFrame || previousCommit is null || RequiresFullFrame(damageReasons))
        {
            resultBuffer.Clear();
            resultBuffer.Add(new SceneDamageRect(0, 0, viewportWidth, viewportHeight));
            return resultBuffer.WrittenSpan;
        }

        if (!sourceDirtyRects.IsEmpty && !damageReasons.HasFlag(SceneDamageReason.FullFrameFallback))
            return NormalizeInto(sourceDirtyRects, viewportWidth, viewportHeight, resultBuffer);

        if (ReferenceEquals(previousCommit, nextCommit))
            return damageReasons.HasFlag(SceneDamageReason.Animation)
                ? BuildAnimatedShaderDirtyRects(nextCommit, viewportWidth, viewportHeight, resultBuffer)
                : ReadOnlySpan<SceneDamageRect>.Empty;

        var estimated = EstimateFromCommitDiff(previousCommit, nextCommit, viewportWidth, viewportHeight, resultBuffer);
        if (!damageReasons.HasFlag(SceneDamageReason.Animation))
            return estimated;

        return MergeDirtyRects(
            estimated,
            BuildAnimatedShaderDirtyRects(nextCommit, viewportWidth, viewportHeight, scratchBuffer),
            viewportWidth,
            viewportHeight,
            resultBuffer);
    }

    public static SceneDamageRect? GetBoxDamageRect(
        SceneLayoutCommit commit,
        SceneNodeId id,
        int viewportWidth,
        int viewportHeight)
    {
        if (!commit.Layout.TryGetValue(id, out var box))
            return null;

        var padding = Math.Max(BorderPadding, box.BorderWidth) +
                      (box.BackgroundShadows is { Length: > 0 } || box.TextStyle?.TextShadows is { Length: > 0 } ? ShadowPadding : 0);
        var indicatorTopPadding = box.NodeKind == SceneNodeKind.TextInput && box.IsFocused
            ? 28f
            : 0f;
        var ancestorScrollOffsetY = GetAncestorScrollOffsetY(commit, id);
        var left = box.AbsLeft - padding;
        var top = box.AbsTop - padding - indicatorTopPadding - ancestorScrollOffsetY;
        var right = box.AbsLeft + box.Width + padding;
        var bottom = box.AbsTop + box.Height + padding - ancestorScrollOffsetY;
        var clipped = IntersectWithClippingAncestorViewports(commit, id, left, top, right, bottom);
        if (clipped is null)
            return null;

        return NormalizeRect(
            (int)Math.Floor(clipped.Value.Left),
            (int)Math.Floor(clipped.Value.Top),
            (int)Math.Ceiling(clipped.Value.Right - clipped.Value.Left),
            (int)Math.Ceiling(clipped.Value.Bottom - clipped.Value.Top),
            viewportWidth,
            viewportHeight);
    }

    private static bool RequiresFullFrame(SceneDamageReason damageReasons)
    => (damageReasons & (SceneDamageReason.Resize | SceneDamageReason.RuntimeReload | SceneDamageReason.ErrorOverlay | SceneDamageReason.FontCatalogChanged)) != 0;

    private static ReadOnlySpan<SceneDamageRect> EstimateFromCommitDiff(
        SceneLayoutCommit previousCommit,
        SceneLayoutCommit nextCommit,
        int viewportWidth,
        int viewportHeight,
        SceneDamageRectBufferWriter dirtyRects)
    {
        dirtyRects.Clear();
        var ids = new HashSet<SceneNodeId>();
        ids.EnsureCapacity(previousCommit.Layout.Count + nextCommit.Layout.Count + previousCommit.Nodes.Count + nextCommit.Nodes.Count);
        AddKeys(ids, previousCommit.Layout.Keys);
        AddKeys(ids, nextCommit.Layout.Keys);
        AddKeys(ids, previousCommit.Nodes.Keys);
        AddKeys(ids, nextCommit.Nodes.Keys);

        foreach (var id in ids)
        {
            previousCommit.Layout.TryGetValue(id, out var previousBox);
            nextCommit.Layout.TryGetValue(id, out var nextBox);
            previousCommit.Nodes.TryGetValue(id, out var previousNode);
            nextCommit.Nodes.TryGetValue(id, out var nextNode);

            if (previousBox is not null && nextBox is not null &&
                previousNode is not null && nextNode is not null &&
                previousBox == nextBox &&
                NodesEqual(previousNode, nextNode))
            {
                continue;
            }

            if (previousBox is not null && nextBox is not null &&
                previousNode is not null && nextNode is not null &&
                CanSkipOpaqueContainerDamage(previousCommit, nextCommit, previousNode, nextNode, previousBox, nextBox))
            {
                continue;
            }

            if (previousBox is not null)
                AddBoxRect(dirtyRects, previousCommit, id, previousBox, viewportWidth, viewportHeight);
            if (nextBox is not null)
                AddBoxRect(dirtyRects, nextCommit, id, nextBox, viewportWidth, viewportHeight);
        }

        return FinalizeDirtyRects(dirtyRects, viewportWidth, viewportHeight);
    }

    private static bool NodesEqual(SceneGraphNode previousNode, SceneGraphNode nextNode)
    {
        if (!NodesEqualIgnoringChildren(previousNode, nextNode) ||
            previousNode.Children.Length != nextNode.Children.Length)
        {
            return false;
        }

        for (var index = 0; index < previousNode.Children.Length; index++)
        {
            if (previousNode.Children[index] != nextNode.Children[index])
                return false;
        }

        return true;
    }

    private static bool NodesEqualIgnoringChildren(SceneGraphNode previousNode, SceneGraphNode nextNode)
    {
        return previousNode.NodeKind == nextNode.NodeKind &&
               previousNode.ParentId == nextNode.ParentId &&
               string.Equals(previousNode.Label, nextNode.Label, StringComparison.Ordinal);
    }

    private static bool CanSkipOpaqueContainerDamage(
        SceneLayoutCommit previousCommit,
        SceneLayoutCommit nextCommit,
        SceneGraphNode previousNode,
        SceneGraphNode nextNode,
        SceneLayoutBox previousBox,
        SceneLayoutBox nextBox)
    {
        return previousBox == nextBox &&
               NodesEqualIgnoringChildren(previousNode, nextNode) &&
               IsOpaquePaintBlocker(previousBox) &&
               !HasPositionedChildMutation(previousCommit, nextCommit, previousNode.Children, nextNode.Children) &&
               HasInsertionRemovalOnlyChildMutation(previousNode.Children, nextNode.Children);
    }

    private static bool HasPositionedChildMutation(
        SceneLayoutCommit previousCommit,
        SceneLayoutCommit nextCommit,
        ReadOnlySpan<SceneNodeId> previousChildren,
        ReadOnlySpan<SceneNodeId> nextChildren)
    {
        for (var index = 0; index < previousChildren.Length; index++)
        {
            var childId = previousChildren[index];
            if (!Contains(nextChildren, childId) &&
                previousCommit.Layout.TryGetValue(childId, out var previousBox) &&
                previousBox.IsPositioned)
            {
                return true;
            }
        }

        for (var index = 0; index < nextChildren.Length; index++)
        {
            var childId = nextChildren[index];
            if (!Contains(previousChildren, childId) &&
                nextCommit.Layout.TryGetValue(childId, out var nextBox) &&
                nextBox.IsPositioned)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(ReadOnlySpan<SceneNodeId> ids, SceneNodeId id)
    {
        for (var index = 0; index < ids.Length; index++)
            if (ids[index] == id)
                return true;

        return false;
    }

    private static bool IsOpaquePaintBlocker(SceneLayoutBox box)
    {
        return !string.IsNullOrWhiteSpace(box.BackgroundColor) ||
               !string.IsNullOrWhiteSpace(box.BackgroundImageSource) ||
               box.BackgroundGradient is not null ||
               box.BackgroundShader is not null;
    }

    private static bool HasInsertionRemovalOnlyChildMutation(ReadOnlySpan<SceneNodeId> previousChildren, ReadOnlySpan<SceneNodeId> nextChildren)
    {
        if (previousChildren.Length == nextChildren.Length)
        {
            var identical = true;
            for (var index = 0; index < previousChildren.Length; index++)
            {
                if (previousChildren[index] == nextChildren[index])
                    continue;

                identical = false;
                break;
            }

            if (identical)
                return false;
        }

        var nextPositions = new Dictionary<SceneNodeId, int>(nextChildren.Length);
        for (var index = 0; index < nextChildren.Length; index++)
            nextPositions[nextChildren[index]] = index;

        var lastMatchedIndex = -1;
        var matchedAny = false;
        for (var index = 0; index < previousChildren.Length; index++)
        {
            var childId = previousChildren[index];
            if (!nextPositions.TryGetValue(childId, out var nextIndex))
                continue;

            if (nextIndex < lastMatchedIndex)
                return false;

            lastMatchedIndex = nextIndex;
            matchedAny = true;
        }

        return matchedAny || previousChildren.Length != nextChildren.Length;
    }

    private static void AddKeys(HashSet<SceneNodeId> ids, IEnumerable<SceneNodeId> keys)
    {
        foreach (var key in keys)
            ids.Add(key);
    }

    private static void AddDirtyRect(SceneDamageRectBufferWriter dirtyRects, SceneDamageRect? rect)
    {
        if (rect is { } normalized)
            dirtyRects.Add(normalized);
    }

    private static void AddBoxRect(
        SceneDamageRectBufferWriter dirtyRects,
        SceneLayoutCommit commit,
        SceneNodeId id,
        SceneLayoutBox box,
        int viewportWidth,
        int viewportHeight)
    {
        AddDirtyRect(dirtyRects, GetBoxDamageRect(commit, id, viewportWidth, viewportHeight));
    }

    private static float GetAncestorScrollOffsetY(SceneLayoutCommit commit, SceneNodeId id)
    {
        var offsetY = 0f;
        var currentId = id;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                offsetY += parentBox.ScrollY;
            }

            currentId = parentId;
        }

        return offsetY;
    }

    private static float GetAncestorScrollOffsetX(SceneLayoutCommit commit, SceneNodeId id)
    {
        var offsetX = 0f;
        var currentId = id;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                parentBox.NodeKind == SceneNodeKind.ScrollView)
            {
                offsetX += parentBox.ScrollX;
            }

            currentId = parentId;
        }

        return offsetX;
    }

    private static ScreenRect? IntersectWithClippingAncestorViewports(
        SceneLayoutCommit commit,
        SceneNodeId id,
        float left,
        float top,
        float right,
        float bottom)
    {
        ScreenRect? result = ScreenRect.Intersect(
            new ScreenRect(left, top, right, bottom),
            new ScreenRect(0, 0, commit.Viewport.Width, commit.Viewport.Height));
        if (result is null)
            return null;

        var currentId = id;
        while (commit.Nodes.TryGetValue(currentId, out var node) && node.ParentId is { } parentId)
        {
            if (commit.Layout.TryGetValue(parentId, out var parentBox) &&
                (parentBox.NodeKind == SceneNodeKind.ScrollView || parentBox.ClipContent))
            {
                var ancestorOffsetX = GetAncestorScrollOffsetX(commit, parentId);
                var ancestorOffsetY = GetAncestorScrollOffsetY(commit, parentId);
                var clipRect = new ScreenRect(
                    parentBox.AbsLeft - ancestorOffsetX,
                    parentBox.AbsTop - ancestorOffsetY,
                    parentBox.AbsLeft + parentBox.Width - ancestorOffsetX,
                    parentBox.AbsTop + parentBox.Height - ancestorOffsetY);
                result = ScreenRect.Intersect(result.Value, clipRect);
                if (result is null)
                    return null;
            }

            currentId = parentId;
        }

        return result;
    }

    private static ReadOnlySpan<SceneDamageRect> NormalizeInto(
        ReadOnlySpan<SceneDamageRect> dirtyRects,
        int viewportWidth,
        int viewportHeight,
        SceneDamageRectBufferWriter normalized)
    {
        normalized.Clear();
        for (var index = 0; index < dirtyRects.Length; index++)
        {
            var rect = dirtyRects[index];
            AddDirtyRect(
                normalized,
                NormalizeRect(rect.X, rect.Y, rect.Width, rect.Height, viewportWidth, viewportHeight));
        }

        return FinalizeDirtyRects(normalized, viewportWidth, viewportHeight);
    }

    private static ReadOnlySpan<SceneDamageRect> BuildAnimatedShaderDirtyRects(
        SceneLayoutCommit commit,
        int viewportWidth,
        int viewportHeight,
        SceneDamageRectBufferWriter dirtyRects)
    {
        dirtyRects.Clear();
        if (commit.HostAnimatedShaderRootIds.Length == 0)
            return ReadOnlySpan<SceneDamageRect>.Empty;

        foreach (var id in commit.HostAnimatedShaderRootIds)
            AddDirtyRect(dirtyRects, GetBoxDamageRect(commit, id, viewportWidth, viewportHeight));

        return FinalizeDirtyRects(dirtyRects, viewportWidth, viewportHeight);
    }

    private static ReadOnlySpan<SceneDamageRect> MergeDirtyRects(
        ReadOnlySpan<SceneDamageRect> first,
        ReadOnlySpan<SceneDamageRect> second,
        int viewportWidth,
        int viewportHeight,
        SceneDamageRectBufferWriter merged)
    {
        if (first.IsEmpty)
            return second;

        if (second.IsEmpty)
            return first;

        foreach (var rect in second)
            merged.Add(rect);
        return FinalizeDirtyRects(merged, viewportWidth, viewportHeight);
    }

    private static ReadOnlySpan<SceneDamageRect> FinalizeDirtyRects(
        SceneDamageRectBufferWriter dirtyRects,
        int viewportWidth,
        int viewportHeight)
    {
        if (dirtyRects.Count == 0)
            return ReadOnlySpan<SceneDamageRect>.Empty;

        var mergedCount = Merge(dirtyRects);
        dirtyRects.Truncate(mergedCount);
        long totalPixels = 0;
        foreach (var rect in dirtyRects.WrittenSpan)
            totalPixels += rect.PixelCount;

        var viewportPixels = (long)viewportWidth * viewportHeight;
        if (mergedCount > MaxRectCountBeforeFullFrame ||
            totalPixels >= viewportPixels * FullFrameAreaThreshold)
        {
            dirtyRects.Clear();
            dirtyRects.Add(new SceneDamageRect(0, 0, viewportWidth, viewportHeight));
        }

        return dirtyRects.WrittenSpan;
    }

    private static SceneDamageRect? NormalizeRect(int x, int y, int width, int height, int viewportWidth, int viewportHeight)
    {
        var left = Math.Clamp(x, 0, viewportWidth);
        var top = Math.Clamp(y, 0, viewportHeight);
        var right = Math.Clamp(x + width, 0, viewportWidth);
        var bottom = Math.Clamp(y + height, 0, viewportHeight);
        if (right <= left || bottom <= top)
            return null;

        return new SceneDamageRect(left, top, right - left, bottom - top);
    }

    private static int Merge(SceneDamageRectBufferWriter rects)
    {
        Array.Sort(rects.Buffer, 0, rects.Count, SceneDamageRectComparer.Instance);
        var buffer = rects.Buffer.AsSpan(0, rects.Count);
        var mergedCount = 0;
        for (var rectIndex = 0; rectIndex < rects.Count; rectIndex++)
        {
            var rect = buffer[rectIndex];
            var mergedAny = false;
            for (var mergedIndex = 0; mergedIndex < mergedCount; mergedIndex++)
            {
                if (!TouchesOrOverlaps(buffer[mergedIndex], rect))
                    continue;

                buffer[mergedIndex] = Union(buffer[mergedIndex], rect);
                mergedAny = true;
                break;
            }

            if (!mergedAny)
                buffer[mergedCount++] = rect;
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < mergedCount; i++)
            {
                for (var j = i + 1; j < mergedCount; j++)
                {
                    if (!TouchesOrOverlaps(buffer[i], buffer[j]))
                        continue;

                    buffer[i] = Union(buffer[i], buffer[j]);
                    for (var moveIndex = j + 1; moveIndex < mergedCount; moveIndex++)
                        buffer[moveIndex - 1] = buffer[moveIndex];
                    mergedCount--;
                    changed = true;
                    j--;
                }
            }
        }

        return mergedCount;
    }

    private static bool TouchesOrOverlaps(SceneDamageRect a, SceneDamageRect b)
    {
        return a.X <= b.X + b.Width &&
               a.X + a.Width >= b.X &&
               a.Y <= b.Y + b.Height &&
               a.Y + a.Height >= b.Y;
    }

    private static SceneDamageRect Union(SceneDamageRect a, SceneDamageRect b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new SceneDamageRect(left, top, right - left, bottom - top);
    }

    private sealed class SceneDamageRectComparer : IComparer<SceneDamageRect>
    {
        public static readonly SceneDamageRectComparer Instance = new();

        public int Compare(SceneDamageRect x, SceneDamageRect y)
        {
            var byY = x.Y.CompareTo(y.Y);
            return byY != 0 ? byY : x.X.CompareTo(y.X);
        }
    }

    private readonly record struct ScreenRect(float Left, float Top, float Right, float Bottom)
    {
        public static ScreenRect? Intersect(ScreenRect a, ScreenRect b)
        {
            var left = Math.Max(a.Left, b.Left);
            var top = Math.Max(a.Top, b.Top);
            var right = Math.Min(a.Right, b.Right);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            if (right <= left || bottom <= top)
                return null;

            return new ScreenRect(left, top, right, bottom);
        }
    }
}

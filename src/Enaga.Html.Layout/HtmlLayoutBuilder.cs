using System.Globalization;
using System.Runtime.InteropServices;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlLayoutBuilder
{
    private readonly HtmlSceneNodeId rootId = HtmlSceneNodeId.Root;
    private readonly IRuntimeTextServices textServices;
    private readonly HtmlPipelineMetrics metrics;
    private readonly LayoutCalculator layoutCalculator;
    private readonly LayoutScratchArena scratch = new();
    private readonly HtmlLayoutMeasurementCache measurementCache;
    private readonly HtmlSceneTextStyleCache textStyleCache = new();
    private readonly SceneNodeIdentityMap<HtmlSceneNodeId> sceneNodeIds;
    private readonly List<HtmlPlacedNode> placedNodes = new();
    private readonly List<HtmlChildRelation> childRelations = new();
    private SceneLayoutCommit? previousCommit;

    public HtmlLayoutBuilder(string rootId, IRuntimeTextServices textServices, HtmlPipelineMetrics metrics, SceneNodeIdAllocator sceneNodeIdAllocator)
    {
        this.textServices = textServices;
        this.metrics = metrics;
        sceneNodeIds = new SceneNodeIdentityMap<HtmlSceneNodeId>(this.rootId, sceneNodeIdAllocator);
        measurementCache = new HtmlLayoutMeasurementCache(metrics);
        layoutCalculator = new LayoutCalculator(textServices);
    }

    public HtmlFragmentTree? LastFragmentTree { get; private set; }

    public IReadOnlyDictionary<SceneNodeId, Enaga.Html.Dom.HtmlNodeId> LastSceneNodeDomIds { get; private set; } =
        new Dictionary<SceneNodeId, Enaga.Html.Dom.HtmlNodeId>();

    public SceneLayoutCommit Build(HtmlStyledSceneTree styledTree, HtmlLayoutOutputStore layoutOutputStore, int width, int height, float viewportScale)
    {
        var resolvedViewportScale = Math.Max(0.001f, viewportScale);
        measurementCache.BeginLayoutPass(layoutOutputStore.Outputs);
        placedNodes.Clear();
        childRelations.Clear();
        var rootStyle = styledTree.RootStyle
            .CloneWithResolvedViewportUnits(width, height)
            .CloneWithResolvedContainerPercentUnits(width);
        var rootChildren = ResolveViewportUnits(styledTree.RootChildren, width, height);
        var bodyLayoutWidth = ResolveRootLayoutWidth(rootStyle, width);
        var rootKind = rootStyle.IsScrollContainer ? SceneNodeKind.ScrollView : SceneNodeKind.View;

        LayoutChildren(rootId, rootStyle, rootChildren, 0, 0, bodyLayoutWidth, height, resolvedViewportScale);
        LastSceneNodeDomIds = CreateSceneNodeDomIdMap(styledTree);
        var commit = new HtmlSceneEmitter(rootId, sceneNodeIds, textStyleCache, width, height, width, height, resolvedViewportScale, previousCommit).Emit(
            rootKind,
            rootStyle,
            placedNodes,
            childRelations,
            metrics);
        previousCommit = commit;
        LastFragmentTree = HtmlFragmentTreeFactory.Create(rootId, sceneNodeIds, width, height, placedNodes, commit.Layout);
        metrics.AddFragmentsRebuilt(LastFragmentTree.Fragments.Count + childRelations.Count);
        return commit;
    }

    private Dictionary<SceneNodeId, Enaga.Html.Dom.HtmlNodeId> CreateSceneNodeDomIdMap(HtmlStyledSceneTree styledTree)
    {
        var map = new Dictionary<SceneNodeId, Enaga.Html.Dom.HtmlNodeId>(placedNodes.Count + 1)
        {
            [sceneNodeIds.GetOrCreate(rootId)] = styledTree.RootDomNodeId
        };

        foreach (var placed in placedNodes)
        {
            if (placed.Node.DomNodeId.IsValid)
                map[sceneNodeIds.GetOrCreate(placed.Id)] = placed.Node.DomNodeId;
        }

        return map;
    }

    private static float ResolveRootLayoutWidth(HtmlComputedStyle rootStyle, float viewportWidth)
    {
        var width = viewportWidth;
        if (LayoutValue.IsSet(rootStyle.Width))
            width = LayoutValue.Resolve(rootStyle.Width, rootStyle.IsWidthPercent, viewportWidth);
        if (LayoutValue.IsSet(rootStyle.MinWidth))
            width = Math.Max(width, LayoutValue.Resolve(rootStyle.MinWidth, rootStyle.IsMinWidthPercent, viewportWidth));
        if (LayoutValue.IsSet(rootStyle.MaxWidth))
            width = Math.Min(width, LayoutValue.Resolve(rootStyle.MaxWidth, rootStyle.IsMaxWidthPercent, viewportWidth));
        return Math.Max(0, width);
    }

    private static bool IsTableRowNode(HtmlSceneNode node)
        => node.Role == HtmlSceneNodeRole.TableRow;

    private static bool IsTableCellNode(HtmlSceneNode node)
        => node.Role == HtmlSceneNodeRole.TableCell;

    private static bool IsCssBlockifiedTableCell(HtmlSceneNode node)
        => IsTableCellNode(node) &&
           node.Style is { HasExplicitDisplay: true, Display: HtmlDisplay.Block };

    private static bool IsListItemNode(HtmlSceneNode node)
        => node.Role == HtmlSceneNodeRole.ListItem;

    private void LayoutChildren(
        HtmlSceneNodeId parentId,
        HtmlComputedStyle parentStyle,
        IReadOnlyList<HtmlSceneNode> children,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        float viewportScale)
    {
        if (children.Count == 0)
        {
            AddChildRelation(parentId, []);
            return;
        }

        var reservedScrollBarGutter = LayoutBoxEdges.Zero;
        var parentContentWidth = Math.Max(0, parentWidth - parentStyle.PaddingLeft - parentStyle.PaddingRight);
        var parentContentHeight = Math.Max(0, parentHeight - parentStyle.PaddingTop - parentStyle.PaddingBottom);
        var resolvedChildren = ResolveContainerPercentUnits(children, parentContentWidth);
        if (IsTableRowCollection(resolvedChildren))
        {
            LayoutTableRows(parentId, parentStyle, resolvedChildren, parentLeft, parentTop, parentWidth, parentHeight, viewportScale);
            return;
        }

        if (TryLayoutFloatChildren(parentId, parentStyle, resolvedChildren, parentLeft, parentTop, parentWidth, parentHeight, viewportScale))
            return;

        if (TryLayoutInlineFormattingContext(parentId, parentStyle, resolvedChildren, parentLeft, parentTop, parentWidth, parentHeight, viewportScale))
            return;

        var scratchMark = scratch.Mark();
        try
        {
            var frames = PrepareLayoutFrames(
                parentStyle,
                children,
                parentWidth,
                parentHeight,
                reservedScrollBarGutter,
                out resolvedChildren,
                out var childRequests,
                out parentContentWidth,
                out parentContentHeight);

            for (var pass = 0; pass < 3; pass++)
            {
                var scrollBarWidth = ResolveLayoutScrollBarWidth(parentStyle, viewportScale);
                var nextReservedScrollBarGutter = new LayoutBoxEdges(
                    0,
                    0,
                    ShouldReserveVerticalScrollBar(parentStyle, childRequests, frames, parentHeight) ? scrollBarWidth : 0,
                    ShouldReserveHorizontalScrollBar(parentStyle, childRequests, frames, parentWidth) ? scrollBarWidth : 0);
                if (SameReservedGutter(reservedScrollBarGutter, nextReservedScrollBarGutter))
                    break;

                reservedScrollBarGutter = nextReservedScrollBarGutter;
                frames = PrepareLayoutFrames(
                    parentStyle,
                    children,
                    parentWidth,
                    parentHeight,
                    reservedScrollBarGutter,
                    out resolvedChildren,
                    out childRequests,
                    out parentContentWidth,
                    out parentContentHeight);
            }

            var childIds = new HtmlSceneNodeId[resolvedChildren.Count];
            for (var index = 0; index < resolvedChildren.Count; index++)
            {
                var child = resolvedChildren[index];
                var frame = frames[index] ?? new LayoutFrameData(parentStyle.PaddingLeft, parentStyle.PaddingTop, 0, 0);
                var absLeft = parentLeft + frame.Left;
                var absTop = parentTop + frame.Top;
                AddPlacedNode(child, parentId, absLeft, absTop, frame.Width, frame.Height);
                childIds[index] = child.Id;

                if (child.Children.Count > 0)
                    LayoutChildren(child.Id, child.Style, child.Children, absLeft, absTop, frame.Width, frame.Height, viewportScale);
                else
                    AddChildRelation(child.Id, []);
            }

            AddChildRelation(parentId, childIds);
        }
        finally
        {
            scratch.Rewind(scratchMark);
        }
    }

    private static bool IsFlexContainer(HtmlComputedStyle style)
        => style.Display == HtmlDisplay.Flex;

    private bool TryLayoutFloatChildren(
        HtmlSceneNodeId parentId,
        HtmlComputedStyle parentStyle,
        IReadOnlyList<HtmlSceneNode> children,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        float viewportScale)
    {
        if (parentStyle.Display != HtmlDisplay.Block || children.Count == 0)
            return false;

        var hasFloat = false;
        for (var index = 0; index < children.Count; index++)
            hasFloat |= children[index].Style.Float != HtmlFloat.None;
        if (!hasFloat)
            return false;

        var contentWidth = Math.Max(0, parentWidth - parentStyle.PaddingLeft - parentStyle.PaddingRight);
        var contentHeight = Math.Max(0, parentHeight - parentStyle.PaddingTop - parentStyle.PaddingBottom);
        var scratchMark = scratch.Mark();
        try
        {
            var requests = CreateFloatMeasureRequests(children, contentWidth, contentHeight);
            var childIds = new HtmlSceneNodeId[children.Count];
            var placements = MeasureFloatContent(parentStyle, children, requests, parentWidth, wrapLines: true).Placements;
            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                ref readonly var request = ref requests[index];
                ref readonly var placement = ref placements[index];
                var width = LayoutValue.IsSet(request.Width) ? request.Width : 0;
                var height = LayoutValue.IsSet(request.Height) ? request.Height : 0;
                var absLeft = parentLeft + placement.Left;
                var absTop = parentTop + placement.Top;
                AddPlacedNode(child, parentId, absLeft, absTop, width, height);
                childIds[index] = child.Id;

                if (child.Children.Count > 0)
                    LayoutChildren(child.Id, child.Style, child.Children, absLeft, absTop, width, height, viewportScale);
                else
                    AddChildRelation(child.Id, []);
            }

            AddChildRelation(parentId, childIds);
        }
        finally
        {
            scratch.Rewind(scratchMark);
        }
        return true;
    }

    private Span<LayoutChildRequest> CreateFloatMeasureRequests(IReadOnlyList<HtmlSceneNode> children, float contentWidth, float contentHeight)
    {
        var requests = scratch.AllocateRequests(children.Count);
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var request = CreateLayoutRequest(child, contentWidth, contentHeight, parentIsFlexContainer: false, FlexDirection.Column, allowFlexShrink: false);
            if (child.Style.Float != HtmlFloat.None)
            {
                var hasExplicitWidth = child.Style.HasExplicitWidth;
                if (LayoutValue.IsSet(request.Width) &&
                    (request.Units & LayoutValueUnitFlags.WidthPercent) != 0)
                {
                    request = WithWidth(request, LayoutValue.Resolve(request.Width, isPercent: true, contentWidth));
                }

                if (!hasExplicitWidth || !LayoutValue.IsSet(request.Width))
                {
                    var preferredWidth = MeasureFloatAutoWidth(child, contentWidth, contentHeight);
                    if (preferredWidth > 0)
                    {
                        var width = Math.Min(preferredWidth, contentWidth);
                        if (!LayoutValue.IsSet(request.Width) || MathF.Abs(width - request.Width) > 0.5f)
                            request = WithWidth(request, width);
                    }
                }
            }

            requests[index] = request;
        }

        return requests;
    }

    private static bool ContainsFloatChildren(IReadOnlyList<HtmlSceneNode> children)
    {
        for (var index = 0; index < children.Count; index++)
        {
            if (children[index].Style.Float != HtmlFloat.None)
                return true;
        }

        return false;
    }

    private FloatContentMeasure MeasureFloatContent(
        HtmlComputedStyle parentStyle,
        IReadOnlyList<HtmlSceneNode> children,
        ReadOnlySpan<LayoutChildRequest> requests,
        float parentWidth,
        bool wrapLines)
    {
        var placements = scratch.AllocateFloatPlacements(children.Count);
        var leftCursor = parentStyle.PaddingLeft;
        var rightCursor = parentWidth - parentStyle.PaddingRight;
        var currentY = parentStyle.PaddingTop;
        var lineHeight = 0f;
        var contentRight = 0f;
        var contentLeft = parentStyle.PaddingLeft;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            ref readonly var request = ref requests[index];
            var width = LayoutValue.IsSet(request.Width) ? request.Width : 0;
            var height = LayoutValue.IsSet(request.Height) ? request.Height : 0;
            var outerWidth = request.MarginLeft + width + request.MarginRight;
            var outerHeight = request.MarginTop + height + request.MarginBottom;
            float localLeft;
            float localTop;

            if (child.Style.Float == HtmlFloat.None)
            {
                if (lineHeight > 0)
                {
                    currentY += lineHeight;
                    leftCursor = parentStyle.PaddingLeft;
                    rightCursor = parentWidth - parentStyle.PaddingRight;
                    lineHeight = 0;
                }

                localLeft = parentStyle.PaddingLeft + request.MarginLeft;
                localTop = currentY + request.MarginTop;
                currentY += outerHeight;
            }
            else
            {
                if (ShouldClearFloatLine(child.Style.Clear, child.Style.Float) && lineHeight > 0)
                {
                    currentY += lineHeight;
                    leftCursor = parentStyle.PaddingLeft;
                    rightCursor = parentWidth - parentStyle.PaddingRight;
                    lineHeight = 0;
                }

                if (wrapLines && outerWidth > rightCursor - leftCursor && lineHeight > 0)
                {
                    currentY += lineHeight;
                    leftCursor = parentStyle.PaddingLeft;
                    rightCursor = parentWidth - parentStyle.PaddingRight;
                    lineHeight = 0;
                }

                if (child.Style.Float == HtmlFloat.Right)
                {
                    rightCursor -= request.MarginRight + width;
                    localLeft = rightCursor;
                    rightCursor -= request.MarginLeft;
                }
                else
                {
                    localLeft = leftCursor + request.MarginLeft;
                    leftCursor += outerWidth;
                }

                localTop = currentY + request.MarginTop;
                lineHeight = Math.Max(lineHeight, height + request.MarginBottom);
            }

            placements[index] = new FloatPlacement(localLeft, localTop);
            contentRight = Math.Max(contentRight, localLeft + width + request.MarginRight);
            contentLeft = Math.Min(contentLeft, localLeft - request.MarginLeft);
        }

        return new FloatContentMeasure(
            contentRight - Math.Min(contentLeft, parentStyle.PaddingLeft),
            currentY + lineHeight,
            placements);
    }

    private static bool ShouldClearFloatLine(HtmlClear clear, HtmlFloat floatSide)
        => clear == HtmlClear.Both ||
           clear == HtmlClear.Left && floatSide == HtmlFloat.Left ||
           clear == HtmlClear.Right && floatSide == HtmlFloat.Right;

    private readonly record struct FloatPlacement(float Left, float Top);

    private readonly ref struct FloatContentMeasure(float Width, float Height, ReadOnlySpan<FloatPlacement> Placements)
    {
        public float Width { get; } = Width;
        public float Height { get; } = Height;
        public ReadOnlySpan<FloatPlacement> Placements { get; } = Placements;
    }

    private void LayoutTableRows(
        HtmlSceneNodeId parentId,
        HtmlComputedStyle parentStyle,
        IReadOnlyList<HtmlSceneNode> rows,
        float parentLeft,
        float parentTop,
        float parentWidth,
        float parentHeight,
        float viewportScale)
    {
        var contentWidth = Math.Max(0, parentWidth - parentStyle.PaddingLeft - parentStyle.PaddingRight);
        var contentHeight = Math.Max(0, parentHeight - parentStyle.PaddingTop - parentStyle.PaddingBottom);
        var tableSpacing = ResolveTableSpacing(parentStyle, rows);
        var grid = CreateTableGridLayout(rows, contentWidth, contentHeight, tableSpacing);
        var rowTops = new float[grid.RowHeights.Length];
        var columnLefts = new float[grid.ColumnWidths.Length];
        for (var index = 1; index < rowTops.Length; index++)
            rowTops[index] = rowTops[index - 1] + grid.RowHeights[index - 1] + grid.RowGap;
        for (var index = 1; index < columnLefts.Length; index++)
            columnLefts[index] = columnLefts[index - 1] + grid.ColumnWidths[index - 1] + grid.ColumnGap;

        var scratchMark = scratch.Mark();
        try
        {
            var rowChildren = scratch.AllocateRowChildBuffers(rows.Count);
            var cells = grid.CellSpan;
            for (var placementIndex = 0; placementIndex < cells.Length; placementIndex++)
            {
                ref readonly var placement = ref cells[placementIndex];
                var cell = placement.Cell;
                var cellLeft = parentLeft + parentStyle.PaddingLeft + columnLefts[placement.ColumnIndex];
                var cellTop = parentTop + parentStyle.PaddingTop + rowTops[placement.RowIndex];
                var cellWidth = SumWithGap(grid.ColumnWidths, placement.ColumnIndex, placement.ColSpan, grid.ColumnGap);
                var cellHeight = SumWithGap(grid.RowHeights, placement.RowIndex, placement.RowSpan, grid.RowGap);
                AddPlacedNode(cell, rows[placement.ParentRowIndex].Id, cellLeft, cellTop, cellWidth, cellHeight);
                rowChildren[placement.ParentRowIndex].Add(cell.Id);

                if (cell.Children.Count > 0)
                    LayoutChildren(cell.Id, cell.Style, cell.Children, cellLeft, cellTop, cellWidth, cellHeight, viewportScale);
                else
                    AddChildRelation(cell.Id, []);
            }

            var rowIds = new HtmlSceneNodeId[rows.Count];
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                rowIds[rowIndex] = row.Id;
                var rowTop = parentTop + parentStyle.PaddingTop + GetParentRowTop(grid, rowIndex);
                var rowHeight = GetParentRowHeight(grid, rowIndex);
                AddPlacedNode(row, parentId, parentLeft + parentStyle.PaddingLeft, rowTop, grid.Width, rowHeight);
                AddChildRelation(row.Id, [.. rowChildren[rowIndex]]);
            }

            AddChildRelation(parentId, rowIds);
        }
        finally
        {
            scratch.Rewind(scratchMark);
        }
    }

    private void AddPlacedNode(
        HtmlSceneNode node,
        HtmlSceneNodeId? parentId,
        float absLeft,
        float absTop,
        float width,
        float height,
        int fragmentIndex = -1,
        string? textContentOverride = null)
        => placedNodes.Add(new HtmlPlacedNode(node, parentId, absLeft, absTop, width, height, fragmentIndex, textContentOverride));

    private void AddChildRelation(HtmlSceneNodeId parentId, HtmlSceneNodeId[] childIds)
        => childRelations.Add(new HtmlChildRelation(parentId, childIds));

    private static float ResolveTableSpacing(HtmlComputedStyle parentStyle, IReadOnlyList<HtmlSceneNode> rows)
    {
        if (parentStyle.TableBorderCollapse)
            return 0;

        for (var index = 0; index < rows.Count; index++)
        {
            if (ContainsCssBlockifiedTableCell(rows[index]))
                return 0;
        }

        return LayoutValue.IsSet(parentStyle.TableBorderSpacing)
            ? Math.Max(0, parentStyle.TableBorderSpacing)
            : Math.Max(0, parentStyle.Gap);
    }

    private TableGridLayout CreateTableGridLayout(IReadOnlyList<HtmlSceneNode> rows, float availableWidth, float availableHeight, float tableSpacing)
    {
        var columnGap = tableSpacing;
        var rowGap = tableSpacing;
        var occupied = new HashSet<(int Row, int Column)>();
        var placements = new List<TableCellPlacement>();
        var columnCount = 0;
        var physicalRowIndex = 0;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (ContainsCssBlockifiedTableCell(rows[rowIndex]))
            {
                foreach (var cell in rows[rowIndex].Children)
                {
                    if (!IsTableCellNode(cell))
                        continue;

                    var preferred = MeasureTableCell(cell, availableWidth, availableHeight);
                    placements.Add(new TableCellPlacement(cell, physicalRowIndex, rowIndex, 0, 1, 1, preferred.MinimumWidth, preferred.Width));
                    physicalRowIndex++;
                    columnCount = Math.Max(columnCount, 1);
                }

                continue;
            }

            var columnIndex = 0;
            foreach (var cell in rows[rowIndex].Children)
            {
                if (!IsTableCellNode(cell))
                    continue;

                while (occupied.Contains((physicalRowIndex, columnIndex)))
                    columnIndex++;

                var rowSpan = Math.Max(1, cell.RowSpan);
                var colSpan = Math.Max(1, cell.ColSpan);
                var preferred = MeasureTableCell(cell, availableWidth, availableHeight);
                placements.Add(new TableCellPlacement(cell, physicalRowIndex, rowIndex, columnIndex, rowSpan, colSpan, preferred.MinimumWidth, preferred.Width));

                for (var row = physicalRowIndex; row < physicalRowIndex + rowSpan; row++)
                {
                    for (var column = columnIndex; column < columnIndex + colSpan; column++)
                        occupied.Add((row, column));
                }

                columnIndex += colSpan;
                columnCount = Math.Max(columnCount, columnIndex);
            }

            physicalRowIndex++;
        }

        var columnWidths = new float[Math.Max(1, columnCount)];
        var minimumColumnWidths = new float[Math.Max(1, columnCount)];
        var rowHeights = new float[Math.Max(1, physicalRowIndex)];
        var placementSpan = CollectionsMarshal.AsSpan(placements);
        for (var placementIndex = 0; placementIndex < placementSpan.Length; placementIndex++)
        {
            ref readonly var placement = ref placementSpan[placementIndex];
            DistributeMinimum(minimumColumnWidths, placement.ColumnIndex, placement.ColSpan, placement.MinimumWidth);
            DistributeMinimum(columnWidths, placement.ColumnIndex, placement.ColSpan, placement.PreferredWidth);
        }

        ShrinkColumnsToAvailableWidth(columnWidths, minimumColumnWidths, availableWidth);
        ExpandColumnsToAvailableWidth(columnWidths, minimumColumnWidths, availableWidth);

        for (var placementIndex = 0; placementIndex < placementSpan.Length; placementIndex++)
        {
            ref readonly var placement = ref placementSpan[placementIndex];
            if (placement.RowSpan != 1)
                continue;

            var cellWidth = SumWithGap(columnWidths, placement.ColumnIndex, placement.ColSpan, columnGap);
            var height = MeasureTableCellHeight(placement.Cell, cellWidth, availableHeight);
            rowHeights[placement.RowIndex] = Math.Max(rowHeights[placement.RowIndex], height);
        }

        for (var placementIndex = 0; placementIndex < placementSpan.Length; placementIndex++)
        {
            ref readonly var placement = ref placementSpan[placementIndex];
            if (placement.RowSpan <= 1)
                continue;

            var cellWidth = SumWithGap(columnWidths, placement.ColumnIndex, placement.ColSpan, columnGap);
            var height = MeasureTableCellHeight(placement.Cell, cellWidth, availableHeight);
            DistributeRowSpanMinimum(rowHeights, placement.RowIndex, placement.RowSpan, height);
        }

        return new TableGridLayout(placements, columnWidths, rowHeights, columnGap, rowGap);
    }

    private static bool ContainsCssBlockifiedTableCell(HtmlSceneNode row)
    {
        for (var index = 0; index < row.Children.Count; index++)
        {
            if (IsCssBlockifiedTableCell(row.Children[index]))
                return true;
        }

        return false;
    }

    private static float GetParentRowTop(TableGridLayout grid, int parentRowIndex)
    {
        var top = 0f;
        var hasPlacement = false;
        var cells = grid.CellSpan;
        for (var index = 0; index < cells.Length; index++)
        {
            ref readonly var placement = ref cells[index];
            if (placement.ParentRowIndex != parentRowIndex)
                continue;

            var placementTop = GetPhysicalRowTop(grid, placement.RowIndex);
            top = hasPlacement ? Math.Min(top, placementTop) : placementTop;
            hasPlacement = true;
        }

        return hasPlacement ? top : 0f;
    }

    private static float GetParentRowHeight(TableGridLayout grid, int parentRowIndex)
    {
        var top = 0f;
        var bottom = 0f;
        var hasPlacement = false;
        var cells = grid.CellSpan;
        for (var index = 0; index < cells.Length; index++)
        {
            ref readonly var placement = ref cells[index];
            if (placement.ParentRowIndex != parentRowIndex)
                continue;

            var placementTop = GetPhysicalRowTop(grid, placement.RowIndex);
            var placementBottom = placementTop + SumWithGap(grid.RowHeights, placement.RowIndex, placement.RowSpan, grid.RowGap);
            if (!hasPlacement)
            {
                top = placementTop;
                bottom = placementBottom;
                hasPlacement = true;
                continue;
            }

            top = Math.Min(top, placementTop);
            bottom = Math.Max(bottom, placementBottom);
        }

        return hasPlacement ? Math.Max(0, bottom - top) : 0f;
    }

    private static float GetPhysicalRowTop(TableGridLayout grid, int rowIndex)
    {
        var top = 0f;
        var safeRowIndex = Math.Clamp(rowIndex, 0, grid.RowHeights.Length);
        for (var index = 0; index < safeRowIndex; index++)
        {
            if (index > 0)
                top += grid.RowGap;
            top += grid.RowHeights[index];
        }

        return top;
    }

    private (float MinimumWidth, float Width) MeasureTableCell(HtmlSceneNode cell, float availableWidth, float availableHeight)
    {
        if (LayoutValue.IsSet(cell.Style.Width))
        {
            var specifiedWidth = ResolveExplicitOuterSize(cell.Style, cell.Style.Width, cell.Style.IsWidthPercent, availableWidth, horizontal: true);
            specifiedWidth = float.IsFinite(specifiedWidth) ? Math.Max(0, specifiedWidth) : 0;
            return (specifiedWidth, specifiedWidth);
        }

        var intrinsic = cell.Children.Count > 0
            ? MeasureNodeIntrinsicSize(cell, availableWidth, availableHeight, parentIsFlexContainer: true, parentFlexDirection: FlexDirection.Row)
            : (Width: 0f, Height: 0f);
        var minContentWidth = cell.Children.Count > 0
            ? Math.Min(MeasureMinContentWidth(cell, availableWidth, availableHeight), Math.Max(0, availableWidth))
            : 0f;
        var maxContentWidth = cell.Children.Count > 0
            ? Math.Min(MeasureMaxContentWidth(cell, availableWidth, availableHeight), Math.Max(0, availableWidth))
            : 0f;
        var explicitContentWidth = MeasureExplicitTableContentWidth(cell);
        var minimumWidth = Math.Max(explicitContentWidth, minContentWidth);
        var width = explicitContentWidth > 0
            ? explicitContentWidth
            : Math.Max(Math.Min(intrinsic.Width, Math.Max(0, availableWidth)), maxContentWidth);
        width = Math.Max(0, width);
        return (minimumWidth, width);
    }

    private float MeasureTableCellHeight(HtmlSceneNode cell, float availableWidth, float availableHeight)
    {
        var layoutHeight = cell.Children.Count > 0
            ? MeasureNodeLayoutHeightUncached(cell, availableWidth, availableHeight, parentIsFlexContainer: true, parentFlexDirection: FlexDirection.Row)
            : 0f;
        var hasExplicitHeight = LayoutValue.IsSet(cell.Style.Height);
        var height = hasExplicitHeight ? cell.Style.Height : layoutHeight;
        if (!hasExplicitHeight &&
            TryMeasureCompactFloatedTableCellHeight(cell, availableWidth, availableHeight, out var compactHeight))
        {
            height = Math.Min(height, compactHeight);
        }
        else if (TryMeasureTableCellContentHeightIgnoringMargins(cell, availableWidth, availableHeight, out var contentHeight))
        {
            height = hasExplicitHeight || contentHeight >= height || HasDirectWrappingInlineRun(cell)
                ? Math.Max(height, contentHeight)
                : contentHeight;
        }

        return Math.Max(0, height);
    }

    private static bool HasDirectWrappingInlineRun(HtmlSceneNode cell)
    {
        for (var index = 0; index < cell.Children.Count; index++)
        {
            var childStyle = cell.Children[index].Style;
            if (childStyle.Display == HtmlDisplay.Flex &&
                childStyle.FlexDirection == FlexDirection.Row &&
                childStyle.FlexWrap == FlexWrap.Wrap)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInlineListItem(HtmlSceneNode node)
    {
        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            if (IsListItemNode(child) && child.Style.Display == HtmlDisplay.Inline)
                return true;
            if (ContainsInlineListItem(child))
                return true;
        }

        return false;
    }

    private static bool ContainsFloatDescendant(HtmlSceneNode node)
    {
        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            if (child.Style.Float != HtmlFloat.None || ContainsFloatDescendant(child))
                return true;
        }

        return false;
    }

    private static bool ContainsNonBreakingText(HtmlSceneNode node)
    {
        if (node.TextContent?.IndexOf('\u00a0', StringComparison.Ordinal) >= 0)
            return true;

        for (var index = 0; index < node.Children.Count; index++)
        {
            if (ContainsNonBreakingText(node.Children[index]))
                return true;
        }

        return false;
    }

    private bool TryMeasureTableCellContentHeightIgnoringMargins(HtmlSceneNode cell, float availableWidth, float availableHeight, out float height)
    {
        height = 0;
        if (cell.Children.Count == 0)
            return false;

        var contentHeight = 0f;
        for (var index = 0; index < cell.Children.Count; index++)
        {
            var child = cell.Children[index];
            var measured = MeasureShrinkToFitSize(child, availableWidth, availableHeight);
            if (measured.Height <= 0)
                return false;

            contentHeight += measured.Height;
        }

        height = cell.Style.PaddingTop + contentHeight + cell.Style.PaddingBottom;
        return true;
    }

    private bool TryMeasureCompactFloatedTableCellHeight(HtmlSceneNode cell, float availableWidth, float availableHeight, out float height)
    {
        height = 0;
        if (cell.Children.Count != 1)
            return false;

        var container = cell.Children[0];
        if (container.Children.Count == 0 || !ContainsFloatChildren(container.Children))
            return false;

        var maxChildHeight = 0f;
        for (var index = 0; index < container.Children.Count; index++)
        {
            var floated = container.Children[index];
            if (floated.Style.Float == HtmlFloat.None)
                return false;

            var contentHeight = 0f;
            if (floated.Children.Count > 0)
            {
                for (var childIndex = 0; childIndex < floated.Children.Count; childIndex++)
                    contentHeight = Math.Max(contentHeight, MeasureShrinkToFitSize(floated.Children[childIndex], availableWidth, availableHeight).Height);
            }
            else
            {
                contentHeight = MeasureShrinkToFitSize(floated, availableWidth, availableHeight).Height;
            }

            maxChildHeight = Math.Max(maxChildHeight, contentHeight + floated.Style.PaddingTop + floated.Style.PaddingBottom + floated.Style.BorderWidth * 2);
        }

        if (maxChildHeight <= 0)
            return false;

        height =
            cell.Style.PaddingTop +
            container.Style.PaddingTop +
            maxChildHeight +
            container.Style.PaddingBottom +
            cell.Style.PaddingBottom;
        return true;
    }

    private static float MeasureExplicitTableContentWidth(HtmlSceneNode node)
    {
        var width = 0f;
        foreach (var child in node.Children)
        {
            if (LayoutValue.IsSet(child.Style.Width) && !child.Style.IsWidthPercent)
                width = Math.Max(width, child.Style.Width + child.Style.MarginLeft + child.Style.MarginRight);
        }

        return width;
    }

    private static void DistributeMinimum(float[] values, int start, int span, float minimum)
    {
        var safeStart = Math.Clamp(start, 0, values.Length);
        var safeEnd = Math.Clamp(start + Math.Max(1, span), safeStart, values.Length);
        if (safeStart >= safeEnd)
            return;

        var current = Sum(values, safeStart, safeEnd - safeStart);
        var deficit = minimum - current;
        if (deficit <= 0)
            return;

        var add = deficit / (safeEnd - safeStart);
        for (var index = safeStart; index < safeEnd; index++)
            values[index] += add;
    }

    private static void DistributeRowSpanMinimum(float[] rowHeights, int start, int span, float minimum)
    {
        var safeStart = Math.Clamp(start, 0, rowHeights.Length);
        var safeEnd = Math.Clamp(start + Math.Max(1, span), safeStart, rowHeights.Length);
        if (safeStart >= safeEnd)
            return;

        var current = Sum(rowHeights, safeStart, safeEnd - safeStart);
        var deficit = minimum - current;
        if (deficit <= 0)
            return;

        rowHeights[safeEnd - 1] += deficit;
    }

    private static void ShrinkColumnsToAvailableWidth(float[] widths, float[] minimumWidths, float availableWidth)
    {
        if (availableWidth <= 0)
            return;

        var total = Sum(widths, 0, widths.Length);
        var overflow = total - availableWidth;
        if (overflow <= 0)
            return;

        while (overflow > 0.5f)
        {
            var shrinkable = 0f;
            for (var index = 0; index < widths.Length; index++)
                shrinkable += Math.Max(0, widths[index] - minimumWidths[index]);

            if (shrinkable <= 0)
                break;

            var consumed = 0f;
            for (var index = 0; index < widths.Length; index++)
            {
                var capacity = Math.Max(0, widths[index] - minimumWidths[index]);
                if (capacity <= 0)
                    continue;

                var shrink = Math.Min(capacity, overflow * (capacity / shrinkable));
                widths[index] -= shrink;
                consumed += shrink;
            }

            if (consumed <= 0.5f)
                break;

            overflow -= consumed;
        }
    }

    private static float ResolveLayoutScrollBarWidth(HtmlComputedStyle style, float viewportScale)
        => Math.Max(0, style.ScrollbarWidth) / Math.Max(0.001f, viewportScale);

    private Span<LayoutFrameData?> PrepareLayoutFrames(
        HtmlComputedStyle parentStyle,
        IReadOnlyList<HtmlSceneNode> children,
        float parentWidth,
        float parentHeight,
        LayoutBoxEdges reservedScrollBarGutter,
        out IReadOnlyList<HtmlSceneNode> resolvedChildren,
        out Span<LayoutChildRequest> childRequests,
        out float parentContentWidth,
        out float parentContentHeight)
    {
        var containerStyle = CreateLayoutContainerStyle(parentStyle, reservedScrollBarGutter);
        parentContentWidth = containerStyle.ResolveContentWidth(parentWidth);
        parentContentHeight = containerStyle.ResolveContentHeight(parentHeight);
        resolvedChildren = ResolveContainerPercentUnits(children, parentContentWidth);

        childRequests = scratch.AllocateRequests(resolvedChildren.Count);
        var parentIsFlexContainer = IsFlexContainer(parentStyle);
        var allowChildFlexShrink = ShouldAllowChildFlexShrink(parentStyle);
        for (var index = 0; index < resolvedChildren.Count; index++)
            childRequests[index] = CreateLayoutRequest(resolvedChildren[index], parentContentWidth, parentContentHeight, parentIsFlexContainer, parentStyle.FlexDirection, allowChildFlexShrink);

        childRequests = ApplyBlockMarginCollapse(parentStyle, childRequests);

        if (FlexLayout.ResolveAxis(parentStyle.FlexDirection) == LayoutAxis.Row)
            ResolveRowAutoHeights(resolvedChildren, childRequests, parentContentWidth, parentContentHeight, parentStyle.Gap, parentStyle.AlignItems);

        var frames = CalculateFrames(containerStyle, childRequests, parentWidth, parentHeight);
        if (TryResolveAutoHeightRequests(resolvedChildren, childRequests, frames, parentContentHeight, parentStyle.FlexDirection, parentStyle.AlignItems, out var resolvedRequests))
        {
            childRequests = resolvedRequests;
            frames = CalculateFrames(containerStyle, childRequests, parentWidth, parentHeight);
        }

        return frames;
    }

    private static bool ShouldReserveVerticalScrollBar(
        HtmlComputedStyle parentStyle,
        ReadOnlySpan<LayoutChildRequest> childRequests,
        ReadOnlySpan<LayoutFrameData?> frames,
        float parentHeight)
    {
        if (!parentStyle.IsScrollContainer || parentStyle.ScrollbarWidth <= 0)
            return false;

        var contentBottom = parentStyle.PaddingTop;
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is not { } frame)
                continue;

            var marginBottom = index < childRequests.Length ? childRequests[index].MarginBottom : 0;
            contentBottom = Math.Max(contentBottom, frame.Top + frame.Height + marginBottom);
        }

        contentBottom += parentStyle.PaddingBottom;
        return contentBottom > parentHeight + 0.5f;
    }

    private static bool ShouldReserveHorizontalScrollBar(
        HtmlComputedStyle parentStyle,
        ReadOnlySpan<LayoutChildRequest> childRequests,
        ReadOnlySpan<LayoutFrameData?> frames,
        float parentWidth)
    {
        if (!parentStyle.IsScrollContainer || parentStyle.ScrollbarWidth <= 0)
            return false;

        var contentRight = parentStyle.PaddingLeft;
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is not { } frame)
                continue;

            var marginRight = index < childRequests.Length ? childRequests[index].MarginRight : 0;
            contentRight = Math.Max(contentRight, frame.Left + frame.Width + marginRight);
        }

        contentRight += parentStyle.PaddingRight;
        return contentRight > parentWidth + 0.5f;
    }

    private static bool SameReservedGutter(LayoutBoxEdges left, LayoutBoxEdges right)
        => Math.Abs(left.Left - right.Left) <= 0.001f &&
           Math.Abs(left.Top - right.Top) <= 0.001f &&
           Math.Abs(left.Right - right.Right) <= 0.001f &&
           Math.Abs(left.Bottom - right.Bottom) <= 0.001f;

    private static void ExpandColumnsToAvailableWidth(float[] widths, float[] minimumWidths, float availableWidth)
    {
        if (availableWidth <= 0 || widths.Length == 0)
            return;

        var total = Sum(widths, 0, widths.Length);
        var remaining = availableWidth - total;
        if (remaining <= 0.5f)
            return;

        var expandable = 0f;
        for (var index = 0; index < widths.Length; index++)
            expandable += Math.Max(1, widths[index] - minimumWidths[index]);

        if (expandable <= 0)
        {
            var add = remaining / widths.Length;
            for (var index = 0; index < widths.Length; index++)
                widths[index] += add;
            return;
        }

        for (var index = 0; index < widths.Length; index++)
        {
            var weight = Math.Max(1, widths[index] - minimumWidths[index]);
            widths[index] += remaining * (weight / expandable);
        }
    }

    private static float Sum(float[] values, int start, int count)
    {
        var total = 0f;
        var end = Math.Min(values.Length, start + count);
        for (var index = Math.Max(0, start); index < end; index++)
            total += values[index];

        return total;
    }

    private static float SumWithGap(float[] values, int start, int count, float gap)
    {
        var total = Sum(values, start, count);
        return count > 1 ? total + (count - 1) * gap : total;
    }

    private static bool IsTableRowCollection(IReadOnlyList<HtmlSceneNode> nodes)
    {
        if (nodes.Count == 0)
            return false;

        for (var index = 0; index < nodes.Count; index++)
        {
            if (!IsTableRowNode(nodes[index]))
                return false;
        }

        return true;
    }

}





using System.Globalization;
using Enaga.Layout;
using Enaga.Rendering;
using Enaga.Scene;

namespace Enaga.Html;

internal sealed partial class HtmlLayoutBuilder
{
    private sealed class TableGridLayout(IReadOnlyList<TableCellPlacement> Cells, float[] ColumnWidths, float[] RowHeights, float ColumnGap, float RowGap)
    {
        public IReadOnlyList<TableCellPlacement> Cells { get; } = Cells;
        public float[] ColumnWidths { get; } = ColumnWidths;
        public float[] RowHeights { get; } = RowHeights;
        public float ColumnGap { get; } = ColumnGap;
        public float RowGap { get; } = RowGap;
        public float Width { get; } = Sum(ColumnWidths, 0, ColumnWidths.Length) + Math.Max(0, ColumnWidths.Length - 1) * ColumnGap;
        public float Height { get; } = Sum(RowHeights, 0, RowHeights.Length) + Math.Max(0, RowHeights.Length - 1) * RowGap;
    }

    private readonly record struct TableCellPlacement(
        HtmlSceneNode Cell,
        int RowIndex,
        int ParentRowIndex,
        int ColumnIndex,
        int RowSpan,
        int ColSpan,
        float MinimumWidth,
        float PreferredWidth);

    private sealed class LayoutScratchArena
    {
        private LayoutChildRequest[] requestBuffer = [];
        private LayoutFrameData?[] frameBuffer = [];
        private float[] floatBuffer = [];
        private InlineLayoutItem[] inlineItemBuffer = [];
        private List<string>[] rowChildBuffer = [];
        private int requestOffset;
        private int frameOffset;
        private int floatOffset;
        private int inlineItemOffset;
        private int rowChildOffset;

        public ScratchMark Mark() => new(requestOffset, frameOffset, floatOffset, inlineItemOffset, rowChildOffset);

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
            var frames = frameBuffer.AsSpan(start, length);
            frames.Clear();
            return frames;
        }

        public Span<float> AllocateFloats(int length)
        {
            if (length <= 0)
                return [];

            EnsureFloatCapacity(floatOffset + length);
            var start = floatOffset;
            floatOffset += length;
            var values = floatBuffer.AsSpan(start, length);
            values.Clear();
            return values;
        }

        public Span<InlineLayoutItem> AllocateInlineItems(int length)
        {
            if (length <= 0)
                return [];

            EnsureInlineItemCapacity(inlineItemOffset + length);
            var start = inlineItemOffset;
            inlineItemOffset += length;
            return inlineItemBuffer.AsSpan(start, length);
        }

        public Span<List<string>> AllocateRowChildBuffers(int length)
        {
            if (length <= 0)
                return [];

            EnsureRowChildCapacity(rowChildOffset + length);
            var start = rowChildOffset;
            rowChildOffset += length;
            var buffers = rowChildBuffer.AsSpan(start, length);
            for (var index = 0; index < buffers.Length; index++)
            {
                buffers[index] ??= [];
                buffers[index].Clear();
            }

            return buffers;
        }

        public void Rewind(ScratchMark mark)
        {
            if (mark.RequestOffset == 0 && requestOffset > 0)
                Array.Clear(requestBuffer, 0, requestOffset);
            if (mark.RowChildOffset == 0 && rowChildOffset > 0)
            {
                for (var index = 0; index < rowChildOffset; index++)
                    rowChildBuffer[index]?.Clear();
            }

            requestOffset = mark.RequestOffset;
            frameOffset = mark.FrameOffset;
            floatOffset = mark.FloatOffset;
            inlineItemOffset = mark.InlineItemOffset;
            rowChildOffset = mark.RowChildOffset;
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

        private void EnsureFloatCapacity(int requiredLength)
        {
            if (floatBuffer.Length >= requiredLength)
                return;

            Array.Resize(ref floatBuffer, Math.Max(requiredLength, Math.Max(16, floatBuffer.Length * 2)));
        }

        private void EnsureRowChildCapacity(int requiredLength)
        {
            if (rowChildBuffer.Length >= requiredLength)
                return;

            Array.Resize(ref rowChildBuffer, Math.Max(requiredLength, Math.Max(16, rowChildBuffer.Length * 2)));
        }

        private void EnsureInlineItemCapacity(int requiredLength)
        {
            if (inlineItemBuffer.Length >= requiredLength)
                return;

            Array.Resize(ref inlineItemBuffer, Math.Max(requiredLength, Math.Max(16, inlineItemBuffer.Length * 2)));
        }

        public readonly record struct ScratchMark(
            int RequestOffset,
            int FrameOffset,
            int FloatOffset,
            int InlineItemOffset,
            int RowChildOffset);
    }

}


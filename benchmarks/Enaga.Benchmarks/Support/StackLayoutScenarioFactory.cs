using Enaga.Layout;
using Enaga.Rendering;

namespace Enaga.Benchmarks.Support;

internal static class StackLayoutScenarioFactory
{
    public static LayoutChildRequest[] CreateChildren(int childCount, LayoutAxis axis)
    {
        var children = new LayoutChildRequest[childCount];
        var random = new Random(1729 + childCount + (int)axis * 97);
        for (var index = 0; index < childCount; index++)
        {
            if (index % 9 == 0)
            {
                children[index] = new LayoutChildRequest(
                    Kind: LayoutChildKind.Spacer,
                    Size: 12 + index % 5,
                    FlexGrow: 1 + index % 3);
                continue;
            }

            var wrap = index % 5 == 0;
            var width = axis == LayoutAxis.Row && index % 4 == 0 ? LayoutValue.Unset : 80f + random.Next(0, 140);
            var height = axis == LayoutAxis.Column && index % 4 == 0 ? LayoutValue.Unset : 18f + random.Next(0, 42);
            var fontSize = 14 + index % 6;
            children[index] = new LayoutChildRequest(
                Kind: LayoutChildKind.Element,
                Width: width,
                Height: height,
                MinWidth: index % 7 == 0 ? 64 : LayoutValue.Unset,
                MaxWidth: index % 11 == 0 ? 240 : LayoutValue.Unset,
                MinHeight: index % 6 == 0 ? 18 : LayoutValue.Unset,
                MaxHeight: index % 10 == 0 ? 96 : LayoutValue.Unset,
                MarginLeft: random.Next(0, 6),
                MarginTop: random.Next(0, 6),
                MarginRight: random.Next(0, 6),
                MarginBottom: random.Next(0, 6),
                Text: index % 3 == 0 ? $"Item {index} content for layout benchmarking" : null,
                FontSize: fontSize,
                FontWeight: index % 4 == 0 ? 600 : 400,
                Wrap: wrap,
                AlignSelf: index % 8 == 0 ? CrossAlignment.Center : CrossAlignment.Auto,
                FlexGrow: index % 5 == 0 ? 1 + index % 3 : 0);
        }

        return children;
    }
}

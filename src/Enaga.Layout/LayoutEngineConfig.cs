namespace Enaga.Layout;

public sealed record LayoutEngineConfig(
    bool CollapseTextOnlyElements = false,
    bool ApplyBlockWidthAsPercent = false,
    bool ApplySemanticTextSpacing = false,
    bool ApplyFormControlDefaults = false,
    PositionMode DefaultPositionMode = PositionMode.Relative
)
{
    public static LayoutEngineConfig NativeDefaults { get; } = new();

    public static LayoutEngineConfig WebDefaults { get; } =
        new(
            CollapseTextOnlyElements: true,
            ApplyBlockWidthAsPercent: true,
            ApplySemanticTextSpacing: true,
            ApplyFormControlDefaults: true,
            DefaultPositionMode: PositionMode.Static
        );
}

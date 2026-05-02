namespace Enaga.React.OkojoRuntime;

internal sealed class NativeHoverTargetState(string id)
{
    public string Id { get; } = id;
    public int Generation { get; set; }
    public int ZOrder { get; set; }
    public string? Tooltip { get; set; }
}

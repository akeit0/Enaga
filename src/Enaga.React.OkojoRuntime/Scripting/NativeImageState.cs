namespace Enaga.React.OkojoRuntime;

internal enum NativeImageLoadState
{
    None,
    Pending,
    Loaded,
    Failed
}

internal sealed class NativeImageState(string id)
{
    public string Id { get; } = id;

    public string RequestedSource { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string RequestedPlaceholderSource { get; set; } = string.Empty;

    public string PlaceholderSource { get; set; } = string.Empty;

    public int Generation { get; set; }

    public NativeImageLoadState LoadState { get; set; }

    public NativeImageLoadState PlaceholderLoadState { get; set; }
}

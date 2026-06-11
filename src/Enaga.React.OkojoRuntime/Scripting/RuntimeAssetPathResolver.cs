namespace Enaga.React.OkojoRuntime;

internal static class RuntimeAssetPathResolver
{
    public static string Resolve(string source, string? entryPath)
    {
        var resolved = FileSystemAssetResolver.Instance.Resolve(
            new RuntimeAssetRequest(source, entryPath)
        );
        return resolved.Kind switch
        {
            RuntimeAssetKind.LocalPath => resolved.LocalPath ?? source,
            RuntimeAssetKind.Uri => resolved.Uri?.AbsoluteUri ?? source,
            _ => source,
        };
    }
}

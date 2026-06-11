using Enaga.React.OkojoRuntime;
using Xunit;

namespace Enaga.Tests;

public sealed class RuntimeAssetResolverTests
{
    [Fact]
    public void FileSystemResolver_UsesAssetBaseForPlainRelativePaths()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetBasePath = Path.Combine(rootDirectory, "ReactApp");
        Directory.CreateDirectory(assetBasePath);

        try
        {
            var resolved = FileSystemAssetResolver.Instance.Resolve(
                new RuntimeAssetRequest("assets/demo.jpg", assetBasePath)
            );

            Assert.Equal(RuntimeAssetKind.LocalPath, resolved.Kind);
            Assert.Equal(Path.Combine(assetBasePath, "assets", "demo.jpg"), resolved.LocalPath);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void FileSystemResolver_UsesEntryReferrerForDotRelativePaths()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetBasePath = Path.Combine(rootDirectory, "ReactApp");
        var distDirectory = Path.Combine(assetBasePath, "dist");
        Directory.CreateDirectory(distDirectory);
        var entryPath = Path.Combine(distDirectory, "react-entry.mjs");

        try
        {
            var resolved = FileSystemAssetResolver.Instance.Resolve(
                new RuntimeAssetRequest("..\\assets\\demo.jpg", assetBasePath, entryPath)
            );

            Assert.Equal(RuntimeAssetKind.LocalPath, resolved.Kind);
            Assert.Equal(Path.Combine(assetBasePath, "assets", "demo.jpg"), resolved.LocalPath);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}

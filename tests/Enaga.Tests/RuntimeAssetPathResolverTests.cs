using Enaga.React.OkojoRuntime;
using Xunit;

namespace Enaga.Tests;

public sealed class RuntimeAssetPathResolverTests
{
    [Fact]
    public void Resolve_FileUri_ReturnsLocalPath()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(filePath, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 8 8\" />");
        try
        {
            var fileUri = new Uri(filePath).AbsoluteUri;

            var resolved = RuntimeAssetPathResolver.Resolve(fileUri, entryPath: null);

            Assert.Equal(filePath, resolved);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Resolve_RelativePath_UsesEntryDirectory()
    {
        var entryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(entryDirectory);
        var entryPath = Path.Combine(entryDirectory, "react-entry.mjs");

        var resolved = RuntimeAssetPathResolver.Resolve("assets\\demo.svg", entryPath);

        Assert.Equal(Path.Combine(entryDirectory, "assets\\demo.svg"), resolved);
        Directory.Delete(entryDirectory);
    }

    [Fact]
    public void Resolve_HttpsUri_IsPreserved()
    {
        const string source = "https://example.com/demo.svg";

        var resolved = RuntimeAssetPathResolver.Resolve(source, entryPath: null);

        Assert.Equal(source, resolved);
    }
}

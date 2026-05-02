using Enaga.Html;
using Xunit;

namespace Enaga.Tests;

public sealed class HtmlUrlResolverTests
{
    [Fact]
    public void ResolveCombinesRelativePathWithHttpBaseUri()
    {
        var resolved = HtmlUrlResolver.Resolve("images/hero.png", "https://example.test/docs/");

        Assert.Equal("https://example.test/docs/images/hero.png", resolved);
    }

    [Fact]
    public void ResolvePreservesAbsoluteHttpUri()
    {
        const string source = "https://cdn.example.test/assets/hero.png";

        var resolved = HtmlUrlResolver.Resolve(source, "https://example.test/docs/");

        Assert.Equal(source, resolved);
    }
}

using Enaga.SampleApp.SyntaxHighlighting;
using Xunit;

namespace Enaga.Tests;

public sealed class SampleSyntaxHighlighterTests
{
    [Fact]
    public void BuildHighlightedLines_ClassifiesLowercaseJsxTags()
    {
        var highlighter = new SampleSyntaxHighlighter();

        var lines = highlighter.BuildHighlightedLines("""
            function Demo() {
                const [count, setCount] = React.useState(0);
                return <button>{count}</button>;
            }
            """);
        var buttonTags = lines
            .SelectMany(line => line.Segments)
            .Where(segment => segment.Text == "button")
            .ToArray();

        Assert.NotEmpty(buttonTags);
        Assert.All(buttonTags, segment => Assert.Equal("jsx-tag", segment.Kind));
    }

    [Fact]
    public void BuildHighlightedLines_ClassifiesComponentJsxTags_FromMinimumPageSnippet()
    {
        var highlighter = new SampleSyntaxHighlighter();

        var lines = highlighter.BuildHighlightedLines("""
            <Row style={{ gap: 25 }}>
              <Label text={`count: ${count}`} />
              <Button label="+1" onPress={() => setCount((value) => value + 1)} />
            </Row>
            """);
        var segments = lines.SelectMany(line => line.Segments).ToArray();

        AssertTagKind(segments, "Row", "jsx-tag");
        AssertTagKind(segments, "Label", "jsx-tag");
        AssertTagKind(segments, "Button", "jsx-tag");
        Assert.Contains(segments, segment => segment.Text == "count" && segment.Kind == "identifier");
        Assert.Contains(segments, segment => segment.Text == "setCount" && segment.Kind == "identifier");
        Assert.Contains(segments, segment => segment.Text.Contains("+1", StringComparison.Ordinal) && segment.Kind == "string");
        Assert.Contains(segments, segment => segment.Text == "=>" && segment.Kind == "operator");
    }

    [Fact]
    public void BuildHighlightedLines_ReturnsEmpty_ForBlankSource()
    {
        var highlighter = new SampleSyntaxHighlighter();

        var lines = highlighter.BuildHighlightedLines("  ");

        Assert.Empty(lines);
    }

    private static void AssertTagKind(
        SampleSyntaxHighlighter.HighlightedCodeSegment[] segments,
        string text,
        string expectedKind)
    {
        var matches = segments.Where(segment => segment.Text == text).ToArray();
        Assert.NotEmpty(matches);
        Assert.All(matches, segment => Assert.Equal(expectedKind, segment.Kind));
    }
}

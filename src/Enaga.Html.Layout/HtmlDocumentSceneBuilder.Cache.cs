namespace Enaga.Html;

internal sealed class HtmlStyledSceneTreeCache
{
    private HtmlParsedDocument? document;
    private int viewportWidth;
    private int viewportHeight;
    private uint styleVersion;
    private HtmlStyledSceneTree? tree;

    public bool TryGet(
        HtmlParsedDocument candidateDocument,
        int candidateViewportWidth,
        int candidateViewportHeight,
        uint candidateStyleVersion,
        out HtmlStyledSceneTree cachedTree)
    {
        if (ReferenceEquals(document, candidateDocument) &&
            viewportWidth == candidateViewportWidth &&
            viewportHeight == candidateViewportHeight &&
            styleVersion == candidateStyleVersion &&
            tree is not null)
        {
            cachedTree = tree;
            return true;
        }

        cachedTree = default!;
        return false;
    }

    public HtmlStyledSceneTree Set(
        HtmlParsedDocument nextDocument,
        int nextViewportWidth,
        int nextViewportHeight,
        uint nextStyleVersion,
        HtmlStyledSceneTree nextTree)
    {
        document = nextDocument;
        viewportWidth = nextViewportWidth;
        viewportHeight = nextViewportHeight;
        styleVersion = nextStyleVersion;
        tree = nextTree;
        return nextTree;
    }

    public void Clear()
    {
        document = null;
        tree = null;
    }
}

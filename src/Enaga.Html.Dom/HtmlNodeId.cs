namespace Enaga.Html.Dom;

public readonly record struct HtmlNodeId(int Value)
{
    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct HtmlDocumentVersion(uint Value)
{
    public HtmlDocumentVersion Next() => new(Value + 1);
}

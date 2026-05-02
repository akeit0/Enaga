using System.Text;
using Enaga.Html;

namespace Enaga.Benchmarks.Support;

internal static class HtmlBenchmarkFixtures
{
    public static Enaga.Html.HtmlDocument Create(HtmlBenchmarkDocument document)
    {
        return document switch
        {
            HtmlBenchmarkDocument.TextWrapStress => CreateTextWrapStress(),
            _ => throw new ArgumentOutOfRangeException(nameof(document), document, null)
        };
    }

    public static (int Width, int Height) GetViewport(HtmlBenchmarkDocument document)
    {
        return document switch
        {
            HtmlBenchmarkDocument.TextWrapStress => (960, 720),
            _ => throw new ArgumentOutOfRangeException(nameof(document), document, null)
        };
    }

    private static Enaga.Html.HtmlDocument CreateTextWrapStress()
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html><body><main>");
        for (var index = 0; index < 120; index++)
        {
            html.Append("<section class='card'><h2>Receiving lane ");
            html.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            html.Append("</h2><p>Review the <a href='docs.html'>receiving playbook</a> before assigning a carrier window. ");
            html.Append("Confirm supplier holds, dock schedules, customs notes, and customer promises before the next handoff.</p>");
            html.Append("<ul><li>Confirm receiving capacity before assigning a carrier window.</li><li>Check exceptions against the <a href='policy.html'>account note policy</a>.</li><li>Flag temperature-sensitive freight for the morning shift lead.</li></ul></section>");
        }

        html.Append("</main></body></html>");

        const string css = """
            body { margin: 0; font: 16px/1.45 Arial, sans-serif; color: #dbeafe; background: #0f172a; }
            main { padding: 24px; display: block; }
            .card { display: block; width: 48%; margin: 0 2% 18px 0; padding: 14px; border-width: 1px; border-style: solid; border-color: #36547b; border-radius: 8px; background: #13243b; }
            h2 { font-size: 22px; margin: 0 0 10px; line-height: 1.2; }
            p { margin: 0 0 12px; }
            ul { margin: 0 0 0 22px; padding: 0; }
            li { margin: 0 0 6px; }
            a { color: #7dd3fc; font-style: italic; text-decoration: underline; }
            """;

        return new Enaga.Html.HtmlDocument(html.ToString(), css);
    }
}

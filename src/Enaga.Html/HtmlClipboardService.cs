using System.Runtime.InteropServices;
using TextCopy;

namespace Enaga.Html;

internal static class HtmlClipboardService
{
    public static string? GetText()
    {
        try
        {
            return ClipboardService.GetText();
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool SetText(string text)
    {
        try
        {
            ClipboardService.SetText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

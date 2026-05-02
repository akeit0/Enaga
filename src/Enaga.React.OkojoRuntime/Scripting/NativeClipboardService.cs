using System.Runtime.InteropServices;
using TextCopy;

namespace Enaga.React.OkojoRuntime;

internal static class NativeClipboardService
{
    public static string? GetText()
    {
        try
        {
            return ClipboardService.GetText();
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine($"[NativeClipboard] Read failed: {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[NativeClipboard] Read failed: {ex.Message}");
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
        catch (COMException ex)
        {
            Console.Error.WriteLine($"[NativeClipboard] Write failed: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[NativeClipboard] Write failed: {ex.Message}");
            return false;
        }
    }
}

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Enaga.Input;

namespace Enaga.Platforms.Mac;

internal sealed unsafe class MacImeContext : IDisposable
{
    private static readonly ConcurrentDictionary<nint, MacImeContext> ContextsByView = new();
    private static readonly object SwizzleLock = new();
    private static bool swizzled;
    private static nint originalSetMarkedText;
    private static nint originalUnmarkText;
    private static nint originalInsertText;
    private static nint originalFirstRectForCharacterRange;

    private readonly nint nsWindow;
    private readonly nint contentView;
    private readonly ITextCompositionSink compositionSink;
    private bool disposed;
    private bool compositionActive;

    public bool HasPendingVisualUpdate { get; private set; }

    private MacImeContext(nint nsWindow, nint contentView, ITextCompositionSink compositionSink)
    {
        this.nsWindow = nsWindow;
        this.contentView = contentView;
        this.compositionSink = compositionSink;
    }

    public static MacImeContext? TryAttach(nint nsWindow, ITextCompositionSink compositionSink)
    {
        var contentView = ObjectiveC.IntPtr_objc_msgSend(
            nsWindow,
            ObjectiveC.GetSelector("contentView")
        );
        if (contentView == 0)
            return null;

        var context = new MacImeContext(nsWindow, contentView, compositionSink);
        ContextsByView[contentView] = context;
        EnsureSwizzled(ObjectiveC.object_getClass(contentView));
        return context;
    }

    public void ClearPendingVisualUpdate()
    {
        HasPendingVisualUpdate = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ContextsByView.TryRemove(contentView, out _);
    }

    private static void EnsureSwizzled(nint viewClass)
    {
        lock (SwizzleLock)
        {
            if (swizzled || viewClass == 0)
                return;

            var setMarkedText = ObjectiveC.GetSelector(
                "setMarkedText:selectedRange:replacementRange:"
            );
            var unmarkText = ObjectiveC.GetSelector("unmarkText");
            var insertText = ObjectiveC.GetSelector("insertText:replacementRange:");
            var firstRectForCharacterRange = ObjectiveC.GetSelector(
                "firstRectForCharacterRange:actualRange:"
            );

            originalSetMarkedText = ObjectiveC.class_getMethodImplementation(
                viewClass,
                setMarkedText
            );
            originalUnmarkText = ObjectiveC.class_getMethodImplementation(viewClass, unmarkText);
            originalInsertText = ObjectiveC.class_getMethodImplementation(viewClass, insertText);
            originalFirstRectForCharacterRange = ObjectiveC.class_getMethodImplementation(
                viewClass,
                firstRectForCharacterRange
            );

            ObjectiveC.class_replaceMethod(
                viewClass,
                setMarkedText,
                (nint)
                    (delegate* unmanaged<nint, nint, nint, NSRange, NSRange, void>)
                        &SetMarkedTextCallback,
                "v@:@{_NSRange=QQ}{_NSRange=QQ}"
            );
            ObjectiveC.class_replaceMethod(
                viewClass,
                unmarkText,
                (nint)(delegate* unmanaged<nint, nint, void>)&UnmarkTextCallback,
                "v@:"
            );
            ObjectiveC.class_replaceMethod(
                viewClass,
                insertText,
                (nint)(delegate* unmanaged<nint, nint, nint, NSRange, void>)&InsertTextCallback,
                "v@:@{_NSRange=QQ}"
            );
            ObjectiveC.class_replaceMethod(
                viewClass,
                firstRectForCharacterRange,
                (nint)
                    (delegate* unmanaged<nint, nint, NSRange, nint, NSRect>)
                        &FirstRectForCharacterRangeCallback,
                "{CGRect={CGPoint=dd}{CGSize=dd}}@:{_NSRange=QQ}^{_NSRange=QQ}"
            );
            swizzled = true;
        }
    }

    [UnmanagedCallersOnly]
    private static void SetMarkedTextCallback(
        nint self,
        nint selector,
        nint textObject,
        NSRange selectedRange,
        NSRange replacementRange
    )
    {
        if (ContextsByView.TryGetValue(self, out var context))
            context.SetMarkedText(textObject, selectedRange, replacementRange);

        if (originalSetMarkedText != 0)
            ((delegate* unmanaged<nint, nint, nint, NSRange, NSRange, void>)originalSetMarkedText)(
                self,
                selector,
                textObject,
                selectedRange,
                replacementRange
            );
    }

    [UnmanagedCallersOnly]
    private static void UnmarkTextCallback(nint self, nint selector)
    {
        if (ContextsByView.TryGetValue(self, out var context))
            context.EndComposition();

        if (originalUnmarkText != 0)
            ((delegate* unmanaged<nint, nint, void>)originalUnmarkText)(self, selector);
    }

    [UnmanagedCallersOnly]
    private static void InsertTextCallback(
        nint self,
        nint selector,
        nint textObject,
        NSRange replacementRange
    )
    {
        ContextsByView.TryGetValue(self, out var context);
        context?.PrepareCompositionCommit();

        if (originalInsertText != 0)
            ((delegate* unmanaged<nint, nint, nint, NSRange, void>)originalInsertText)(
                self,
                selector,
                textObject,
                replacementRange
            );

        context?.CompleteCompositionCommit();
    }

    [UnmanagedCallersOnly]
    private static NSRect FirstRectForCharacterRangeCallback(
        nint self,
        nint selector,
        NSRange range,
        nint actualRange
    )
    {
        if (
            ContextsByView.TryGetValue(self, out var context)
            && context.TryGetCandidateRect(out var rect)
        )
        {
            return rect;
        }

        return originalFirstRectForCharacterRange != 0
            ? (
                (delegate* unmanaged<
                    nint,
                    nint,
                    NSRange,
                    nint,
                    NSRect>)originalFirstRectForCharacterRange
            )(self, selector, range, actualRange)
            : default;
    }

    private void PrepareCompositionCommit()
    {
        if (compositionActive)
        {
            compositionSink.PrepareTextCompositionCommit();
            HasPendingVisualUpdate = true;
        }
    }

    private void CompleteCompositionCommit()
    {
        if (!compositionActive)
            return;

        compositionActive = false;
        compositionSink.EndTextComposition();
        HasPendingVisualUpdate = true;
    }

    private void SetMarkedText(nint textObject, NSRange selectedRange, NSRange replacementRange)
    {
        var text = ObjectiveC.ToString(textObject);
        if (!compositionActive)
        {
            compositionActive = true;
            if (
                TryResolveReplacementStart(replacementRange, out var startIndex)
                && compositionSink is ITextCompositionRangeSink rangeSink
            )
                rangeSink.StartTextComposition(startIndex);
            else
                compositionSink.StartTextComposition();
        }

        var selectionStart = checked((int)Math.Min(selectedRange.Location, (nuint)int.MaxValue));
        var selectionLength = checked((int)Math.Min(selectedRange.Length, (nuint)int.MaxValue));
        compositionSink.UpdateTextComposition(
            text,
            selectionStart,
            selectionStart,
            selectionLength
        );
        HasPendingVisualUpdate = true;
    }

    private static bool TryResolveReplacementStart(NSRange replacementRange, out int startIndex)
    {
        const ulong nsNotFound = ulong.MaxValue;
        var location = (ulong)replacementRange.Location;
        if (location == nsNotFound || location > int.MaxValue)
        {
            startIndex = 0;
            return false;
        }

        startIndex = (int)location;
        return true;
    }

    private void EndComposition()
    {
        if (!compositionActive)
            return;

        compositionActive = false;
        compositionSink.EndTextComposition();
        HasPendingVisualUpdate = true;
    }

    private bool TryGetCandidateRect(out NSRect rect)
    {
        rect = default;
        if (!compositionSink.TryGetTextCompositionCursor(out var cursor))
            return false;

        var contentView = ObjectiveC.IntPtr_objc_msgSend(
            nsWindow,
            ObjectiveC.GetSelector("contentView")
        );
        if (contentView == 0)
            return false;

        var bounds = ObjectiveC.NSRect_objc_msgSend(contentView, ObjectiveC.GetSelector("bounds"));
        var localRect = new NSRect(
            cursor.X,
            Math.Max(0, bounds.Height - cursor.Y - cursor.Height),
            Math.Max(1, cursor.Width),
            Math.Max(1, cursor.Height)
        );
        rect = ObjectiveC.NSRect_objc_msgSend_NSRect(
            nsWindow,
            ObjectiveC.GetSelector("convertRectToScreen:"),
            localRect
        );
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSRange(nuint Location, nuint Length);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NSRect(double X, double Y, double Width, double Height);

    private static class ObjectiveC
    {
        private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjCLibrary)]
        public static extern nint sel_registerName(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name
        );

        [DllImport(ObjCLibrary)]
        public static extern nint object_getClass(nint obj);

        [DllImport(ObjCLibrary)]
        public static extern nint class_getMethodImplementation(nint cls, nint selector);

        [DllImport(ObjCLibrary)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool class_replaceMethod(
            nint cls,
            nint selector,
            nint implementation,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string types
        );

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint IntPtr_objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint IntPtr_objc_msgSend_IntPtr(
            nint receiver,
            nint selector,
            nint value
        );

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern byte Byte_objc_msgSend_IntPtr(
            nint receiver,
            nint selector,
            nint value
        );

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern nint UTF8String_objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern NSRect NSRect_objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        public static extern NSRect NSRect_objc_msgSend_NSRect(
            nint receiver,
            nint selector,
            NSRect rect
        );

        public static nint GetSelector(string name) => sel_registerName(name);

        public static string ToString(nint textObject)
        {
            if (textObject == 0)
                return string.Empty;

            var stringSelector = GetSelector("string");
            var respondsToSelector = GetSelector("respondsToSelector:");
            var utf8String = GetSelector("UTF8String");
            var nsString =
                Byte_objc_msgSend_IntPtr(textObject, respondsToSelector, stringSelector) != 0
                    ? IntPtr_objc_msgSend(textObject, stringSelector)
                    : textObject;
            var utf8 = UTF8String_objc_msgSend(nsString, utf8String);
            return utf8 == 0 ? string.Empty : Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
        }
    }
}

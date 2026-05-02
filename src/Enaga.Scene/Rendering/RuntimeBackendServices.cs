using Enaga.Scene;

namespace Enaga.Rendering;

public readonly record struct RuntimeCaretPosition(float X, float Y);

public enum RuntimeImageResolveState
{
    Pending = 0,
    Ready = 1,
    Failed = 2
}

public readonly record struct RuntimeImageResolveResult(
    RuntimeImageResolveState State,
    string? LocalPath = null,
    string? Error = null);

public interface IRuntimeTextServices : IDisposable
{
    void ConfigureFonts(string? defaultFamily = null, IReadOnlyList<string>? fallbackFamilies = null);
    void RegisterFont(string family, string source);
    float MeasureTextHeight(string content, float width, SceneTextStyle style);
    float MeasureLineHeight(SceneFont font);
    float MeasureLineHeight(float fontSize);
    float MeasureTextWidth(string content, SceneTextStyle style);
    float MeasureTextWidth(ReadOnlySpan<char> content, SceneTextStyle style);
    int BreakText(ReadOnlySpan<char> content, float maxWidth, SceneTextStyle style, out float measuredWidth);
    int SnapCaretIndex(string text, int caretIndex);
    int GetPreviousTextElementIndex(string text, int caretIndex);
    int GetNextTextElementIndex(string text, int caretIndex);
    RuntimeCaretPosition GetCaretPosition(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex);
    int HitTestCaretIndex(SceneTextStyle style, string text, float lineHeight, float maxWidth, float x, float y);
    int MoveCaretVertical(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, int lineDelta, float? preferredX);
    int MoveCaretToLineEdge(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, bool toEnd);
}

public interface IRuntimeImageResolver
{
    RuntimeImageResolveResult ResolveImage(string source);
}

public sealed class RuntimeBackendServices : IDisposable
{
    private static readonly IRuntimeTextServices MissingText = new MissingRuntimeTextServices();
    private static readonly IRuntimeImageResolver MissingImages = new MissingRuntimeImageResolver();

    public RuntimeBackendServices(
        IRuntimeTextServices? text = null,
        IRuntimeImageResolver? images = null)
    {
        Text = text ?? MissingText;
        Images = images ?? MissingImages;
    }

    public static RuntimeBackendServices Missing { get; } = new();

    public IRuntimeTextServices Text { get; }
    public IRuntimeImageResolver Images { get; }

    public void Dispose()
    {
        if (!ReferenceEquals(Text, MissingText))
            Text.Dispose();

        if (Images is IDisposable disposableImages && !ReferenceEquals(Images, MissingImages))
            disposableImages.Dispose();
    }

    private sealed class MissingRuntimeTextServices : IRuntimeTextServices
    {
        private static InvalidOperationException CreateException()
            => new("No runtime text services are configured. Pass a backend service bundle when creating the runtime host.");

        public void ConfigureFonts(string? defaultFamily = null, IReadOnlyList<string>? fallbackFamilies = null) => throw CreateException();
        public void RegisterFont(string family, string source) => throw CreateException();
        public float MeasureTextHeight(string content, float width, SceneTextStyle style) => throw CreateException();
        public float MeasureLineHeight(SceneFont font) => throw CreateException();
        public float MeasureLineHeight(float fontSize) => throw CreateException();
        public float MeasureTextWidth(string content, SceneTextStyle style) => throw CreateException();
        public float MeasureTextWidth(ReadOnlySpan<char> content, SceneTextStyle style) => throw CreateException();
        public int BreakText(ReadOnlySpan<char> content, float maxWidth, SceneTextStyle style, out float measuredWidth) => throw CreateException();
        public int SnapCaretIndex(string text, int caretIndex) => throw CreateException();
        public int GetPreviousTextElementIndex(string text, int caretIndex) => throw CreateException();
        public int GetNextTextElementIndex(string text, int caretIndex) => throw CreateException();
        public RuntimeCaretPosition GetCaretPosition(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex) => throw CreateException();
        public int HitTestCaretIndex(SceneTextStyle style, string text, float lineHeight, float maxWidth, float x, float y) => throw CreateException();
        public int MoveCaretVertical(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, int lineDelta, float? preferredX) => throw CreateException();
        public int MoveCaretToLineEdge(SceneTextStyle style, string text, float lineHeight, float maxWidth, int caretIndex, bool toEnd) => throw CreateException();
        public void Dispose() { }
    }

    private sealed class MissingRuntimeImageResolver : IRuntimeImageResolver
    {
        public RuntimeImageResolveResult ResolveImage(string source)
        {
            throw new InvalidOperationException("No runtime image resolver is configured. Pass a backend service bundle when creating the runtime host.");
        }
    }
}

public interface IRuntimeBackendServicesSource
{
    RuntimeBackendServices BackendServices { get; }
}

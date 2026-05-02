using Enaga.Input;
using Enaga.Scene;
using Enaga.Rendering;

namespace Enaga.React.OkojoRuntime;

internal sealed class NativeScrollViewState : ISceneScrollOffsetState
{
    public NativeScrollViewState(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public string ParentId { get; set; } = "root";

    public float Left { get; set; }

    public float Top { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public float ContentHeight { get; set; }

    public float ContentWidth { get; set; }

    public bool HorizontalScrollEnabled { get; set; }

    public float ScrollX { get; set; }

    public float ScrollY { get; set; }

    public float TargetScrollX { get; set; }

    public float TargetScrollY { get; set; }

    public string? BackgroundColor { get; set; }

    public SceneGradient? BackgroundGradient { get; set; }

    public SceneRuntimeShader? BackgroundShader { get; set; }

    public SceneBoxShadow[]? BackgroundShadows { get; set; }

    public string? BorderColor { get; set; }

    public float BorderWidth { get; set; }

    public float BorderRadius { get; set; }

    public SceneBoxSizing BoxSizing { get; set; } = SceneBoxSizing.ContentBox;

    public bool ClipContent { get; set; }

    public float PaddingLeft { get; set; }

    public float PaddingTop { get; set; }

    public float PaddingRight { get; set; }

    public float PaddingBottom { get; set; }

    public int ZOrder { get; set; }

    public int Generation { get; set; }
}

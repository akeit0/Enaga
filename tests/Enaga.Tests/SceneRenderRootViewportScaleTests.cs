using Enaga.Rendering;
using Enaga.Rendering.Skia;
using Enaga.Scene;
using Enaga.Input;
using SkiaSharp;
using Xunit;

namespace Enaga.Tests;

public sealed class SceneRenderRootViewportScaleTests
{
    [Fact]
    public void Render_ForcesFullPresentationDirtyRectWhenViewportScaleChanges()
    {
        var source = new TestViewportScaleFrameSource();
        using var root = new SceneRenderRoot(source);
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 200, 100, TimeSpan.Zero);
        source.Scale = 2f;
        root.Render(canvas, 200, 100, TimeSpan.FromMilliseconds(16));

        var dirtyRect = Assert.Single(root.GetLastDirtyRects().ToArray());
        Assert.Equal(new SceneDamageRect(0, 0, 200, 100), dirtyRect);
    }

    [Fact]
    public void Render_ScalesSceneDirtyRectsForPresentationWhenViewportScaleIsActive()
    {
        var source = new TestViewportScaleFrameSource { Scale = 2f };
        using var root = new SceneRenderRoot(source);
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 200, 100, TimeSpan.Zero);
        source.NextDirtyRects = [new SceneDamageRect(10, 12, 20, 8)];
        root.Render(canvas, 200, 100, TimeSpan.FromMilliseconds(16));

        var dirtyRect = Assert.Single(root.GetLastDirtyRects().ToArray());
        Assert.Equal(new SceneDamageRect(20, 24, 40, 16), dirtyRect);
    }

    [Fact]
    public void Render_ForcesFullPresentationDirtyRectWhenPresentationSizeChanges()
    {
        var source = new TestViewportScaleFrameSource();
        using var root = new SceneRenderRoot(source);
        using var bitmap = new SKBitmap(240, 120);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 200, 100, TimeSpan.Zero);
        root.Render(canvas, 240, 120, TimeSpan.FromMilliseconds(16));

        var dirtyRect = Assert.Single(root.GetLastDirtyRects().ToArray());
        Assert.Equal(new SceneDamageRect(0, 0, 240, 120), dirtyRect);
    }

    [Fact]
    public void Render_ForcesFullPresentationDirtyRectWhenPresentationSurfaceIsInvalidated()
    {
        var source = new TestViewportScaleFrameSource();
        using var root = new SceneRenderRoot(source);
        using var bitmap = new SKBitmap(200, 100);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 200, 100, TimeSpan.Zero);
        root.Render(canvas, 200, 100, TimeSpan.FromMilliseconds(16));
        Assert.Empty(root.GetLastDirtyRects().ToArray());

        root.InvalidatePresentationSurface();
        root.Render(canvas, 200, 100, TimeSpan.FromMilliseconds(32));

        var dirtyRect = Assert.Single(root.GetLastDirtyRects().ToArray());
        Assert.Equal(new SceneDamageRect(0, 0, 200, 100), dirtyRect);
    }

    [Fact]
    public void ScaleOverlay_IsCenteredAndBlocksPointerInput()
    {
        var source = new TestViewportScaleFrameSource();
        using var root = new SceneRenderRoot(source);
        using var bitmap = new SKBitmap(500, 120);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 500, 120, TimeSpan.Zero);
        source.Scale = 1.1f;
        root.Render(canvas, 500, 120, TimeSpan.FromMilliseconds(16));

        Assert.False(root.HitTestOverlayInput(20, 20));
        Assert.True(root.HitTestOverlayInput(250, 20));

        root.PointerMove(250, 20, 0, synthetic: false);
        Assert.Equal(0, source.PointerMoveCount);

        root.PointerMove(20, 70, 0, synthetic: false);
        Assert.Equal(1, source.PointerMoveCount);
    }

    [Fact]
    public void ScaleOverlay_ButtonsChangeViewportScale()
    {
        var source = new TestViewportScaleFrameSource();
        using var root = new SceneRenderRoot(source);
        using var bitmap = new SKBitmap(500, 120);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 500, 120, TimeSpan.Zero);
        source.Scale = 1.1f;
        root.Render(canvas, 500, 120, TimeSpan.FromMilliseconds(16));

        root.PointerMove(320, 20, 0, synthetic: false);
        root.PointerDown(0, 1, synthetic: false);
        Assert.Equal(1.25f, source.Scale);

        root.PointerMove(285, 20, 0, synthetic: false);
        root.PointerDown(0, 1, synthetic: false);
        Assert.Equal(0.9f, source.Scale);

        root.PointerMove(370, 20, 0, synthetic: false);
        root.PointerDown(0, 1, synthetic: false);
        Assert.Equal(1f, source.Scale);
    }

    [Fact]
    public void ScaleOverlay_ExpiredOverlayRepaintsItsOldBounds()
    {
        var source = new TestViewportScaleFrameSource();
        var timeProvider = new TestTimeProvider();
        using var root = new SceneRenderRoot(source, new SceneRenderRootOptions { TimeProvider = timeProvider });
        using var bitmap = new SKBitmap(500, 120);
        using var canvas = new SKCanvas(bitmap);

        root.Render(canvas, 500, 120, TimeSpan.Zero);
        source.Scale = 1.1f;
        root.Render(canvas, 500, 120, TimeSpan.FromMilliseconds(16));
        timeProvider.Advance(TimeSpan.FromMilliseconds(24));
        root.Render(canvas, 500, 120, TimeSpan.FromMilliseconds(24));
        Assert.Contains(root.GetLastDirtyRects().ToArray(), rect => rect == new SceneDamageRect(85, 0, 330, 48));

        timeProvider.Advance(TimeSpan.FromSeconds(3));
        root.Render(canvas, 500, 120, TimeSpan.FromMilliseconds(40));

        Assert.Contains(root.GetLastDirtyRects().ToArray(), rect => rect == new SceneDamageRect(85, 0, 330, 48));
        Assert.False(root.HitTestOverlayInput(250, 20));
    }

    [Fact]
    public void InputForwarding_RequestsRenderWakeAfterSourceMutation()
    {
        var source = new TestViewportScaleFrameSource();
        using var root = new SceneRenderRoot(source);
        var wakeCount = 0;
        root.RenderWakeRequested += () => wakeCount++;

        root.PointerDown(0, 1, synthetic: false);

        Assert.Equal(1, source.PointerDownCount);
        Assert.Equal(1, wakeCount);
    }

    private sealed class TestViewportScaleFrameSource : ISceneFrameSource, IRenderViewportScaleController, IInputSink
    {
        private bool rendered;

        public string? LastError => null;

        public float Scale { get; set; } = 1f;

        public SceneDamageRect[]? NextDirtyRects { get; set; }

        public float ViewportScale => Scale;

        public int PointerMoveCount { get; private set; }

        public int PointerDownCount { get; private set; }

        public bool TryStepViewportScale(int direction)
        {
            Scale = direction > 0 ? 1.25f : 0.9f;
            return true;
        }

        public bool TryResetViewportScale()
        {
            Scale = 1f;
            return true;
        }

        public SceneFrameResult RenderFrame(int width, int height, TimeSpan elapsed)
        {
            var commit = CreateCommit(width, height);
            if (NextDirtyRects is { } dirtyRects)
            {
                NextDirtyRects = null;
                rendered = true;
                return new SceneFrameResult(commit, dirtyRects, SceneDamageReason.Scroll);
            }

            if (!rendered)
            {
                rendered = true;
                return SceneFrameResult.FullFrame(commit, width, height, SceneDamageReason.RuntimeReload);
            }

            return SceneFrameResult.NoDamage(commit);
        }

        public void PointerMove(float x, float y, int buttons, bool synthetic)
        {
            PointerMoveCount++;
        }

        public void PointerDown(int button, int buttons, bool synthetic)
        {
            PointerDownCount++;
        }

        public void PointerUp(int button, int buttons, bool synthetic)
        {
        }

        public void Wheel(float deltaX, float deltaY, bool synthetic)
        {
        }

        public void Wheel(float deltaX, float deltaY, bool synthetic, int modifiers = 0)
        {
        }

        public void KeyDown(string key, int modifiers, bool repeat, bool synthetic)
        {
        }

        public void KeyUp(string key, int modifiers, bool synthetic)
        {
        }

        public void TextInput(string text, bool synthetic)
        {
        }

        private static SceneLayoutCommit CreateCommit(int width, int height)
        {
            var root = new SceneNodeId(1);
            var nodes = new Dictionary<SceneNodeId, SceneGraphNode>
            {
                [root] = new(SceneNodeKind.View, null, []),
            };
            var layout = new Dictionary<SceneNodeId, SceneLayoutBox>
            {
                [root] = new(SceneNodeKind.View, 0, 0, width, height, "#000000"),
            };

            return new SceneLayoutCommit(root, new SceneViewport(width, height), nodes, layout, []);
        }
    }

    private sealed class TestTimeProvider(long timestampFrequency = 1000) : TimeProvider
    {
        private long timestamp;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long TimestampFrequency => timestampFrequency;

        public override DateTimeOffset GetUtcNow()
            => DateTimeOffset.UnixEpoch + GetElapsedTime(0, timestamp);

        public override long GetTimestamp()
            => timestamp;

        public void Advance(TimeSpan delta)
        {
            timestamp += (long)Math.Round(delta.TotalSeconds * timestampFrequency, MidpointRounding.AwayFromZero);
        }
    }

}

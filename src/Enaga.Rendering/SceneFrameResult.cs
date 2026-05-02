using Enaga.Scene;

namespace Enaga.Rendering;

[Flags]
public enum SceneDamageReason
{
    None = 0,
    Resize = 1 << 0,
    TextInput = 1 << 1,
    CaretBlink = 1 << 2,
    Composition = 1 << 3,
    Scroll = 1 << 4,
    ImageReady = 1 << 5,
    Animation = 1 << 6,
    FontCatalogChanged = 1 << 7,
    ErrorOverlay = 1 << 8,
    RuntimeReload = 1 << 9,
    LowLevelDraw = 1 << 10,
    FullFrameFallback = 1 << 11,
    FragmentDamage = 1 << 12
}

public readonly record struct SceneDamageRect(int X, int Y, int Width, int Height)
{
    public long PixelCount => (long)Math.Max(0, Width) * Math.Max(0, Height);
}

public sealed record SceneFrameResult(
    SceneLayoutCommit Commit,
    SceneDamageRect[] DirtyRects,
    SceneDamageReason DamageReasons)
{
    public long DirtyPixelCount
    {
        get
        {
            long pixels = 0;
            foreach (var rect in DirtyRects)
                pixels += rect.PixelCount;
            return pixels;
        }
    }

    public static SceneFrameResult NoDamage(SceneLayoutCommit commit)
    {
        return new SceneFrameResult(commit, [], SceneDamageReason.None);
    }

    public static SceneFrameResult FullFrame(SceneLayoutCommit commit, int width, int height, SceneDamageReason damageReasons)
    {
        if (width <= 0 || height <= 0)
            return new SceneFrameResult(commit, [], damageReasons);

        return new SceneFrameResult(
            commit,
            [new SceneDamageRect(0, 0, width, height)],
            damageReasons);
    }
}

namespace Enaga.Hosting;

[Flags]
public enum RenderTraceLogFlags
{
    None = 0,
    Paint = 1 << 0,
    ViewPerFrame = 1 << 1,
    Damage = 1 << 2,
    Runtime = 1 << 3,
    All = Paint | ViewPerFrame | Damage | Runtime
}

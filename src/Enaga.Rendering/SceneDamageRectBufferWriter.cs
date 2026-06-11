using System.Buffers;

namespace Enaga.Rendering;

public sealed class SceneDamageRectBufferWriter : IDisposable
{
    internal SceneDamageRect[] Buffer { get; private set; }

    public int Count { get; private set; }

    public ReadOnlySpan<SceneDamageRect> WrittenSpan => Buffer.AsSpan(0, Count);

    public SceneDamageRectBufferWriter(int capacity = 0)
    {
        Buffer =
            capacity > 0
                ? ArrayPool<SceneDamageRect>.Shared.Rent(capacity)
                : Array.Empty<SceneDamageRect>();
    }

    public void Clear()
    {
        Count = 0;
    }

    public void Truncate(int count)
    {
        Count = Math.Clamp(count, 0, Count);
    }

    public void Add(SceneDamageRect rect)
    {
        if (Count == Buffer.Length)
            Grow();

        Buffer[Count++] = rect;
    }

    public SceneDamageRect[] ToArray(int count)
    {
        return Buffer.AsSpan(0, count).ToArray();
    }

    public void Dispose()
    {
        if (Buffer.Length == 0)
            return;

        ArrayPool<SceneDamageRect>.Shared.Return(Buffer);
        Buffer = Array.Empty<SceneDamageRect>();
        Count = 0;
    }

    private void Grow()
    {
        var nextBuffer = ArrayPool<SceneDamageRect>.Shared.Rent(Math.Max(4, Buffer.Length * 2));
        Buffer.AsSpan(0, Count).CopyTo(nextBuffer);
        if (Buffer.Length > 0)
            ArrayPool<SceneDamageRect>.Shared.Return(Buffer);
        Buffer = nextBuffer;
    }
}

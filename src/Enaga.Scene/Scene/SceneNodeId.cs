namespace Enaga.Scene;

public readonly record struct SceneNodeId(ulong Value)
{
    public bool IsValid => Value != 0;

    public override string ToString() => IsValid ? Value.ToString("x16") : "<invalid>";
}

public sealed class SceneNodeIdAllocator
{
    private ulong nextValue;

    public SceneNodeIdAllocator(ulong firstValue = 1)
    {
        nextValue = Math.Max(1UL, firstValue);
    }

    public SceneNodeId Allocate()
    {
        if (nextValue == ulong.MaxValue)
            throw new InvalidOperationException("Scene node id space is exhausted.");

        return new SceneNodeId(nextValue++);
    }

    public void Reset(ulong firstValue = 1) => nextValue = Math.Max(1UL, firstValue);
}

public sealed class SceneNodeIdentityMap<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, SceneNodeId> ids;
    private readonly SceneNodeIdAllocator allocator;

    public SceneNodeIdentityMap(TKey rootKey, IEqualityComparer<TKey>? comparer = null)
        : this(rootKey, new SceneNodeIdAllocator(), comparer) { }

    public SceneNodeIdentityMap(
        TKey rootKey,
        SceneNodeIdAllocator allocator,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ids = new Dictionary<TKey, SceneNodeId>(comparer);
        this.allocator = allocator;
        RootId = this.allocator.Allocate();
        ids[rootKey] = RootId;
    }

    public SceneNodeId RootId { get; }

    public SceneNodeId GetOrCreate(TKey key)
    {
        if (ids.TryGetValue(key, out var id))
            return id;

        id = allocator.Allocate();
        ids[key] = id;
        return id;
    }

    public bool TryGet(TKey key, out SceneNodeId id) => ids.TryGetValue(key, out id);
}

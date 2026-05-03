using System.Collections;

namespace Enaga.Scene;

public sealed class SceneNodeMap<T> : IReadOnlyDictionary<SceneNodeId, T>
{
    private readonly Dictionary<SceneNodeId, T> items;

    public SceneNodeMap()
    {
        items = new Dictionary<SceneNodeId, T>();
    }

    public SceneNodeMap(int capacity)
    {
        items = new Dictionary<SceneNodeId, T>(capacity);
    }

    public SceneNodeMap(IReadOnlyDictionary<SceneNodeId, T> source)
    {
        items = new Dictionary<SceneNodeId, T>(source.Count);
        CopyFrom(source);
    }

    public int Count => items.Count;

    public IEnumerable<SceneNodeId> Keys => items.Keys;

    public IEnumerable<T> Values => items.Values;

    public T this[SceneNodeId key]
    {
        get => items[key];
        set => items[key] = value;
    }

    public void Clear()
        => items.Clear();

    public bool ContainsKey(SceneNodeId key)
        => items.ContainsKey(key);

    public void CopyFrom(IReadOnlyDictionary<SceneNodeId, T> source)
    {
        items.Clear();
        foreach (var pair in source)
            items[pair.Key] = pair.Value;
    }

    public void EnsureCapacity(int capacity)
        => items.EnsureCapacity(capacity);

    public IEnumerator<KeyValuePair<SceneNodeId, T>> GetEnumerator()
        => items.GetEnumerator();

    public bool Remove(SceneNodeId key)
        => items.Remove(key);

    public bool TryGetValue(SceneNodeId key, out T value)
        => items.TryGetValue(key, out value!);

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}

using System.Collections;

namespace Enaga.Scene;

public sealed class SceneNodeMap<T> : IReadOnlyDictionary<SceneNodeId, T>
{
    private readonly Dictionary<SceneNodeId, T> items;
    private SceneNodeMap<T>? fallback;

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

    private SceneNodeMap(SceneNodeMap<T> fallback, int overrideCapacity)
    {
        this.fallback = fallback;
        items = new Dictionary<SceneNodeId, T>(overrideCapacity);
    }

    public int Count
    {
        get
        {
            if (fallback is null)
                return items.Count;

            var count = fallback.Count;
            foreach (var key in items.Keys)
            {
                if (!fallback.ContainsKey(key))
                    count++;
            }

            return count;
        }
    }

    public IEnumerable<SceneNodeId> Keys => fallback is null ? items.Keys : EnumerateOverlayKeys();

    public IEnumerable<T> Values => fallback is null ? items.Values : EnumerateOverlayValues();

    public T this[SceneNodeId key]
    {
        get
        {
            if (items.TryGetValue(key, out var value))
                return value;
            if (fallback is not null)
                return fallback[key];
            return items[key];
        }
        set => items[key] = value;
    }

    public void Clear()
    {
        fallback = null;
        items.Clear();
    }

    public bool ContainsKey(SceneNodeId key)
        => items.ContainsKey(key) || (fallback?.ContainsKey(key) == true);

    public void CopyFrom(IReadOnlyDictionary<SceneNodeId, T> source)
    {
        fallback = null;
        items.Clear();
        foreach (var pair in source)
            items[pair.Key] = pair.Value;
    }

    public static SceneNodeMap<T> CreateOverlay(SceneNodeMap<T> fallback, int overrideCapacity = 0)
        => fallback.fallback is null
            ? new SceneNodeMap<T>(fallback, overrideCapacity)
            : new SceneNodeMap<T>(fallback);

    public void EnsureCapacity(int capacity)
        => items.EnsureCapacity(capacity);

    public Enumerator GetEnumerator()
        => new(items, fallback);

    public bool Remove(SceneNodeId key)
        => items.Remove(key);

    public bool TryGetValue(SceneNodeId key, out T value)
    {
        if (items.TryGetValue(key, out value!))
            return true;

        if (fallback is not null)
            return fallback.TryGetValue(key, out value!);

        return false;
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    IEnumerator<KeyValuePair<SceneNodeId, T>> IEnumerable<KeyValuePair<SceneNodeId, T>>.GetEnumerator()
        => GetEnumerator();

    private IEnumerable<KeyValuePair<SceneNodeId, T>> EnumerateOverlay()
    {
        foreach (var pair in fallback!)
        {
            yield return items.TryGetValue(pair.Key, out var value)
                ? new KeyValuePair<SceneNodeId, T>(pair.Key, value)
                : pair;
        }

        foreach (var pair in items)
        {
            if (!fallback!.ContainsKey(pair.Key))
                yield return pair;
        }
    }

    private IEnumerable<SceneNodeId> EnumerateOverlayKeys()
    {
        foreach (var pair in this)
            yield return pair.Key;
    }

    private IEnumerable<T> EnumerateOverlayValues()
    {
        foreach (var pair in this)
            yield return pair.Value;
    }

    public struct Enumerator : IEnumerator<KeyValuePair<SceneNodeId, T>>
    {
        private Dictionary<SceneNodeId, T>.Enumerator fallbackEnumerator;
        private Dictionary<SceneNodeId, T>.Enumerator itemEnumerator;
        private readonly Dictionary<SceneNodeId, T>? fallbackItems;
        private readonly Dictionary<SceneNodeId, T> items;
        private int phase;

        internal Enumerator(Dictionary<SceneNodeId, T> items, SceneNodeMap<T>? fallback)
        {
            this.items = items;
            fallbackItems = fallback?.items;
            fallbackEnumerator = fallbackItems?.GetEnumerator() ?? default;
            itemEnumerator = items.GetEnumerator();
            phase = fallbackItems is null ? 1 : 0;
            Current = default;
        }

        public KeyValuePair<SceneNodeId, T> Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (phase == 0)
            {
                while (fallbackEnumerator.MoveNext())
                {
                    var pair = fallbackEnumerator.Current;
                    Current = items.TryGetValue(pair.Key, out var value)
                        ? new KeyValuePair<SceneNodeId, T>(pair.Key, value)
                        : pair;
                    return true;
                }

                phase = 1;
            }

            while (phase == 1 && itemEnumerator.MoveNext())
            {
                var pair = itemEnumerator.Current;
                if (fallbackItems?.ContainsKey(pair.Key) == true)
                    continue;

                Current = pair;
                return true;
            }

            phase = 2;
            Current = default;
            return false;
        }

        public void Reset()
        {
            fallbackEnumerator = fallbackItems?.GetEnumerator() ?? default;
            itemEnumerator = items.GetEnumerator();
            phase = fallbackItems is null ? 1 : 0;
            Current = default;
        }

        public void Dispose()
        {
            fallbackEnumerator.Dispose();
            itemEnumerator.Dispose();
        }
    }
}

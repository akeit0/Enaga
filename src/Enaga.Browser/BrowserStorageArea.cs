namespace Enaga.Browser;

internal sealed class BrowserStorageArea
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private readonly List<string> keys = [];

    public int Length
    {
        get
        {
            lock (gate)
                return keys.Count;
        }
    }

    public string? Key(int index)
    {
        lock (gate)
            return index >= 0 && index < keys.Count ? keys[index] : null;
    }

    public string? GetItem(string key)
    {
        lock (gate)
            return values.TryGetValue(key, out var value) ? value : null;
    }

    public void SetItem(string key, string value)
    {
        lock (gate)
        {
            if (!values.ContainsKey(key))
                keys.Add(key);
            values[key] = value;
        }
    }

    public void RemoveItem(string key)
    {
        lock (gate)
        {
            if (!values.Remove(key))
                return;
            keys.Remove(key);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            values.Clear();
            keys.Clear();
        }
    }
}

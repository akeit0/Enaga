namespace Enaga.Input;

public sealed class SceneWheelScrollTargetLatch<T>
    where T : notnull
{
    private readonly double timeoutMs;
    private T? activeId;
    private double lastWheelElapsedMs = double.NegativeInfinity;

    public SceneWheelScrollTargetLatch(double timeoutMs = 200)
    {
        this.timeoutMs = timeoutMs;
    }

    public T? ActiveId => activeId;

    public void Clear()
    {
        activeId = default;
        lastWheelElapsedMs = double.NegativeInfinity;
    }

    public bool TryUseActive(double elapsedMs, out T id)
    {
        var withinActiveGesture = elapsedMs - lastWheelElapsedMs <= timeoutMs;
        lastWheelElapsedMs = elapsedMs;

        if (withinActiveGesture && activeId is { } currentId)
        {
            id = currentId;
            return true;
        }

        id = default!;
        return false;
    }

    public T? SetActive(T? id)
    {
        activeId = id;
        return activeId;
    }
}

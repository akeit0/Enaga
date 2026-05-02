namespace Enaga.React.OkojoRuntime;

public interface IReactAppEntrySource : IDisposable
{
    string DisplayPath { get; }

    string AssetBasePath { get; }

    IEnumerable<string> EnumerateWatchPaths();

    string PrepareEntryPath();
}

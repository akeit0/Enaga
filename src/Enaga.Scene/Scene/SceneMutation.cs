namespace Enaga.Scene;

public abstract record SceneMutation;

public sealed record ResetSceneMutation(string RootId, SceneViewport Viewport) : SceneMutation;

public sealed record SetViewportMutation(SceneViewport Viewport) : SceneMutation;

public sealed record UpsertNodeMutation(
    string Id,
    SceneNodeKind Kind,
    string? ParentId = null,
    string? Label = null) : SceneMutation;

public sealed record SetChildrenMutation(string ParentId, IReadOnlyList<string> Children) : SceneMutation;

public sealed record SetLayoutMutation(string Id, SceneLayoutBox Layout) : SceneMutation;

public sealed record RemoveNodeMutation(string Id) : SceneMutation;

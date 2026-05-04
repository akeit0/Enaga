namespace Enaga.Scene;

public abstract record SceneMutation;

public sealed record ResetSceneMutation(SceneNodeId RootId, SceneViewport Viewport) : SceneMutation;

public sealed record SetViewportMutation(SceneViewport Viewport) : SceneMutation;

public sealed record UpsertNodeMutation(
    SceneNodeId Id,
    SceneNodeKind Kind,
    SceneNodeId? ParentId = null,
    string? Label = null) : SceneMutation;

public sealed record SetChildrenMutation(SceneNodeId ParentId, SceneNodeId[] Children) : SceneMutation;

public sealed record SetLayoutMutation(SceneNodeId Id, SceneLayoutBox Layout) : SceneMutation;

public sealed record RemoveNodeMutation(SceneNodeId Id) : SceneMutation;

namespace Enaga.Rendering;

internal static class SceneDamageReasonState
{
    public static SceneDamageReason Consume(
        ref SceneDamageReason pendingDamageReasons,
        bool animationEnabled,
        bool shaderAnimationEnabled = false
    )
    {
        var damageReasons = pendingDamageReasons;
        if (animationEnabled || shaderAnimationEnabled)
            damageReasons |= SceneDamageReason.Animation;

        pendingDamageReasons = SceneDamageReason.None;
        return damageReasons == SceneDamageReason.None
            ? SceneDamageReason.FullFrameFallback
            : damageReasons;
    }
}

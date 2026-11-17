namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Which of `CTFPlayerAnimState::Update`'s custom-model branches draws a player (`tf_playeranimstate.cpp:340-366`, B765).
/// </summary>
/// <remarks>
/// A custom model without the class's animations returns before the feet are turned, so its drawn angles are this branch's
/// every frame, not the feet's. Carried on the player so each per-frame route keeps them rather than recomputing the feet.
/// </remarks>
public enum CustomRenderAngles
{
    /// <summary>The class's own animations: the feet draw him.</summary>
    None,

    /// <summary>`m_angCustomModelRotation`, the set rotation whole.</summary>
    Rotation,

    /// <summary>The eye yaw, with no pitch or roll.</summary>
    EyeYaw,

    /// <summary>Not rotating: `m_angRender` as it last stood.</summary>
    Held,
}

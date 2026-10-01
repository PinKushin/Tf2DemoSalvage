using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The four ConVars <c>CDemoPlayer::InterpolateViewpoint</c> reads, as the watcher has them (B56, D190).</summary>
/// <param name="InterpolateView"><c>demo_interpolateview</c>, read as <c>m_nValue</c>.</param>
/// <param name="InterpLimit"><c>demo_interplimit</c>: origin speed, units a second, past which the view snaps.</param>
/// <param name="AvelLimit"><c>demo_avellimit</c>: local-angle speed, degrees a second, past which it snaps.</param>
/// <param name="LegacyRollback"><c>demo_legacy_rollback</c>, read as <c>m_nValue</c>.</param>
public readonly record struct DemoViewConVars(bool InterpolateView, float InterpLimit, float AvelLimit, bool LegacyRollback)
{
    /// <summary><c>demo_interpolateview</c>.</summary>
    public const string InterpolateViewName = "demo_interpolateview";

    /// <summary><c>demo_interplimit</c>.</summary>
    public const string InterpLimitName = "demo_interplimit";

    /// <summary><c>demo_avellimit</c>.</summary>
    public const string AvelLimitName = "demo_avellimit";

    /// <summary><c>demo_legacy_rollback</c>.</summary>
    public const string LegacyRollbackName = "demo_legacy_rollback";

    /// <summary>The registrations' defaults, from <see cref="EngineConVars"/>.</summary>
    public static DemoViewConVars Defaults { get; } = new(
        (int)EngineConVars.ByName(InterpolateViewName).Number != 0,
        EngineConVars.ByName(InterpLimitName).Number,
        EngineConVars.ByName(AvelLimitName).Number,
        (int)EngineConVars.ByName(LegacyRollbackName).Number != 0);
}

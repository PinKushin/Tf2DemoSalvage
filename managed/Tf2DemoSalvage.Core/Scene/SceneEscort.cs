namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What `CTFHudEscort` holds from its escort events at one moment (game/client/tf/tf_hud_escort.cpp:883-925).</summary>
/// <param name="Progress">`m_flProgress`: the last `escort_progress`.</param>
/// <param name="ShownProgress">`m_flProgressHistory.Interpolate( curtime, hud_escort_interp )`: what the cart is drawn at.</param>
/// <param name="Speed">`m_iSpeedLevel`.</param>
/// <param name="Players">The last `escort_speed`'s `players`.</param>
/// <param name="RecedeTime">`m_flRecedeTime`.</param>
/// <param name="SwoopStart">When the cart last started moving from still, which `m_bShowSwoop` marks, or 0.</param>
public readonly record struct SceneEscort(float Progress, float ShownProgress, int Speed, int Players, float RecedeTime, float SwoopStart);

using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// One choreographed scene starting to play, as the wire describes it (B351).
/// </summary>
/// <param name="Tick">The tick <c>m_bIsPlayingBack</c> became true.</param>
/// <param name="EntityIndex">The <c>CSceneEntity</c>'s own slot.</param>
/// <param name="Scene">
/// The compiled scene's filename, out of the <c>Scenes</c> string table — the only thing on the
/// wire that says which taunt this is.
/// </param>
/// <param name="Actors">
/// The entities the scene animates, from <c>m_hActorList</c>. For a taunt this is the taunting
/// player, and for a partner taunt both of them.
/// </param>
/// <remarks>
/// **A start, not a state, because TF2 runs the scene's clock client-side.**
/// `C_SceneEntity::DoThink` advances `m_flCurrentTime += gpGlobals->frametime`
/// (<c>c_sceneentity.cpp:992</c>) and `OnResetClientTime` — the one thing that would resync it from
/// the server's `m_flForceClientTime` — is `#ifndef TF_CLIENT_DLL` around its only statement, with
/// the comment *"In TF2 we ignore this as the scene is played entirely client-side"*
/// (<c>c_sceneentity.cpp:70</c>). So the scene's time is elapsed time since playback began, and a
/// viewer driving its own clock can reproduce it exactly from this one tick.
///
/// **`m_flForceClientTime` is therefore NOT carried here**, deliberately: it arrives on the wire and
/// TF2's client throws it away. Carrying it would invite a consumer to honour a value the game does
/// not.
///
/// **The stop is <see cref="StoppedTick"/>** (B513): a face drops a stopped scene's events, as
/// <c>StopPlayback</c> clears them. The gesture does not read it — its layer's own auto-kill removes it when
/// its cycle passes one (<c>multiplayer_animstate.cpp:1275</c>).
/// </remarks>
public sealed record SceneChoreography(
    int Tick,
    int EntityIndex,
    string Scene,
    IReadOnlyList<int> Actors)
{
    /// <summary>
    /// The tick <c>m_bIsPlayingBack</c> went false (or the slot began another scene), or null while it plays on — where
    /// <c>C_SceneEntity::StopPlayback</c> clears every actor's scene events (B513).
    /// </summary>
    public int? StoppedTick { get; init; }
}

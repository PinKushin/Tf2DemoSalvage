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
/// **What is NOT recorded is the stop.** A scene that ends is `m_bIsPlayingBack` going false, and a
/// gesture layer's own auto-kill already removes it when its cycle passes one
/// (<c>multiplayer_animstate.cpp:1275</c>) — which is how the engine ends a taunt whose scene the
/// server has already stopped sending. A LOOP scene held open by the server, such as the high-five
/// idle, is the case that will need it.
/// </remarks>
public sealed record SceneChoreography(
    int Tick,
    int EntityIndex,
    string Scene,
    IReadOnlyList<int> Actors);

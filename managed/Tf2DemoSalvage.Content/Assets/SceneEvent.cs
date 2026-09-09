namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One event out of a compiled choreography scene (B351).
/// </summary>
/// <param name="Type">
/// The <c>CChoreoEvent::EVENTTYPE</c> (<c>choreoevent.h:257</c>) — 6 is <c>GESTURE</c> and 7 is
/// <c>SEQUENCE</c>, the two that name an animation.
/// </param>
/// <param name="Start">When the event begins, in seconds from the scene's start.</param>
/// <param name="End">When it ends, or -1 for an event with no duration.</param>
/// <param name="Parameters">
/// The first parameter string. For a gesture this is the sequence name the client resolves against
/// the player's own model: <c>LookupSequence( event-&gt;GetParameters() )</c>
/// (<c>c_tf_player.cpp:9456</c>).
/// </param>
/// <param name="At">Where in the decompressed scene the event's first byte sits.</param>
/// <remarks>
/// **The times are carried because the engine plays a scene on its own clock**, so a scene staging
/// two gestures one after another cannot be reduced to a single sequence name. TF2 runs that clock
/// client-side — `C_SceneEntity::OnResetClientTime` is `#ifndef TF_CLIENT_DLL` around its only
/// statement, *"In TF2 we ignore this as the scene is played entirely client-side"*
/// (<c>c_sceneentity.cpp:70</c>) — which is why a viewer driving time itself can reproduce it.
/// </remarks>
public readonly record struct SceneEvent(byte Type, float Start, float End, string Parameters, int At);

using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One event out of a compiled choreography scene (B351).
/// </summary>
/// <param name="Type">
/// The <c>CChoreoEvent::EVENTTYPE</c> (<c>choreoevent.h:257</c>) — 6 is <c>GESTURE</c> and 7 is
/// <c>SEQUENCE</c>, the two that name an animation; 10 is <c>FLEXANIMATION</c>, the one that moves a face.
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
public readonly record struct SceneEvent(byte Type, float Start, float End, string Parameters, int At)
{
    /// <summary>The second parameter — for an <c>EXPRESSION</c>, the flex setting's name (<c>GetParameters2</c>).</summary>
    public string Parameters2 { get; init; } = string.Empty;

    /// <summary>The event's own intensity ramp, <c>m_Ramp</c> — empty means a constant 1.</summary>
    public IReadOnlyList<SceneCurveSample> Ramp { get; init; } = [];

    /// <summary>The flex animation tracks, <c>RestoreFlexAnimationsFromBuffer</c> (<c>choreoevent.cpp:4455</c>).</summary>
    public IReadOnlyList<SceneFlexTrack> FlexTracks { get; init; } = [];
}

/// <summary>One flex sample, <c>choreoevent.cpp:4477-4481</c>: time, value / 255, curve type.</summary>
/// <param name="Time">Seconds into the event.</param>
/// <param name="Value">0 to 1, in the track's own range.</param>
/// <param name="CurveType">
/// <c>MAKE_CURVE_TYPE( in, out )</c>: the low byte is the left interpolator, the next the right.
/// </param>
public readonly record struct SceneFlexSample(float Time, float Value, int CurveType);

/// <summary>One flex animation track, as the binary restore leaves it (no edge info is stored).</summary>
/// <param name="Controller">The controller's name; a combo track prefixes <c>right_</c>/<c>left_</c>.</param>
/// <param name="Active">Flag bit 0.</param>
/// <param name="Combo">Flag bit 1: a stereo track with a balance curve.</param>
/// <param name="Min">The track's range bottom.</param>
/// <param name="Max">Its top.</param>
/// <param name="Samples">The magnitude curve, type 0.</param>
/// <param name="Balance">The balance curve, type 1, for a combo track.</param>
public sealed record SceneFlexTrack(
    string Controller,
    bool Active,
    bool Combo,
    float Min,
    float Max,
    IReadOnlyList<SceneFlexSample> Samples,
    IReadOnlyList<SceneFlexSample> Balance);

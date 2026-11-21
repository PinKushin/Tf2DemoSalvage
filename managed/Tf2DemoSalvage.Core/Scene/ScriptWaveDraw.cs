using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// A client script sound's `GetParametersForSound`, carried to the moment it plays and drawn there against the script's
/// availability flags (B503, B782).
/// </summary>
/// <param name="Script">The soundscript entry's name.</param>
/// <param name="Emitted">
/// `GetParametersForSoundEx`'s `isbeingemitted`: true for `EmitSoundByHandle`, which clears the wave it picks; false for a
/// plain `GetParametersForSound` (footsteps, physics impacts, sound patches), which only reads the flags.
/// </param>
/// <param name="Stream">
/// Where the stream stood when the FIRST `GetParametersForSound` on it began — this one's, or for a draw <see cref="After"/>
/// another, that one's.
/// </param>
/// <remarks>
/// **Why the stream and not a number** (B782): soundemittersystem.dll's `GetParametersForSound` (FUN_180003370) draws the
/// volume, the pitch, then FUN_180005680's `RandomInt( 0, count - 1 )` over the waves AVAILABLE, then the soundlevel — and
/// `RandomInt` takes no number for a span of one and draws again on a refusal. How many numbers the wave takes depends on
/// the flags when the sound plays, so everything after it on the stream can only be drawn then.
/// </remarks>
public sealed record ScriptWaveDraw(string Script, bool Emitted, RandomStreamPosition Stream)
{
    /// <summary>
    /// The `GetParametersForSound` drawn on the same stream just before this one, or null for the first: an impact after its
    /// shrapnel's, a sound patch's `Init` after `PhysFrictionSound`'s own. This one's numbers start where that one's ended.
    /// </summary>
    public ScriptWaveDraw? After { get; init; }

    /// <summary>
    /// A `CSoundPatch` played by `Play( patch, volume, pitch )`: the sound's <c>Volume</c> multiplies the script volume this
    /// draw gives (`GetVolumeForEngine`), and its <c>Pitch</c> is the one `Play` gave, not this draw's.
    /// </summary>
    public bool Scaled { get; init; }
}

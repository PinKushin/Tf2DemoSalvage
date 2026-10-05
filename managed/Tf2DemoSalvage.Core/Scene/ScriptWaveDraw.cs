namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The wave draw of a client script sound, picked against the script's availability flags when it plays (B503).</summary>
/// <param name="Script">The soundscript entry's name.</param>
/// <param name="Draw">The generator's own number at the wave's place in the draws — `RandomInt( 0, n - 1 )` is it mod n.</param>
/// <param name="Emitted">
/// `GetParametersForSoundEx`'s `isbeingemitted`: true for `EmitSoundByHandle`, which clears the wave it picks; false for a
/// plain `GetParametersForSound` (footsteps, physics impacts, sound patches), which only reads the flags.
/// </param>
public readonly record struct ScriptWaveDraw(string Script, int Draw, bool Emitted);

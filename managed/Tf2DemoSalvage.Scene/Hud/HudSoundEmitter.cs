namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// Lets a HUD element ask for a <c>game_sounds.txt</c> script to play — `C_BaseEntity::EmitSound` with a
/// `CLocalPlayerFilter`, which is always the local listener's own 2D sound (`tf_hud_deathnotice.cpp:751`,
/// `hud_basechat.cpp:793`).
/// </summary>
/// <param name="scriptName">The script's name in <c>game_sounds.txt</c>, such as <c>"Game.Domination"</c>.</param>
/// <remarks>
/// **The seam that keeps Scene/Hud free of an audio dependency.** A HUD element calls this delegate by name and knows
/// nothing about <c>Tf2DemoSalvage.Audio</c> or <c>SoundScriptCatalog</c>; the presentation layer supplies the
/// delegate and resolves the name against the loaded scripts, the same way <c>EntitySounds.Emit</c> resolves a
/// world sound against them.
/// </remarks>
public delegate void HudSoundEmitter(string scriptName);

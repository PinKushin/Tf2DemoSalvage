namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// What one compiled scene's event walk found, and how far it got (B351).
/// </summary>
/// <param name="Sequence">
/// The name the first <c>GESTURE</c> or <c>SEQUENCE</c> event carries, or null when the scene plays
/// none — which most of the archive does, since a speech scene is all `SPEAK` and flex.
/// </param>
/// <param name="Complete">
/// Whether every declared actor, channel and event was consumed without running out of bytes.
/// </param>
/// <param name="Stopped">The cursor the walk finished on.</param>
/// <param name="Length">The scene's decompressed length, to compare that cursor against.</param>
/// <remarks>
/// **<paramref name="Sequence"/> being null and <paramref name="Complete"/> being false are
/// different findings, which is the whole reason this type exists.** A null sequence is ordinary; an
/// incomplete walk means a stride is wrong somewhere and any name found past that point was read
/// from the wrong offset. One nullable string cannot say which happened, and collapsing them is how
/// the actor tree stayed missing from this reader — see
/// `docs/findings/53-a-taunt-names-its-sequence-in-a-scene.md`.
/// </remarks>
public readonly record struct SceneWalk(string? Sequence, bool Complete, int Stopped, int Length);

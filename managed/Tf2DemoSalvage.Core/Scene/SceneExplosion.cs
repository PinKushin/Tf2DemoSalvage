using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One explosion, as <c>CTETFExplosion</c> sent it (B415).</summary>
/// <param name="Tick">The demo tick the temp entity arrived on.</param>
/// <param name="X">`m_vecOrigin[0]` — the blast's world position.</param>
/// <param name="Y">`m_vecOrigin[1]`.</param>
/// <param name="Z">`m_vecOrigin[2]`.</param>
/// <param name="Normal">
/// `m_vecNormal` — the surface the blast struck, or something too small to be one when it happened in mid air. See
/// <see cref="InAir"/>.
/// </param>
/// <param name="WeaponId">
/// `m_iWeaponID`, which names the weapon script the effect comes from. `TfWeaponAliases.ForExplosion` is what turns
/// it into a script name, because four ids are remapped first.
/// </param>
/// <param name="Entity">
/// `entindex`, the thing that was hit, or <see cref="NoEntity"/>. **Its only job is `bIsPlayer`**, which picks
/// `ExplosionPlayerEffect` over `ExplosionEffect` — the same branch mid air takes.
/// </param>
/// <param name="CustomParticleIndex">
/// `m_iCustomParticleIndex`, an index into the `ParticleEffectNames` string table which overrides the weapon
/// script's choice entirely, or <see cref="NoCustomParticle"/>.
/// </param>
/// <param name="StruckPlayer">
/// `bIsPlayer` — whether <see cref="Entity"/> named a player in the client's entity list when the blast arrived
/// (`tf_fx_explosions.cpp:62-70`). **The one field NOT on the wire**: the client asks its own list, so this is
/// resolved against the entity table as that packet left it, which is the same moment.
/// </param>
/// <remarks>
/// **Everything else here is on the wire and nothing is inferred.** `m_nDefID` and `m_nSound` are decoded and
/// dropped: they select a replacement SOUND from the item definition (`tf_fx_explosions.cpp:133-152`) and nothing
/// about what is drawn.
/// </remarks>
public readonly record struct SceneExplosion(
    int Tick,
    float X,
    float Y,
    float Z,
    (float X, float Y, float Z) Normal,
    int WeaponId,
    int Entity,
    int CustomParticleIndex,
    bool StruckPlayer = false)
{
    /// <summary>What `m_iCustomParticleIndex` is when the blast names no particular effect.</summary>
    /// <remarks>
    /// **65535, and it is NOT −1** — `networkstringtabledefs.h:17` declares
    /// <c>const unsigned short INVALID_STRING_INDEX = (unsigned short)-1</c>, and the field it is assigned into is
    /// an `int`. The server sends it through `SendPropInt( m_iCustomParticleIndex, -1 )`, which is a signed
    /// 32-bit field, so what arrives is 65535.
    ///
    /// **Measured wrong before it was read**: with −1 as the sentinel, 2,492 of `demostf-cp_process_f12`'s 2,786
    /// explosions reported a custom particle — 89%, which read as a genuine finding about modern TF2 rather than
    /// as an off-by-65536. Nothing failed; the number was simply available and plausible.
    /// </remarks>
    public const int NoCustomParticle = 65535;

    /// <summary>What <see cref="Entity"/> is when the blast struck nothing that has an index.</summary>
    /// <remarks>
    /// **The wire has TWO encodings for this and both have to be recognised.**
    /// `RecvProxy_ExplosionEntIndex` (`tf_fx_explosions.cpp:222-229`) carries Valve's own note:
    ///
    /// <code>
    /// // The 'new' encoding for INVALID_EHANDLE_INDEX is 2047, but the old encoding
    /// // was -1. Old demos and replays will use the old encoding so we have to check
    /// // for it. The field is now unsigned so -1 will not be created in new replays.
    /// m_hEntity = (nEntIndex == kInvalidEHandleExplosion || nEntIndex == -1)
    ///     ? INVALID_EHANDLE : ClientEntityList().EntIndexToHandle( nEntIndex );
    /// </code>
    ///
    /// This is a comment written FOR a demo player, which is unusual enough to be worth keeping: a reader that knew
    /// only the modern 2047 would hand every old demo's blast an entity index of −1 to look up, and one that knew
    /// only −1 would look up edict 2047. Neither reports anything.
    ///
    /// **Not zero, because zero is worldspawn** and a blast really can name it.
    /// </remarks>
    public const int NoEntity = -1;

    /// <summary>`kInvalidEHandleExplosion` — <c>MAX_EDICTS - 1</c>, the modern encoding for no entity.</summary>
    public const int InvalidEntityIndex = 2047;

    /// <summary>How small every component of the normal must be for the blast to count as mid air.</summary>
    /// <remarks>
    /// `tf_fx_explosions.cpp:76`, with Valve's own explanation on the line above it:
    ///
    /// <code>
    /// // Cannot use zeros here because we are sending the normal at a smaller bit size.
    /// if ( fabs( vecNormal.x ) &lt; 0.05f &amp;&amp; fabs( vecNormal.y ) &lt; 0.05f &amp;&amp; fabs( vecNormal.z ) &lt; 0.05f )
    /// </code>
    ///
    /// So "mid air" is not a flag on the wire and not a trace — it is the server declining to send a surface, and
    /// the client recognising the smallness that survived quantisation.
    ///
    /// **And the comment is literally true, which the send table settles**:
    /// `SendPropVector( SENDINFO_NOCHECK( m_vecNormal ), 6, 0, -1.0f, 1.0f )` (`tf_fx.cpp:140`) is SIX bits per
    /// component over <c>[-1, 1]</c>. Sixty-four steps across a two-unit range put the representable values at
    /// <c>2k/63 − 1</c>, and no integer <c>k</c> gives zero — the nearest are ±0.0159. An exact zero genuinely
    /// cannot be sent, so a threshold is the only way to read the intent and 0.05 sits comfortably above the
    /// quantisation step of 0.0317.
    /// </remarks>
    public const float AirThreshold = 0.05f;

    /// <summary>Whether the blast happened in mid air rather than against a surface.</summary>
    /// <remarks>
    /// **All three components, not the length.** A length test would agree on these inputs and disagree on a normal
    /// of <c>(0.049, 0.049, 0.049)</c>, whose length is 0.085 — and Valve tests the components.
    /// </remarks>
    public bool InAir =>
        MathF.Abs(Normal.X) < AirThreshold &&
        MathF.Abs(Normal.Y) < AirThreshold &&
        MathF.Abs(Normal.Z) < AirThreshold;

    /// <summary>Whether the blast names its own particle effect rather than taking the weapon's.</summary>
    public bool HasCustomParticle => CustomParticleIndex != NoCustomParticle;

    /// <summary>Whether the blast struck something with an entity index.</summary>
    public bool HasEntity => Entity != NoEntity;
}

using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>What a frame's gather decides cast types by.</summary>
/// <param name="LocalPlayer">The local player's origin — the recorder, or SourceTV's own client — or null for none.</param>
/// <param name="Seconds">The client clock, <c>gpGlobals-&gt;curtime</c>.</param>
/// <param name="RenderToTextureOff">
/// <c>r_shadowrendertotexture 0</c>: every render-to-texture shadow is simple (<c>GetActualShadowCastType</c>,
/// `clientshadowmgr.cpp:2217-2236`). Named for the off state so a default context is the engine's default.
/// </param>
/// <param name="Blobby"><c>cl_blobbyshadows</c>: a player casts <c>SHADOWS_SIMPLE</c> (`c_tf_player.cpp:9274`).</param>
public readonly record struct ShadowGatherContext(
    Vector3? LocalPlayer, double Seconds, bool RenderToTextureOff = false, bool Blobby = false);

/// <summary>The drawn entities as the client shadow manager reads them — each one's <c>ShadowCastType</c> and bounds.</summary>
/// <remarks>
/// **`C_BaseEntity::ShadowCastType`** (`c_baseentity.cpp:1502-1509`): render-to-texture for a studio model, none otherwise.
/// **`C_BaseAnimating::ShadowCastType`** (`c_baseanimating.cpp:820-851`), which a prop, a building and a projectile run:
/// <c>SHADOWS_RENDER_TO_TEXTURE_DYNAMIC</c> for a model with sequences and any pose parameter, bone controller or IK chain
/// — a sentry, a door — redrawn every frame it is in view; plain render-to-texture otherwise. The other overrides are
/// filed in finding 107: a worn item's (always dynamic) and a weapon's (none unless active) decide only whether it is
/// drawn into its wearer's shadow; the bosses' are listed there.
/// **`C_TFPlayer`** (`c_tf_player.cpp:9255-9303`): a drawn player is <c>SHADOWS_RENDER_TO_TEXTURE_DYNAMIC</c>, and its
/// shadow bounds are its render bounds fattened 36 in x and y — "the rotation of the guy's upper torso can place his gun
/// a ways out of his bbox". A player not drawn — the first-person one — is not on the drawn list, so casts nothing.
///
/// <c>EF_NOSHADOW</c> casts none, for both (`c_baseentity.cpp:1504`, `c_tf_player.cpp:9264`). An attached prop whose
/// parent is not drawn is skipped: its pose is relative to that parent, so it has no origin of its own to cast from.
/// A corpse casts nothing yet (finding 107).
/// </remarks>
public sealed class ShadowEntities
{
    /// <summary><c>g_flFattenAmt</c> as <c>GetShadowRenderBounds</c> sets it.</summary>
    public const float PlayerFatten = 36f;

    /// <summary><c>nb_shadow_dist</c>'s default (`C_NextBot.cpp:17`): a NextBot nearer the local player casts render-to-texture.</summary>
    public const float NextBotShadowDistance = 400f;

    /// <summary>How often a NextBot's shadow type is decided again — `m_shadowTimer.Start( 0.15f )` (`C_NextBot.cpp:119`).</summary>
    public const double NextBotShadowPeriod = 0.15;

    /// <summary>
    /// The client classes that are <c>C_NextBotCombatCharacter</c>s (`C_NextBot.cpp:113-124`), by their networked names: the
    /// Halloween bosses, the skeletons and the unused bot NPCs.
    /// </summary>
    private static readonly HashSet<string> NextBots = new(StringComparer.Ordinal)
    {
        "CHeadlessHatman", "CMerasmus", "CEyeballBoss", "CZombie", "CBotNPC", "CBotNPCMinion", "CBossAlpha",
    };

    /// <summary><c>C_TFBaseBoss</c> and its tank (`c_tf_base_boss.cpp:16-24`): always render-to-texture dynamic.</summary>
    private static readonly HashSet<string> BaseBosses = new(StringComparer.Ordinal) { "CTFBaseBoss", "CTFTankBoss" };

    /// <summary>The entities drawn this frame, kept to be reused.</summary>
    private readonly HashSet<int> _present = [];

    /// <summary>Each NextBot's last decided shadow type and when it is decided again — <c>m_shadowType</c>, <c>m_shadowTimer</c>.</summary>
    private readonly Dictionary<int, (string Model, ShadowCast Type, double Next)> _nextBotLod = [];
    private readonly List<int> _gone = [];

    /// <summary>The drawn props as shadow entities.</summary>
    /// <param name="drawn">What the moment draws.</param>
    /// <param name="isPlayer">Whether an entity is a player.</param>
    /// <param name="renderBounds">A prop's render bounds in its own frame.</param>
    /// <param name="animates">Whether a prop's model animates its shadow (<see cref="PropModels.ModelFrames.AnimatingShadow"/>).</param>
    /// <param name="falloffBias">A prop's shadow falloff bias from its last <c>ComputeFxBlend</c>.</param>
    /// <param name="context">The local player and the clock, which a NextBot's shadow type is decided by.</param>
    /// <param name="into">Where they go, cleared first.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Gather(
        IReadOnlyList<SceneProp> drawn,
        Func<int, bool> isPlayer,
        Func<SceneProp, (Vector3 Min, Vector3 Max)> renderBounds,
        Func<SceneProp, bool> animates,
        Func<SceneProp, byte> falloffBias,
        ShadowGatherContext context,
        ICollection<ShadowEntity> into)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(isPlayer);
        ArgumentNullException.ThrowIfNull(renderBounds);
        ArgumentNullException.ThrowIfNull(animates);
        ArgumentNullException.ThrowIfNull(falloffBias);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        _present.Clear();

        foreach (SceneProp prop in drawn)
        {
            _present.Add(prop.EntityIndex);
        }

        foreach (SceneProp prop in drawn)
        {
            if (prop.AttachedTo is { } parent && !_present.Contains(parent))
            {
                continue;
            }

            bool player = isPlayer(prop.EntityIndex);
            ShadowCast cast = CastOf(prop, player, animates, context);

            // `GetActualShadowCastType`: with render-to-texture off, every render-to-texture shadow is simple.
            if (context.RenderToTextureOff && cast is ShadowCast.RenderToTexture or ShadowCast.Dynamic)
            {
                cast = ShadowCast.Simple;
            }

            (Vector3 min, Vector3 max) = renderBounds(prop);
            Vector3? direction = null;

            if (player && cast == ShadowCast.Simple)
            {
                // `C_TFPlayer::GetShadowRenderBounds` and `GetShadowCastDirection` for a simple shadow (`:9281-9329`): the
                // collision box, "or else the shadow will pop and stretch", cast straight down.
                (min, max) = CollisionBox(prop);
                direction = -Vector3.UnitZ;
            }
            else if (player)
            {
                min -= new Vector3(PlayerFatten, PlayerFatten, 0f);
                max += new Vector3(PlayerFatten, PlayerFatten, 0f);
            }

            into.Add(new ShadowEntity(
                prop.EntityIndex,
                prop.AttachedTo,
                cast,
                new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll),
                min,
                max,
                prop.ModelPath,
                (prop.Pose.Sequence, prop.Pose.Cycle),
                falloffBias(prop))
            {
                CastDirection = direction,
            });
        }

        _gone.Clear();

        foreach (int bot in _nextBotLod.Keys)
        {
            if (!_present.Contains(bot))
            {
                _gone.Add(bot);
            }
        }

        foreach (int bot in _gone)
        {
            _nextBotLod.Remove(bot);
        }
    }

    /// <summary>
    /// A drawn entity's <c>ShadowCastType</c>: none for a model that is not studio or a corpse; a NextBot's by its distance
    /// (<see cref="NextBotLod"/>, which tests no <c>EF_NOSHADOW</c>); otherwise none under <c>EF_NOSHADOW</c>, dynamic for
    /// <c>C_TFPlayer</c> and <c>C_TFBaseBoss</c>, and <c>C_BaseAnimating</c>'s for the rest.
    /// </summary>
    private ShadowCast CastOf(SceneProp prop, bool player, Func<SceneProp, bool> animates, ShadowGatherContext context)
    {
        if (prop.Kind != SceneModelKind.Studio || prop.FirstTick is not null)
        {
            return ShadowCast.None;
        }

        if (prop.ClassName is { } bot && NextBots.Contains(bot))
        {
            return NextBotLod(prop, context);
        }

        if (prop.Pose.NoShadow)
        {
            return ShadowCast.None;
        }

        // `C_TFPlayer::ShadowCastType` (`:9255-9278`): simple under `cl_blobbyshadows`, else dynamic.
        if (player)
        {
            return context.Blobby ? ShadowCast.Simple : ShadowCast.Dynamic;
        }

        if (prop.ClassName is { } boss && BaseBosses.Contains(boss))
        {
            return ShadowCast.Dynamic;
        }

        return animates(prop) ? ShadowCast.Dynamic : ShadowCast.RenderToTexture;
    }

    /// <summary>
    /// A player's collision box — <c>CollisionProp()-&gt;OBBMins/OBBMaxs</c>: TF2's hull, (−24, −24, 0)–(24, 24, 82), 62 high
    /// while ducked (<c>g_TFViewVectors</c>, `tf_gamerules.cpp:1310-1324`), scaled by the model scale.
    /// </summary>
    /// <remarks>*Interpolated:* the box is chosen by the pose's <c>FL_DUCKING</c> rather than read from <c>m_Collision</c>.</remarks>
    private static (Vector3 Min, Vector3 Max) CollisionBox(SceneProp prop)
    {
        float top = prop.Pose.Flags is { } flags && (flags & Ducking) != 0 ? 62f : 82f;
        float scale = prop.Pose.Scale;

        return (new Vector3(-24f, -24f, 0f) * scale, new Vector3(24f, 24f, top) * scale);
    }

    /// <summary><c>FL_DUCKING</c> (`const.h`): the player is crouched.</summary>
    private const int Ducking = 1 << 1;

    /// <summary>
    /// <c>C_NextBotCombatCharacter::ShadowCastType</c> and <c>UpdateShadowLOD</c> (`C_NextBot.cpp:66-124`): decided again
    /// once the 0.15-second timer has run out — render-to-texture dynamic within <c>nb_shadow_dist</c> of the local player,
    /// simple beyond it or with no local player.
    /// </summary>
    private ShadowCast NextBotLod(SceneProp prop, ShadowGatherContext context)
    {
        bool known = _nextBotLod.TryGetValue(prop.EntityIndex, out (string Model, ShadowCast Type, double Next) held) &&
            string.Equals(held.Model, prop.ModelPath, StringComparison.Ordinal);

        // `IsElapsed` is `curtime > m_timestamp`; a clock that ran backwards (a seek) decides again too.
        if (known && context.Seconds <= held.Next && context.Seconds > held.Next - NextBotShadowPeriod)
        {
            return held.Type;
        }

        Vector3 origin = new(prop.Pose.X, prop.Pose.Y, prop.Pose.Z);
        ShadowCast type = context.LocalPlayer is { } local && Vector3.Distance(origin, local) < NextBotShadowDistance
            ? ShadowCast.Dynamic
            : ShadowCast.Simple;

        _nextBotLod[prop.EntityIndex] = (prop.ModelPath, type, context.Seconds + NextBotShadowPeriod);
        return type;
    }

    /// <summary>
    /// The drawn brush entities that can take a shadow — `*N` models, at their render origin and angles, bounded by their
    /// model's mins and maxs (<c>GetModelRenderBounds</c>).
    /// </summary>
    /// <param name="drawn">What the moment draws.</param>
    /// <param name="models">The map's models.</param>
    /// <param name="into">Where they go, cleared first.</param>
    /// <remarks>
    /// **`ShouldReceiveProjectedTextures`** (`c_baseentity.cpp:1566-1590`) as `AddShadowToReceiver` asks it, with the whole
    /// type mask: so its flashlight branch, which refuses a render mode above normal at alpha 0 (an undrawn entity is not
    /// on the list). The leaf system's own ask, with the shadow flag (`clientleafsystem.cpp:950-957`), refuses
    /// <c>EF_NORECEIVESHADOW</c> (`c_baseentity.cpp:1583`).
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Brushes(IReadOnlyList<SceneProp> drawn, IReadOnlyList<BspModel> models, ICollection<ShadowBrush> into)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (SceneProp prop in drawn)
        {
            int index = BspModels.IndexOf(prop.ModelPath);

            if (index <= 0 || index >= models.Count || prop.Pose.NoReceiveShadow
                || (prop.Pose.RenderMode > 0 && prop.Pose.RenderAlpha == 0))
            {
                continue;
            }

            BspModel model = models[index];

            into.Add(new ShadowBrush(
                new ShadowEntity(
                    prop.EntityIndex,
                    null,
                    ShadowCast.None,
                    new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                    (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll),
                    new Vector3(model.Minimum.X, model.Minimum.Y, model.Minimum.Z),
                    new Vector3(model.Maximum.X, model.Maximum.Y, model.Maximum.Z),
                    prop.ModelPath),
                model.FirstFace,
                model.FaceCount));
        }
    }
}

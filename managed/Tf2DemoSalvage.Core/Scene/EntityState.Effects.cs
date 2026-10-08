using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The effect entities that override <c>DrawModel</c>: beams, ropes and sprite trails.</summary>
/// <remarks>
/// **Two of the three are NOBASE tables**, and that is what kept them out of the viewer for its whole life:
/// <c>DT_Beam</c> (`beam_shared.cpp:147`) and <c>DT_RopeKeyframe</c> (`c_rope.cpp:42`) inherit nothing from
/// <c>DT_BaseEntity</c> and declare their own copies of the members they need. An accessor that looks only in
/// <c>DT_BaseEntity</c> answers null for every one of them, and <c>DemoTimeline</c>'s admission gate drops an
/// entity with no model — so 18,942 spotlight beams across the corpus produced no track at all.
/// </remarks>
public sealed partial class EntityState
{
    /// <summary><c>CBeam</c>'s own table.</summary>
    private const string BeamTable = "DT_Beam";

    /// <summary><c>CRopeKeyframe</c>'s own table.</summary>
    private const string RopeTable = "DT_RopeKeyframe";

    /// <summary>
    /// The handle this entity moves with — <c>moveparent</c>, from whichever table declares it.
    /// </summary>
    /// <remarks>
    /// **Three tables, because three classes declare it.** <c>DT_BaseEntity</c> for everything that inherits it,
    /// and <c>RecvPropInt( RECVINFO_NAME(m_hNetworkMoveParent, moveparent), 0, RecvProxy_IntToMoveParent )</c>
    /// again inside the two NOBASE tables (`beam_shared.cpp:226`, `c_rope.cpp:61`), so the same member is written
    /// by a different key on a beam and on a rope. A class declares one of the three and never two, so the order
    /// is not a priority — it is the explicit list rather than a scan of every key, because a scan would also
    /// find <c>DT_BaseViewModel</c>'s own fields and change what a viewmodel answers.
    /// </remarks>
    private int? MoveParent() =>
        Integer($"{BaseEntityTable}.{ParentProperty}") ??
        Integer($"{BeamTable}.{ParentProperty}") ??
        Integer($"{RopeTable}.{ParentProperty}");

    /// <summary>A beam's own fields, or null for anything that is not a <c>CBeam</c>.</summary>
    /// <returns>The beam as <c>C_Beam</c> holds it after receive.</returns>
    /// <remarks>
    /// **The type identifies the class**, the way <see cref="SpriteScale"/> identifies a sprite: only
    /// <c>DT_Beam</c> sends <c>m_nBeamType</c>, so a present one means this is a beam and anything else gets
    /// null rather than a record of zeroes.
    ///
    /// **Each fallback is <c>C_Beam</c>'s own** (`beam_shared.cpp:324-344`): the constructor sets
    /// <c>m_nMinDXLevel</c> to 0 and <c>m_flHDRColorScale</c> to 1 and leaves the rest zeroed, so a field the
    /// demo omits is zero except those two.
    ///
    /// **The scroll speed is divided here, as the client does it on receipt.** <c>RecvProxy_Beam_ScrollSpeed</c>
    /// stores <c>m_Value.m_Float * 0.1</c> (`beam_shared.cpp:83-96`), so the speed the drawing code reads is a
    /// tenth of the one on the wire.
    /// </remarks>
    public SceneBeam? Beam()
    {
        if (Integer($"{BeamTable}.m_nBeamType") is not { } type)
        {
            return null;
        }

        List<(int Handle, int Attachment)> ends = new(SceneBeam.MaximumEnds);

        for (int end = 0; end < SceneBeam.MaximumEnds; end++)
        {
            string element = end.ToString("D3", CultureInfo.InvariantCulture);

            ends.Add((
                Integer($"m_hAttachEntity.{element}") ?? InvalidHandle,
                Integer($"m_nAttachIndex.{element}") ?? 0));
        }

        return new SceneBeam(
            type,
            Integer($"{BeamTable}.m_nBeamFlags") ?? 0,
            Integer($"{BeamTable}.m_nNumBeamEnts") ?? 0,
            ends,
            Integer($"{BeamTable}.m_nHaloIndex") ?? 0,
            Number($"{BeamTable}.m_fHaloScale") ?? 0f,
            Number($"{BeamTable}.m_fWidth") ?? 0f,
            Number($"{BeamTable}.m_fEndWidth") ?? 0f,
            Number($"{BeamTable}.m_fFadeLength") ?? 0f,
            Number($"{BeamTable}.m_fAmplitude") ?? 0f,
            Number($"{BeamTable}.m_fStartFrame") ?? 0f,
            (float)((Number($"{BeamTable}.m_fSpeed") ?? 0f) * ScrollSpeedReceived),
            Number($"{BeamTable}.m_flFrameRate") ?? 0f,
            Number($"{BeamTable}.m_flHDRColorScale") ?? 1f,
            Number($"{BeamTable}.m_flFrame") ?? 0f,
            _properties.TryGetValue($"{BeamTable}.m_vecEndPos", out Schema.PropertyValue end3) &&
                end3.Kind == Schema.PropertyValueKind.Vector
                ? end3.AsVector
                : (0f, 0f, 0f),
            Integer($"{BeamTable}.m_nMinDXLevel") ?? 0);
    }

    /// <summary>A sprite trail's own fields, or null for anything that is not a <c>CSpriteTrail</c>.</summary>
    /// <returns>The trail's sampling parameters and the sprite attachment its render origin reads.</returns>
    /// <remarks>
    /// **The lifetime identifies the class**: only <c>DT_SpriteTrail</c> sends <c>m_flLifeTime</c>
    /// (`SpriteTrail.cpp:90`). **Each fallback is the constructor's** (`SpriteTrail.cpp:119-131`): end width −1,
    /// variance 0, skybox scale 1 and origin zero; the rest are zero, and every corpus trail sends them anyway.
    /// </remarks>
    public SceneSpriteTrail? SpriteTrail()
    {
        if (Number($"{SpriteTrailTable}.m_flLifeTime") is not { } lifeTime)
        {
            return null;
        }

        return new SceneSpriteTrail(
            lifeTime,
            Number($"{SpriteTrailTable}.m_flStartWidth") ?? 0f,
            Number($"{SpriteTrailTable}.m_flEndWidth") ?? -1f,
            Number($"{SpriteTrailTable}.m_flStartWidthVariance") ?? 0f,
            Number($"{SpriteTrailTable}.m_flTextureRes") ?? 0f,
            Number($"{SpriteTrailTable}.m_flMinFadeLength") ?? 0f,
            _properties.TryGetValue($"{SpriteTrailTable}.m_vecSkyboxOrigin", out Schema.PropertyValue origin) &&
                origin.Kind == Schema.PropertyValueKind.Vector
                ? origin.AsVector
                : (0f, 0f, 0f),
            Number($"{SpriteTrailTable}.m_flSkyboxScale") ?? 1f,
            Integer($"{SpriteTable}.m_hAttachedToEntity") ?? InvalidHandle,
            Integer($"{SpriteTable}.m_nAttachment") ?? 0);
    }

    /// <summary><c>CSpriteTrail</c>'s own table.</summary>
    private const string SpriteTrailTable = "DT_SpriteTrail";

    /// <summary>A rope's own fields, or null for anything that is not a <c>CRopeKeyframe</c>.</summary>
    /// <returns>The rope as <c>C_RopeKeyframe</c> holds it after receive.</returns>
    /// <remarks>
    /// **The segment count identifies the class**: only <c>DT_RopeKeyframe</c> sends <c>m_nSegments</c>
    /// (`rope.cpp:41`). **Each fallback is the client constructor's** (`c_rope.cpp:1040-1066`): <c>m_Subdiv</c> 255,
    /// <c>m_TextureScale</c> 4, the rest zeroed — the server's own defaults arrive in the baseline.
    /// </remarks>
    public SceneRope? Rope()
    {
        if (Integer($"{RopeTable}.m_nSegments") is not { } segments)
        {
            return null;
        }

        return new SceneRope(
            Integer($"{RopeTable}.m_hStartPoint") ?? InvalidHandle,
            Integer($"{RopeTable}.m_hEndPoint") ?? InvalidHandle,
            Integer($"{RopeTable}.m_iStartAttachment") ?? 0,
            Integer($"{RopeTable}.m_iEndAttachment") ?? 0,
            Integer($"{RopeTable}.m_Slack") ?? 0,
            Integer($"{RopeTable}.m_RopeLength") ?? 0,
            Integer($"{RopeTable}.m_fLockedPoints") ?? 0,
            Integer($"{RopeTable}.m_RopeFlags") ?? 0,
            segments,
            (Integer($"{RopeTable}.m_bConstrainBetweenEndpoints") ?? 0) != 0,
            Integer($"{RopeTable}.m_Subdiv") ?? 255,
            Number($"{RopeTable}.m_TextureScale") ?? 4f,
            Number($"{RopeTable}.m_Width") ?? 0f,
            Number($"{RopeTable}.m_flScrollSpeed") ?? 0f);
    }

    /// <summary>
    /// The rope's material as a <c>modelprecache</c> index — <c>m_iRopeMaterialModelIndex</c>, which
    /// <c>C_RopeKeyframe::OnDataChanged</c> turns into the material it draws (`c_rope.cpp:1294-1309`).
    /// </summary>
    /// <returns>The index, or null for anything that is not a rope.</returns>
    public int? RopeMaterialIndex() => Integer($"{RopeTable}.m_iRopeMaterialModelIndex");

    /// <summary>An ACTIVE <c>point_camera</c>'s view, or null for an inactive one or anything that is not one (B511).</summary>
    /// <returns>What <c>DrawOneMonitor</c> reads off <c>C_PointCamera</c> (`viewrender.cpp:3168-3238`).</returns>
    /// <remarks>
    /// **The FOV identifies the class**: only <c>DT_PointCamera</c> sends <c>m_FOV</c> (`c_point_camera.cpp:18-29`).
    /// <c>m_bActive</c> is false in the client constructor, so an absent one is inactive. The fog is read as a fog
    /// controller's is — the colour a <c>color32</c> — and only when <c>m_bFogEnable</c>; otherwise the monitor
    /// draws under the view's own fog.
    /// </remarks>
    public ScenePointCamera? PointCamera()
    {
        string table = PointCameraFeed.Table;

        if (Number($"{table}.m_FOV") is not { } fov || Integer($"{table}.m_bActive") is not 1)
        {
            return null;
        }

        float start = Number($"{table}.m_flFogStart") ?? 0f;
        float end = Number($"{table}.m_flFogEnd") ?? 0f;
        uint packed = (uint)(Integer($"{table}.m_FogColor") ?? 0);

        // `end <= start` would divide by zero in the fog shader, the same guard the controller's read keeps.
        SceneFog? fog = Integer($"{table}.m_bFogEnable") is 1 && end > start
            ? new SceneFog(
                start, end, (packed & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, ((packed >> 16) & 0xFF) / 255f,
                Number($"{table}.m_flFogMaxDensity") ?? 1f, Integer($"{table}.m_bFogRadial") is 1)
            : null;

        return new ScenePointCamera(
            EntityIndex, Origin() ?? (0f, 0f, 0f), Angles() ?? (0f, 0f, 0f), fov,
            Integer($"{table}.m_bUseScreenAspectRatio") is 1, fog);
    }

    /// <summary><c>RecvProxy_Beam_ScrollSpeed</c>'s <c>val *= 0.1</c> (`beam_shared.cpp:90`).</summary>
    /// <remarks>A double, as the literal is: a float times <c>0.1</c> is promoted, multiplied and narrowed back.</remarks>
    internal const double ScrollSpeedReceived = 0.1;
}

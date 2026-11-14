using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// `CVoiceStatus::DrawHeadLabels` (game/shared/voice_status.cpp:205): a white quad above every speaking player's head, in
/// their team's speech-bubble material (B549).
/// </summary>
/// <remarks>
/// Drawn by `CViewRender::RenderPlayerSprites` (viewrender.cpp:1916, :2140), once, for the main view, after the whole scene
/// and `DisableFog`, before the viewmodels. For each player speaking, in the entity list and not `IsPlayerDead()`
/// (`pl.deadflag`), one quad at `WorldSpaceCenter() + (0, 0, g_flHeadOffset)` — 35 (:190) — eight either side
/// (`g_flHeadIconSize`, :191), facing the view's right with its tilt flattened out, never edge-on. The gate is
/// <see cref="ShouldDraw"/>.
///
/// **One batch per material rather than one draw per player**, as every sprite path here batches: two labels of
/// different teams overlapping on screen blend in material order where the engine blends them in player order.
/// </remarks>
public sealed class HeadLabels
{
    /// <summary>`pszHeadLabelNames[TF_PLAYER_HEAD_LABEL_RED]` (c_tf_player.cpp:269).</summary>
    public const string RedMaterial = "effects/speech_voice_red.vmt";

    /// <summary>`pszHeadLabelNames[TF_PLAYER_HEAD_LABEL_BLUE]` (c_tf_player.cpp:270): any team but RED's.</summary>
    public const string BlueMaterial = "effects/speech_voice_blue.vmt";

    /// <summary>`g_flHeadOffset`: "20 units above his head", as the comment says; the value is 35.</summary>
    public const float HeadOffset = 35f;

    /// <summary>`g_flHeadIconSize`: the quad's half-size.</summary>
    public const float HeadIconSize = 8f;

    private const int TeamRed = 2;

    /// <summary>`fabs( vRight.z ) &gt; 0.95`: "don't draw it edge-on".</summary>
    private const float EdgeOn = 0.95f;

    /// <summary>The quad's edges about its centre, one unit each way, scaled by <see cref="HeadIconSize"/>.</summary>
    private static readonly SpriteExtents Square = new(-1f, 1f, 1f, -1f);

    private readonly List<DetailSpriteVertex> _red = [];
    private readonly List<DetailSpriteVertex> _blue = [];
    private readonly List<ParticleBatch> _batches = [];

    /// <summary>
    /// `C_TFGameRules::ShouldDrawHeadLabels` (tf_gamerules.cpp:3670): never in tournament mode off a match server — a demo
    /// is played on none — else `CMultiplayRules`' `mp_show_voice_icons` (multiplay_gamerules.h:104).
    /// </summary>
    /// <param name="tournament">`mp_tournament`.</param>
    /// <param name="showVoiceIcons">`mp_show_voice_icons`, replicated, default 1 (multiplay_gamerules.cpp:94).</param>
    /// <returns>Whether head labels draw at all.</returns>
    public static bool ShouldDraw(bool tournament, bool showVoiceIcons) => !tournament && showVoiceIcons;

    /// <summary>A frame's labels behind <see cref="ShouldDraw"/>, its two ConVars read by name: what the frame presenter asks.</summary>
    /// <param name="convars">The server's replicated ConVars, `mp_tournament` and `mp_show_voice_icons` among them.</param>
    /// <param name="speaking">Who is speaking, or null for nobody.</param>
    /// <param name="players">The players in the entity list where the frame drew them, or null with no moment shown.</param>
    /// <param name="viewRight">The main view's right.</param>
    /// <param name="materials">The map's sprite materials, or null with no map.</param>
    /// <returns>A batch per material with a label, empty when none draws.</returns>
    public IReadOnlyList<ParticleBatch> For(
        Hud.HudConVars convars,
        IReadOnlySet<int>? speaking,
        IReadOnlyList<ScenePlayer>? players,
        Vector3 viewRight,
        IReadOnlyDictionary<string, EngineSprite>? materials)
    {
        if (players is null || materials is null ||
            !ShouldDraw(convars.GetBool("mp_tournament"), convars.GetBool("mp_show_voice_icons")))
        {
            return Build(null, [], viewRight, materials ?? NoMaterials);
        }

        return Build(speaking, players, viewRight, materials);
    }

    private static readonly IReadOnlyDictionary<string, EngineSprite> NoMaterials = new Dictionary<string, EngineSprite>();

    /// <summary>This frame's labels.</summary>
    /// <param name="speaking">Who is speaking (`m_VoicePlayers`), or null for nobody.</param>
    /// <param name="players">The players in the entity list, where the frame drew them.</param>
    /// <param name="viewRight">`CurrentViewRight()`: the main view's right.</param>
    /// <param name="materials">The two materials, keyed by <see cref="RedMaterial"/> and <see cref="BlueMaterial"/>.</param>
    /// <returns>A batch per material with a label, empty when none draws.</returns>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlySet<int>? speaking,
        IReadOnlyList<ScenePlayer> players,
        Vector3 viewRight,
        IReadOnlyDictionary<string, EngineSprite> materials)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(materials);

        _red.Clear();
        _blue.Clear();
        _batches.Clear();

        // The view is the same for every label, so an edge-on view draws none of them.
        if (speaking is not { Count: > 0 } speakers || MathF.Abs(viewRight.Z) > EdgeOn)
        {
            return _batches;
        }

        Vector3 right = Vector3.Normalize(viewRight with { Z = 0f });
        Vector3 up = Vector3.UnitZ;

        foreach (ScenePlayer player in players)
        {
            if (!speakers.Contains(player.EntityIndex) || player.DeadFlag)
            {
                continue;
            }

            Vector3 centre = new(player.X, player.Y, player.Z + PlayerHull.CenterHeight(player.Flags) + HeadOffset);

            EntitySprites.Corners(centre, right, up, Square, HeadIconSize, Vector3.One, 1f, player.Team == TeamRed ? _red : _blue);
        }

        Add(RedMaterial, _red, materials);
        Add(BlueMaterial, _blue, materials);

        return _batches;
    }

    /// <summary>One material's batch, drawn by its own text: `$translucent`, the colour its shader makes of white.</summary>
    private void Add(string material, List<DetailSpriteVertex> corners, IReadOnlyDictionary<string, EngineSprite> materials)
    {
        if (corners.Count == 0 || !materials.TryGetValue(material, out EngineSprite sprite) ||
            EntitySprites.PassesFor(sprite, RenderModes.Normal) is not [SpritePass pass, ..])
        {
            return;
        }

        (Vector3 colour, float alpha) = EntitySprites.ShaderInput(sprite, RenderModes.Normal, Vector3.One, 1f);

        if (colour != Vector3.One || alpha < 1f)
        {
            for (int i = 0; i < corners.Count; i++)
            {
                corners[i] = corners[i] with { Red = colour.X, Green = colour.Y, Blue = colour.Z, Alpha = alpha };
            }
        }

        _batches.Add(new ParticleBatch(corners, sprite.Material with { Blend = pass.Blend, Depth = pass.Depth }));
    }
}

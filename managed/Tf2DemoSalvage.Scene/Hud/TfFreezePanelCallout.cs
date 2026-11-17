using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>What the freeze panel's callouts point at (B752): `UpdateCallout`'s two inputs (tf_hud_freezepanel.cpp:757-758).</summary>
/// <param name="Gibs">
/// The local player's `GetSpawnedGibs()`, each piece's place in that list and its world mass centre; a piece already
/// released is left out, its place kept.
/// </param>
/// <param name="Ragdoll">`GetRepresentativeRagdoll()->GetRagdollOrigin()`, or null with no simulated ragdoll.</param>
/// <param name="OpaqueTrace">
/// `UTIL_TraceLine( from, to, MASK_OPAQUE, NULL, … )`'s fraction, 1 when clear; null where nothing traces, which answers
/// clear.
/// </param>
public sealed record FreezeCalloutSources(
    IReadOnlyList<(int Index, Vector3 Origin)> Gibs,
    Vector3? Ragdoll,
    Func<Vector3, Vector3, float>? OpaqueTrace)
{
    /// <summary>The sources for a frame: the local player's spawned gibs and simulated ragdoll, in the freeze cam only.</summary>
    /// <param name="state">The HUD's state.</param>
    /// <param name="tick">The HUD's tick.</param>
    /// <param name="corpses">The recording's corpses, or null.</param>
    /// <param name="massCentres">The simulated gibs' mass centres (<c>CorpsePhysics.MassCentres</c>).</param>
    /// <param name="roots">The simulated corpses' roots (<c>CorpsePhysics.Roots</c>).</param>
    /// <param name="level">The map, or null; with none nothing traces.</param>
    /// <param name="doors">The brush entities, or null.</param>
    /// <returns>The sources, or null outside the freeze cam or with neither gibs nor a ragdoll.</returns>
    /// <remarks>
    /// *Interpolated:* the line-of-sight trace meets the map — brushes, terrain, static props and the brush entities standing
    /// — and not the players or other entities `UTIL_TraceLine` with no filter would.
    /// </remarks>
    public static FreezeCalloutSources? Of(
        HudState state,
        int tick,
        IReadOnlyList<Core.Scene.SceneRagdoll>? corpses,
        IReadOnlyDictionary<int, Vector3> massCentres,
        IReadOnlyDictionary<int, Vector3> roots,
        MapLevel? level,
        SolidBrushEntities? doors)
    {
        if (!state.HasLocalPlayer || state.ObserverMode != Core.Scene.ObserverModes.FreezeCam || corpses is null)
        {
            return null;
        }

        IReadOnlyList<(int Index, Vector3 Origin)> gibs = ObserverRagdolls.SpawnedGibs(corpses, state.LocalIndex, tick, massCentres);
        Vector3? ragdoll = ObserverRagdolls.Of(corpses, roots, state.LocalIndex, tick)?.Root;

        if (gibs.Count == 0 && ragdoll is null)
        {
            return null;
        }

        Func<Vector3, Vector3, float>? trace = null;

        if (level is not null)
        {
            IReadOnlyList<SolidBrush> standing = doors?.At(tick) ?? [];
            trace = (from, to) => level.Trace((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), (0f, 0f, 0f), MapLevel.MaskOpaque, standing).Fraction;
        }

        return new FreezeCalloutSources(gibs, ragdoll, trace);
    }
}

/// <summary>
/// `CTFFreezePanelCallout` (tf_hud_freezepanel.cpp:1257-1380): an arrow and a label over one of the recorder's own gibs or
/// his ragdoll during the freeze cam (B752).
/// </summary>
/// <remarks>
/// **TF2's stock `.res` draws nothing**: its three controls are `"visible" "0"` and `"wide" "0"`, so the callouts are made and
/// placed and show only under a custom HUD that turns them on.
/// </remarks>
public sealed class TfFreezePanelCallout : VguiEditablePanel
{
    /// <summary>`LoadControlSettings( "resource/UI/FreezePanelCallout.res" )` (:1269).</summary>
    public const string ResFile = "resource/UI/FreezePanelCallout.res";

    /// <summary>`pszCalloutGibNames` (:1274-1281): the first four places named, the fifth a random organ.</summary>
    private static readonly string?[] GibNames = ["#Callout_Head", "#Callout_Foot", "#Callout_Hand", "#Callout_Torso", null];

    /// <summary>`pszCalloutRandomGibNames` (:1282-1289).</summary>
    private static readonly string[] RandomGibNames =
        ["#Callout_Organ2", "#Callout_Organ3", "#Callout_Organ4", "#Callout_Organ5", "#Callout_Organ6"];

    /// <summary>`new CTFFreezePanelCallout( g_pClientMode->GetViewport(), "FreezePanelCallout" )` (:731).</summary>
    /// <param name="viewport">The viewport.</param>
    public TfFreezePanelCallout(VguiPanel viewport)
        : base(viewport, "FreezePanelCallout")
    {
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFFreezePanelCallout";

    /// <summary>`m_pGibLabel`: the `.res`'s `CalloutLabel`, or null without one.</summary>
    public VguiLabel? Label { get; private set; }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:1265): the base, the `.res`, and the label it keeps.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings(ResFile, context);

        Label = FindChildByName("CalloutLabel") as VguiLabel;
    }

    /// <summary>`UpdateForGib` (:1294): the label by the gib's place, then widened to its text.</summary>
    /// <param name="gib">The gib's place in the spawned list.</param>
    /// <param name="count">How many callouts the gibs before it placed.</param>
    /// <param name="random">
    /// `RandomInt`'s stream. *Interpolated:* the engine draws from the client's global stream, which every other caller
    /// moves, so which organ is named cannot be recovered.
    /// </param>
    public void UpdateForGib(int gib, int count, UniformRandomStream random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (Label is not { } label)
        {
            return;
        }

        string text = gib < GibNames.Length
            ? GibNames[gib] ?? RandomGibNames[random.RandomInt(0, RandomGibNames.Length - 1)]
            : count switch
            {
                > 1 => "#FreezePanel_Callout3",
                1 => "#FreezePanel_Callout2",
                _ => "#FreezePanel_Callout",
            };

        label.SetText(text, Localize);
        Widen(label);
    }

    /// <summary>`UpdateForRagdoll` (:1374).</summary>
    public void UpdateForRagdoll() => Label?.SetText("#Callout_Ragdoll", Localize);

    /// <summary>"is the text wider than the label?" (:1327-1367): the label, its background and the callout grown, kept on screen.</summary>
    private void Widen(VguiLabel label)
    {
        (int wide, _) = label.GetContentSize();

        if (wide <= label.Wide)
        {
            return;
        }

        int diff = wide - label.Wide;
        label.SetSize(label.Wide + diff, label.Tall);

        if (FindChildByName("CalloutBG") is { } background)
        {
            background.SetSize(background.Wide + diff, background.Tall);
        }

        SetSize(Wide + diff, Tall);

        int screen = HudViewport.Of(this)?.Wide ?? 640;

        if (X + Wide > screen)
        {
            // "push ourselves to the left to fit on the screen", and the arrow right by as much.
            int push = X + Wide - screen;
            X -= push;

            if (FindChildByName("ArrowIcon") is { } arrow)
            {
                arrow.X += push;
            }
        }
    }

    /// <summary>`g_pVGuiLocalize->Find`, by token without its `#`.</summary>
    private string? Localize(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token);
}

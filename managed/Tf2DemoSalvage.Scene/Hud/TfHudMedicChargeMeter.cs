using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudMedicChargeMeter` (game/client/tf/tf_hud_mediccharge.cpp): a medic's ÜberCharge, or the Vaccinator's chunks.</summary>
/// <remarks>
/// Hidden by `HIDEHUD_MISCSTATUS` (:98). `ApplySchemeSettings` loads `resource/UI/HudMedicCharge.res` (:119). `OnTick`
/// (:175) reads the medigun — the active weapon, or the one owned while the bonesaw or an ÜberCharge syringe gun is out —
/// and sets the bar, or for `MEDIGUN_RESIST` the `weapon_medigun_resist_num_chunks` chunks; `HudMedicCharged` runs at a
/// full charge and `HudMedicChargedStop` once it is spent. **Not drawn:** the Vaccinator's `ResistIcon` image, whose resist
/// is read but whose icon path is left for when a Vaccinator demo is on hand.
/// </remarks>
public sealed class TfHudMedicChargeMeter : VguiEditablePanel, IHudElement
{
    private const int TfClassMedic = 5;
    private const int MedigunResist = 3;
    private const string Medigun = "CWeaponMedigun";

    // `weapon_medigun_resist_num_chunks` (tf_weapon_medigun.cpp), FCVAR_CHEAT, default 4.
    private const int ResistChunks = 4;

    private readonly VguiContinuousProgressBar _chargeMeter;
    private readonly List<VguiContinuousProgressBar> _chargeMeters = [];
    private VguiLabel? _uberchargeLabel;
    private VguiLabel? _uberchargeCountLabel;
    private bool _charged;
    private float _lastChargeValue = -1f;
    private int _lastKnownMedigunType;

    /// <summary>`CHudMedicChargeMeter( pElementName )` (:82): the bar and its chunks made before the `.res` places them.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudMedicChargeMeter(VguiPanel viewport)
        : base(viewport, "HudMedicCharge")
    {
        _chargeMeter = new VguiContinuousProgressBar(this, "ChargeMeter");

        for (int chunk = 0; chunk < ResistChunks; chunk++)
        {
            _chargeMeters.Add(new VguiContinuousProgressBar(this, string.Create(CultureInfo.InvariantCulture, $"ChargeMeter{chunk + 1}")));
        }

        SetDialogVariable("charge", 0);
        SetDialogVariable("charge_count", 0);
    }

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus;

    /// <summary>`ShouldDraw` (:147): a living medic holding the medigun, the bonesaw, or a syringe gun that costs ÜberCharge.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state)
    {
        if (!state.HasLocalPlayer || state.PlayerClass != TfClassMedic || !state.Alive
            || state.Player(state.LocalIndex) is not { } player || Active(player) is not { } weapon)
        {
            return false;
        }

        bool shown = weapon.ClassName is Medigun or "CTFBonesaw"
            || (weapon.ClassName == "CTFSyringeGun" && UberChargeAmmoPerShot(player, weapon) > 0f);

        return shown && !HudVisibility.IsHidden(state, HiddenBits);
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        LoadControlSettings("resource/UI/HudMedicCharge.res", context);

        _uberchargeLabel = FindChildByName("ChargeLabel") as VguiLabel;
        _uberchargeCountLabel = FindChildByName("IndividualChargesLabel") as VguiLabel;

        if (_uberchargeCountLabel is { } count)
        {
            count.Visible = false;
        }

        foreach (VguiContinuousProgressBar chunk in _chargeMeters)
        {
            chunk.Visible = false;
        }

        if (HudViewport.Of(this) is { } viewport)
        {
            UpdateKnownChargeType(viewport.State, viewport, force: true);
        }

        base.ApplySchemeSettings(context);
    }

    /// <summary>`OnTick`'s body (:175), given the state it reads.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="viewport">The viewport, for the attribute hooks and the animation controller.</param>
    public void Think(HudState state, HudViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        if (!state.HasLocalPlayer || state.Player(state.LocalIndex) is not { } player)
        {
            return;
        }

        UpdateKnownChargeType(state, viewport, force: false);

        SceneItem? weapon = Active(player);

        if (weapon is { ClassName: "CTFBonesaw" } || (weapon is { ClassName: "CTFSyringeGun" } && UberChargeAmmoPerShot(player, weapon) > 0f))
        {
            weapon = Owned(player, Medigun);
        }

        if (weapon is not { ClassName: Medigun } medigun)
        {
            return;
        }

        float charge = medigun.ChargeLevel;

        // `flCharge != m_flLastChargeValue`, a float compared by value.
        if (BitConverter.SingleToInt32Bits(charge) == BitConverter.SingleToInt32Bits(_lastChargeValue))
        {
            return;
        }

        if (MedigunType(player, medigun, viewport) == MedigunResist)
        {
            float chunkSize = 1f / ResistChunks;

            for (int chunk = 0; chunk < ResistChunks; chunk++)
            {
                float chunkCharge = charge - (chunkSize * chunk);
                float progress = MathF.Min(chunkCharge, chunkSize) / chunkSize;

                // "Not-full bars are dimmed a bit" (:239).
                _chargeMeters[chunk].FgColor = progress < 1f && !medigun.ChargeRelease ? ((byte)190, (byte)190, (byte)190, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255);
                _chargeMeters[chunk].SetProgress(progress);
            }

            SetDialogVariable("charge_count", (int)MathF.Floor(charge / chunkSize));
        }
        else
        {
            _chargeMeter.SetProgress(charge);
            SetDialogVariable("charge", (int)(charge * 100));
        }

        if (!_charged)
        {
            if (charge >= 1.0f)
            {
                viewport.Animations.StartAnimationSequence(this, "HudMedicCharged");
                _charged = true;
            }
        }
        else if (!medigun.ChargeRelease)
        {
            viewport.Animations.StartAnimationSequence(this, "HudMedicChargedStop");
            _charged = false;
        }

        _lastChargeValue = charge;
    }

    /// <inheritdoc/>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is { } viewport)
        {
            Think(viewport.State, viewport);
        }
    }

    /// <summary>`UpdateKnownChargeType` and `UpdateControlVisibility` (:284-345): the chunks for the Vaccinator, else the bar.</summary>
    private void UpdateKnownChargeType(HudState state, HudViewport viewport, bool force)
    {
        if (!state.HasLocalPlayer || state.Player(state.LocalIndex) is not { } player || Owned(player, Medigun) is not { } medigun)
        {
            return;
        }

        int type = MedigunType(player, medigun, viewport);

        if (type == _lastKnownMedigunType && !force)
        {
            return;
        }

        _lastKnownMedigunType = type;
        bool resist = type == MedigunResist;

        foreach (VguiContinuousProgressBar chunk in _chargeMeters)
        {
            chunk.Visible = resist;
        }

        _chargeMeter.Visible = !resist;

        if (_uberchargeLabel is { } label)
        {
            label.Visible = !resist;
        }

        if (_uberchargeCountLabel is { } count)
        {
            count.Visible = resist;
        }
    }

    /// <summary>`GetMedigunType`: `CALL_ATTRIB_HOOK_INT( iMode, set_weapon_mode )` from 0 (tf_weapon_medigun.cpp:781).</summary>
    private static int MedigunType(ScenePlayer player, SceneItem medigun, HudViewport viewport) =>
        viewport.WeaponAttribute is { } hook ? AttributeHooks.RoundFloatToInt(hook(player, medigun, "set_weapon_mode", 0f)) : 0;

    /// <summary>`UberChargeAmmoPerShot`: `ubercharge_ammo` × 0.01.</summary>
    private float UberChargeAmmoPerShot(ScenePlayer player, SceneItem weapon) =>
        HudViewport.Of(this)?.WeaponAttribute is { } hook ? hook(player, weapon, "ubercharge_ammo", 0f) * 0.01f : 0f;

    private static SceneItem? Active(ScenePlayer player)
    {
        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.EntityIndex == player.ActiveWeapon)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`Weapon_OwnsThisID`: the first carried weapon of that class.</summary>
    private static SceneItem? Owned(ScenePlayer player, string className)
    {
        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.IsWeapon && item.ClassName == className)
            {
                return item;
            }
        }

        return null;
    }
}

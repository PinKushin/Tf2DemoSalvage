namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudWeaponAmmo` (game/client/tf/tf_hud_ammostatus.cpp): the active weapon's clip and reserve.</summary>
/// <remarks>
/// Hidden by `HIDEHUD_HEALTH | HIDEHUD_PLAYERDEAD`, and by <see cref="TfAmmoState.Shown"/> — `ShouldDraw`'s own tests.
/// `ApplySchemeSettings` loads `resource/UI/HudAmmoWeapons.res` and keeps the low-ammo image's bounds. `OnThink` every
/// 0.1 s: with no usable weapon every count is hidden; otherwise a clip weapon shows `Ammo` (the clip) and
/// `AmmoInReserve`, and a clipless one shows `Ammo` alone (the reserve), each set only when a number or the weapon
/// changed. The low-ammo image shows, red, when clip plus reserve falls under round(40% of the most that can be held),
/// grown by `(threshold − total) / threshold × 5` each way — `hud_lowammowarning_threshold` and `…maxposadjust`, both
/// `FCVAR_DEVELOPMENTONLY` and so fixed. **Not modelled yet:** the `HudLowAmmoPulse` animation it starts.
/// </remarks>
public sealed class TfHudWeaponAmmo : VguiEditablePanel, IHudElement
{
    private const float WarningThreshold = 0.40f;
    private const float WarningMaxPosAdjust = 5f;

    private VguiLabel? _inClip;
    private VguiLabel? _inClipShadow;
    private VguiLabel? _inReserve;
    private VguiLabel? _inReserveShadow;
    private VguiLabel? _noClip;
    private VguiLabel? _noClipShadow;
    private VguiImagePanel? _lowAmmoImage;
    private (int X, int Y, int Wide, int Tall) _lowAmmoOrigin;
    private float _nextThink;
    private float _lastCurTime;
    private int _ammo = -1;
    private int _ammo2 = -1;
    private int _weaponSeen;

    /// <summary>`CTFHudWeaponAmmo( pElementName )`, parented to the viewport.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudWeaponAmmo(VguiPanel viewport)
        : base(viewport, "HudWeaponAmmo")
    {
    }

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideHealth | HudVisibility.HidePlayerDead;

    /// <inheritdoc/>
    public bool ShouldDraw(HudState state) => state.Ammo.Shown && !HudVisibility.IsHidden(state, HiddenBits);

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/HudAmmoWeapons.res", context);

        _inClip = FindChildByName("AmmoInClip") as VguiLabel;
        _inClipShadow = FindChildByName("AmmoInClipShadow") as VguiLabel;
        _inReserve = FindChildByName("AmmoInReserve") as VguiLabel;
        _inReserveShadow = FindChildByName("AmmoInReserveShadow") as VguiLabel;
        _noClip = FindChildByName("AmmoNoClip") as VguiLabel;
        _noClipShadow = FindChildByName("AmmoNoClipShadow") as VguiLabel;
        _lowAmmoImage = FindChildByName("HudWeaponLowAmmoImage") as VguiImagePanel;

        if (_lowAmmoImage is { } image)
        {
            _lowAmmoOrigin = (image.X, image.Y, image.Wide, image.Tall);
        }

        _ammo = -1;
        _ammo2 = -1;
        _weaponSeen = 0;
        _nextThink = 0f;
        UpdateAmmoLabels(primary: false, reserve: false, noClip: false);
    }

    /// <summary>`OnThink`'s body, given the state it reads.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="weapon">The active weapon's entity slot, so a switch counts as a change.</param>
    public void Think(HudState state, int weapon)
    {
        if (state.CurTime < _lastCurTime)
        {
            _nextThink = state.CurTime + 0.05f;
        }

        _lastCurTime = state.CurTime;

        if (_nextThink >= state.CurTime)
        {
            return;
        }

        TfAmmoState ammo = state.Ammo;

        if (!state.HasLocalPlayer || !ammo.HasWeapon || !ammo.UsesPrimaryAmmo)
        {
            UpdateAmmoLabels(primary: false, reserve: false, noClip: false);
            HideLowAmmoIndicator();
            _ammo = -1;
            _ammo2 = -1;
        }
        else
        {
            // "Clip ammo not used, get total ammo count" — else the second number is the reserve.
            (int ammo1, int ammo2) = ammo.Clip1 < 0 ? (ammo.Reserve, 0) : (ammo.Clip1, ammo.Reserve);

            if (_ammo != ammo1 || _ammo2 != ammo2 || _weaponSeen != weapon)
            {
                (_ammo, _ammo2, _weaponSeen) = (ammo1, ammo2, weapon);

                if (ammo.UsesClips)
                {
                    UpdateAmmoLabels(primary: true, reserve: true, noClip: false);
                    SetDialogVariable("Ammo", _ammo);
                    SetDialogVariable("AmmoInReserve", _ammo2);
                }
                else
                {
                    UpdateAmmoLabels(primary: false, reserve: false, noClip: true);
                    SetDialogVariable("Ammo", _ammo);
                }
            }

            int total = ammo1 + ammo2;
            int maxTotal = ammo.MaxAmmo + (ammo.MaxClip1 > 0 ? ammo.MaxClip1 : 0);
            float threshold = maxTotal * WarningThreshold;

            if (total < AttributeHooks.RoundFloatToInt(threshold))
            {
                ShowLowAmmoIndicator();
                SizeLowAmmoIndicator(total, threshold);
            }
            else
            {
                HideLowAmmoIndicator();
            }
        }

        _nextThink = state.CurTime + 0.1f;
    }

    /// <inheritdoc/>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is { } viewport)
        {
            Think(viewport.State, viewport.State.ActiveWeapon);
        }
    }

    private void UpdateAmmoLabels(bool primary, bool reserve, bool noClip)
    {
        Pair(_inClip, _inClipShadow, primary);
        Pair(_inReserve, _inReserveShadow, reserve);
        Pair(_noClip, _noClipShadow, noClip);
    }

    private static void Pair(VguiLabel? label, VguiLabel? shadow, bool visible)
    {
        if (label is null || shadow is null || label.Visible == visible)
        {
            return;
        }

        label.Visible = visible;
        shadow.Visible = visible;
    }

    private void ShowLowAmmoIndicator()
    {
        if (_lowAmmoImage is not { Visible: false } image)
        {
            return;
        }

        (image.X, image.Y, image.Wide, image.Tall) = _lowAmmoOrigin;
        image.Visible = true;
        image.FgColor = (255, 0, 0, 255);
    }

    private void SizeLowAmmoIndicator(float current, float max)
    {
        if (_lowAmmoImage is not { Visible: true } image)
        {
            return;
        }

        int adjust = AttributeHooks.RoundFloatToInt((max - current) / max * WarningMaxPosAdjust);

        (image.X, image.Y) = (_lowAmmoOrigin.X - adjust, _lowAmmoOrigin.Y - adjust);
        (image.Wide, image.Tall) = (_lowAmmoOrigin.Wide + (2 * adjust), _lowAmmoOrigin.Tall + (2 * adjust));
    }

    private void HideLowAmmoIndicator()
    {
        if (_lowAmmoImage is not { Visible: true } image)
        {
            return;
        }

        (image.X, image.Y, image.Wide, image.Tall) = _lowAmmoOrigin;
        image.Visible = false;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CItemEffectMeterManager` (tf_hud_itemeffectmeter.cpp:84-181): the local player's item effect meters, rebuilt for its
/// class and items.
/// </summary>
/// <remarks>
/// `SetPlayer` runs from `C_TFPlayer::OnPlayerClassChange` for the local player (c_tf_player.cpp:5257) and from this
/// manager's own events; it is also what starts the listening, so before the first class change no event reaches it.
/// `Update` runs from the local player's `ClientThink` (c_tf_player.cpp:6021), and `ClearExistingMeters` from its
/// destructor (:4072).
/// </remarks>
/// <param name="viewport">`g_pClientMode->GetViewport()`, which each meter is parented to.</param>
public sealed class TfItemEffectMeterManager(HudViewport viewport)
{
    private readonly List<TfHudItemEffectMeter> _meters = [];

    /// <summary>The events `SetPlayer` listens for (:127-129).</summary>
    public static IReadOnlySet<string> ListensFor { get; } =
        new HashSet<string>(["post_inventory_application", "localplayer_pickup_weapon", "localplayer_respawn"], StringComparer.Ordinal);

    /// <summary>Whether `SetPlayer` has run, so the events are listened for.</summary>
    public bool Listening { get; private set; }

    /// <summary>`m_Meters`, head first — `outMeters.AddToHead` puts the last made first.</summary>
    public IReadOnlyList<TfHudItemEffectMeter> Meters => _meters;

    /// <summary>The viewport the meters are parented to.</summary>
    public HudViewport Viewport { get; } = viewport ?? throw new ArgumentNullException(nameof(viewport));

    /// <summary>`C_TFPlayer::EmitSound` for the local player, which a refilled meter beeps through.</summary>
    public HudSoundEmitter? SoundEmitter { get; set; }

    /// <summary>`gViewPortInterface->FindPanelByName( PANEL_SCOREBOARD )->IsVisible()`, which the kart meter reads.</summary>
    public bool ScoreboardVisible { get; set; }

    /// <summary>`ClearExistingMeters` (:91): every meter removed from the HUD and deleted.</summary>
    public void ClearExistingMeters()
    {
        foreach (TfHudItemEffectMeter meter in _meters)
        {
            Viewport.RemoveHudElement(meter);
        }

        _meters.Clear();
    }

    /// <summary>`GetNumEnabled` (:105).</summary>
    /// <returns>How many meters are enabled.</returns>
    public int NumEnabled()
    {
        int count = 0;

        foreach (TfHudItemEffectMeter meter in _meters)
        {
            if (meter.IsEnabled())
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>`SetPlayer` (:123): listen, clear, and make the meters for the player's class.</summary>
    /// <param name="player">The player as it is now, or null for none.</param>
    public void SetPlayer(ScenePlayer? player)
    {
        Listening = true;
        ClearExistingMeters();

        if (player is { } made)
        {
            TfHudItemEffectMeter.CreateHudElementsForClass(this, made, _meters);
        }
    }

    /// <summary>`Update` (:142): each meter, with the player.</summary>
    /// <param name="player">The local player.</param>
    public void Update(ScenePlayer? player)
    {
        foreach (TfHudItemEffectMeter meter in _meters)
        {
            meter.Update(player);
        }
    }

    /// <summary>`FireGameEvent` (:157).</summary>
    /// <param name="gameEvent">The event.</param>
    /// <param name="state">The state it is handled in.</param>
    public void HandleGameEvent(SceneGameEvent gameEvent, HudState state)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        if (!Listening)
        {
            return;
        }

        bool needsUpdate = gameEvent.Name switch
        {
            "localplayer_pickup_weapon" or "localplayer_respawn" => true,

            // "Force a refresh. Our items may have changed causing us to now draw, etc." — the local player's only.
            "post_inventory_application" => state.HasLocalPlayer
                && gameEvent.Roster.TryGetValue(state.LocalIndex, out Core.Net.PlayerInfo info)
                && info.UserId == gameEvent.GetInt("userid"),
            _ => false,
        };

        if (needsUpdate)
        {
            SetPlayer(state.HasLocalPlayer ? state.Player(state.LocalIndex) : null);
        }
    }
}

/// <summary>`CHudItemEffectMeter` (tf_hud_itemeffectmeter.cpp:187-554): one meter, by default the spy's cloak.</summary>
/// <remarks>
/// Each meter holds `m_pPlayer`, the player it was made for, by entity index — a handle that reads nothing once the
/// entity is gone. Its label, icon, progress bars and beep all come from the virtuals below, which the weapon meters
/// (<see cref="TfItemEffectMeterWeapon"/>) and the rune and attribute meters override as Valve's specialisations do.
/// </remarks>
public class TfHudItemEffectMeter : VguiEditablePanel, IHudElement
{
    /// <summary>`TF_CLASS_SPY`.</summary>
    protected const int ClassSpy = 8;

    private const int ClassMedic = 5;
    private const string ElementName = "HudItemEffectMeter";

    // `HIDEHUD_CLOAK_AND_FEIGN` (shareddefs.h:221).
    private const int HideCloakAndFeign = 1 << 13;

    private static readonly IReadOnlyList<string> Groups = ["global", "inspect_panel"];

    private readonly VguiLabel _label;
    private readonly List<VguiContinuousProgressBar> _progressBars = [];
    private float _oldProgress = 1f;
    private int _state = -1;
    private TfImagePanel? _itemEffectIcon;

    /// <summary>`CHudItemEffectMeter( pszElementName, pPlayer )` (:187): parented to the viewport, with its label.</summary>
    /// <param name="manager">`g_ItemEffectMeterManager`, which the layout counts enabled meters through.</param>
    /// <param name="playerIndex">`m_pPlayer`'s entity index.</param>
    public TfHudItemEffectMeter(TfItemEffectMeterManager manager, int playerIndex)
        : base(ViewportOf(manager), ElementName)
    {
        Manager = manager;
        PlayerIndex = playerIndex;
        _label = new VguiLabel(this, "ItemEffectMeterLabel");
        DeclareAnimationVar("x_offset", VguiPanelVarType.ProportionalFloat, "0");
    }

    private static HudViewport ViewportOf(TfItemEffectMeterManager manager) =>
        (manager ?? throw new ArgumentNullException(nameof(manager))).Viewport;

    /// <inheritdoc/>
    public override string ClassName => ElementName;

    /// <summary>`m_iHiddenBits`: `HIDEHUD_MISCSTATUS | HIDEHUD_CLOAK_AND_FEIGN` (:195).</summary>
    public int HiddenBits => HudVisibility.HideMiscStatus | HideCloakAndFeign;

    /// <summary>"global", and "inspect_panel" (:203).</summary>
    public IReadOnlyList<string> RenderGroups => Groups;

    /// <summary>`m_pLabel`.</summary>
    public VguiLabel Label => _label;

    /// <summary>`m_vecProgressBars`.</summary>
    public IReadOnlyList<VguiContinuousProgressBar> ProgressBars => _progressBars;

    /// <summary>`m_pItemEffectIcon`, or null when the `.res` makes none.</summary>
    public TfImagePanel? ItemEffectIcon => _itemEffectIcon;

    /// <summary>The manager.</summary>
    protected TfItemEffectMeterManager Manager { get; }

    /// <summary>`m_pPlayer` as an entity index.</summary>
    protected int PlayerIndex { get; }

    /// <summary>`m_bEnabled`.</summary>
    protected bool MeterEnabled { get; set; } = true;

    /// <summary>This frame's state.</summary>
    protected HudState State => Manager.Viewport.State;

    /// <summary>`m_pPlayer`: null once the entity is gone.</summary>
    protected ScenePlayer? Player => PlayerIndex != 0 ? State.Player(PlayerIndex) : null;

    /// <summary>`C_TFPlayer::GetLocalTFPlayer()`.</summary>
    protected ScenePlayer? LocalPlayer => State.HasLocalPlayer ? State.Player(State.LocalIndex) : null;

    /// <summary>`CreateHudElementsForClass` (:220): the meters for the player's class, then every class's.</summary>
    /// <param name="manager">The manager.</param>
    /// <param name="player">The player.</param>
    /// <param name="outMeters">`outMeters`, each new meter added at its head.</param>
    internal static void CreateHudElementsForClass(TfItemEffectMeterManager manager, ScenePlayer player, List<TfHudItemEffectMeter> outMeters)
    {
        int index = player.EntityIndex;

        void Add(TfHudItemEffectMeter meter)
        {
            outMeters.Insert(0, meter);
            meter.Visible = false;
        }

        void Weapon(TfItemEffectMeterWeapon meter) => Add(meter);

        switch (player.PlayerClass)
        {
            case 1:
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_BAT_WOOD", ["CTFBat_Wood"], true, null));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_BAT_GIFTWRAP", ["CTFBat_Giftwrap"], true, null));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_LUNCHBOX", ["CTFLunchBox_Drink"], true, "resource/UI/HudItemEffectMeter_Scout.res"));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_JAR_MILK", ["CTFJarMilk"], true, "resource/UI/HudItemEffectMeter_Scout.res"));
                Weapon(new TfItemEffectMeterSodaPopper(manager, index));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_PEP_BRAWLER_BLASTER", ["CTFPEPBrawlerBlaster"], true, "resource/UI/HudItemEffectMeter_SodaPopper.res"));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_CLEAVER", ["CTFCleaver"], true, "resource/UI/HudItemEffectMeter_Cleaver.res"));
                break;

            case 6:
                AddItemAttributeMeter(manager, player, "tf_weapon_lunchbox", true, Add);
                Weapon(new TfItemEffectMeterMinigun(manager, index));
                break;

            case 2:
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_JAR", ["CTFJar"], true, null));
                Weapon(new TfItemEffectMeterSniperRifleDecap(manager, index));
                Weapon(new TfItemEffectMeterSniperRifle(manager, index));
                Weapon(new TfItemEffectMeterChargedSmg(manager, index));
                AddItemAttributeMeter(manager, player, "tf_wearable_razorback", true, Add);
                break;

            case 4:
                Weapon(new TfItemEffectMeterSword(manager, index));
                break;

            case 3:
                Weapon(new TfItemEffectMeterBuffItem(manager, index));
                Weapon(new TfItemEffectMeterParticleCannon(manager, index));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_RAYGUN", ["CTFRaygun"], false, "resource/UI/HUDItemEffectMeter_Raygun.res"));
                Weapon(new TfItemEffectMeterAirStrike(manager, index));
                break;

            case ClassSpy:
                Weapon(new TfItemEffectMeterKnife(manager, index));
                Add(new TfHudItemEffectMeter(manager, index));
                Weapon(new TfItemEffectMeterBuilder(manager, index));
                Weapon(new TfItemEffectMeterRevolver(manager, index));
                break;

            case 9:
                Weapon(new TfItemEffectMeterShotgunRevenge(manager, index));
                Weapon(new TfItemEffectMeterWeapon(manager, index, "TF_WEAPON_DRG_POMSON", ["CTFDRGPomson"], false, "resource/UI/HUDItemEffectMeter_Pomson.res"));
                Weapon(new TfItemEffectMeterRevolver(manager, index));
                break;

            case 7:
                Weapon(new TfItemEffectMeterFlameThrower(manager, index));
                Weapon(new TfItemEffectMeterFlareGunRevenge(manager, index));
                Weapon(new TfItemEffectMeterRocketPack(manager, index));
                AddItemAttributeMeter(manager, player, "tf_weapon_jar_gas", true, Add);
                AddItemAttributeMeter(manager, player, "tf_weapon_rocketlauncher_fireball", false, Add);
                break;

            case ClassMedic:
                Weapon(new TfItemEffectMeterMedigun(manager, index));
                Weapon(new TfItemEffectMeterBonesaw(manager, index));
                break;

            default:
                break;
        }

        // "ALL CLASS", the kill streak, the kart, the canteen and the rune (:344-379).
        Weapon(new TfItemEffectMeterThrowable(manager, index));
        Weapon(new TfItemEffectMeterKillStreak(manager, index));
        Weapon(new TfItemEffectMeterSpellBook(manager, index));
        Weapon(new TfItemEffectMeterPowerupBottle(manager, index));
        Add(new TfItemEffectMeterRune(manager, index));
    }

    /// <summary>
    /// `lambdaAddItemEffectMeter` (:228): the first charge-meter slot holding an item of that `item_class`; a meter for it
    /// when its `item_meter_charge_type` is set, labelled by its `meter_label` or the slot's default.
    /// </summary>
    private static void AddItemAttributeMeter(
        TfItemEffectMeterManager manager, ScenePlayer player, string itemClass, bool beep, Action<TfHudItemEffectMeter> add)
    {
        if (manager.Viewport.Items is not { } schema)
        {
            return;
        }

        // `FIRST_LOADOUT_SLOT_WITH_CHARGE_METER` to `LAST_…` (tf_item_constants.h:84-85): primary through `MISC2`.
        for (int slot = 0; slot <= Content.Assets.ItemSchema.LoadoutSlotMisc2; slot++)
        {
            if (TfItemAttributeEffectMeter.EntityForLoadoutSlot(player, slot, schema) is not { DefinitionIndex: { } definition } item
                || schema.ItemClass(definition) != itemClass)
            {
                continue;
            }

            // `FindAttribute( pItem, attrMeterType, &retval )`, then `retval == ATTRIBUTE_METER_TYPE_NONE`: both return.
            if (TfItemAttributeEffectMeter.FindAttribute(item, schema, "item_meter_charge_type") is not { } meterType
                || meterType.RawBits == 0)
            {
                return;
            }

            string label = schema.DefinitionStringAttribute(definition, "meter_label") is { } attribute
                ? attribute
                : DefaultMeterTextForLoadoutPosition(slot);

            add(new TfItemAttributeEffectMeter(manager, player, slot, label, beep));
            return;
        }
    }

    /// <summary>`GetDefaultMeterTextForLoadoutPosition` (:44).</summary>
    /// <param name="loadout">The slot.</param>
    /// <returns>The label.</returns>
    internal static string DefaultMeterTextForLoadoutPosition(int loadout) => loadout switch
    {
        0 => "#TF_PrimaryMeter",
        1 => "#TF_SecondaryMeter",
        2 => "#TF_MeleeMeter",
        _ => string.Empty,
    };

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:386): the `.res`, the label, the icon, and each bar by its name.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        LoadControlSettings(ResFile(), context);
        base.ApplySchemeSettings(context);
        SetLabelText();

        _itemEffectIcon = FindChildByName("ItemEffectIcon") as TfImagePanel;
        _itemEffectIcon?.SetImage(IconName());

        _progressBars.Clear();

        for (int bar = 0; bar < NumProgressBar(); bar++)
        {
            string name = bar == 0 ? "ItemEffectMeter" : string.Create(CultureInfo.InvariantCulture, $"ItemEffectMeter{bar + 1}");

            // A missing bar is Valve's `Warning( "%s missing ContinuousProgressBar field" )`, and no bar.
            if (FindChildByName(name) is VguiContinuousProgressBar progressBar)
            {
                _progressBars.Add(progressBar);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// `PerformLayout` (:426): the icon and label again, then — with more than one meter enabled, or one beside a medic's
    /// — slid left by `x_offset`. Each layout slides it again from where it is; only a scheme reload puts it back.
    /// </remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        _itemEffectIcon?.SetImage(IconName());
        SetLabelText();

        if (!ShouldAutoAdjustPosition())
        {
            return;
        }

        // "slide over by 1 for medic".
        int offset = LocalPlayer is { PlayerClass: ClassMedic } ? 1 : 0;

        if (Manager.NumEnabled() + offset > 1)
        {
            // `SetPos( xPos - m_iXOffset, yPos )`: an int less a float, truncated.
            X = (int)(X - GetFloat("x_offset"));
        }
    }

    /// <summary>`ShouldDraw` (:460): the local player alive and not a ghost, no minigame nor summary, and enabled.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether to draw.</returns>
    public virtual bool ShouldDraw(HudState state) => ApplyVisibility(BaseShouldDraw(state));

    /// <summary>`CHudItemEffectMeter::ShouldDraw`'s decision, before it shows or hides the panel.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether to draw.</returns>
    protected bool BaseShouldDraw(HudState state)
    {
        ScenePlayer? local = state.HasLocalPlayer ? state.Player(state.LocalIndex) : null;

        if (local is not { IsAlive: true } alive || alive.Conditions.Has(ConditionHalloweenGhostMode))
        {
            return false;
        }

        if (state.Rules.ActiveMinigame || state.Rules.ShowMatchSummary)
        {
            return false;
        }

        return IsEnabled() && HudVisibility.ShouldDraw(state, this);
    }

    /// <summary>`if ( IsVisible() != bShouldDraw )`: show or hide, and a meter coming into view reloads its scheme.</summary>
    /// <param name="shouldDraw">The decision.</param>
    /// <returns>The decision.</returns>
    protected bool ApplyVisibility(bool shouldDraw)
    {
        if (Visible != shouldDraw)
        {
            Visible = shouldDraw;

            if (shouldDraw)
            {
                // "if we're going to be visible, redo our layout": `InvalidateLayout( false, true )`.
                InvalidateLayout(reloadScheme: true);
            }
        }

        return shouldDraw;
    }

    /// <summary>`TF_COND_HALLOWEEN_GHOST_MODE` (tf_shareddefs.h).</summary>
    protected const int ConditionHalloweenGhostMode = 77;

    /// <summary>`Update` (:500): the count, the beep, each bar's fill and color, and a layout when the state changes.</summary>
    /// <param name="player">The local player.</param>
    public virtual void Update(ScenePlayer? player)
    {
        if (!IsEnabled() || player is not { } owner)
        {
            return;
        }

        // "Progress counts override progress bars."
        int count = Count();

        if (count >= 0)
        {
            if (ShowPercentSymbol())
            {
                SetDialogVariable("progresscount", string.Create(CultureInfo.InvariantCulture, $"{count}%"));
            }
            else
            {
                SetDialogVariable("progresscount", count);
            }
        }

        float progress = Progress();

        // "Play a sound if we are refreshed."
        if (LocalPlayer is not null && progress >= 1f && _oldProgress < 1f && owner.IsAlive && ShouldBeep())
        {
            _oldProgress = progress;
            Manager.SoundEmitter?.Invoke(BeepSound());
        }
        else
        {
            _oldProgress = progress;
        }

        float maxProgressPerBar = 1f / NumProgressBar();

        for (int bar = 0; bar < _progressBars.Count; bar++)
        {
            float current = Math.Min(progress - (maxProgressPerBar * bar), maxProgressPerBar);
            // `RemapValClamped( current, 0, max, 0, 1 )` (mathlib.h): `max` is never 0, so it is the clamped quotient.
            _progressBars[bar].SetProgress(Math.Clamp(current / maxProgressPerBar, 0f, 1f));

            if (ShouldFlash())
            {
                int colorOffset = (int)(State.RealTime * 10) % 10;
                _progressBars[bar].FgColor = ((byte)(160 + (colorOffset * 10)), 0, 0, 255);
            }
            else
            {
                _progressBars[bar].FgColor = ProgressBarColor();
            }
        }

        // "update these when state changes".
        int currentState = MeterState();

        if (_state != currentState)
        {
            InvalidateLayout();
            _state = currentState;
        }
    }

    /// <summary>`SetLabelText` (:574): the label's token localised, key bindings replaced; the raw text when not found.</summary>
    /// <param name="text">A label to use instead of <see cref="LabelText"/>, or null.</param>
    public void SetLabelText(string? text = null)
    {
        string label = string.IsNullOrEmpty(text) ? LabelText() : text;
        VguiContext? context = Manager.Viewport.Context;

        if (context?.Localize?.Invoke(label.StartsWith('#') ? label[1..] : label) is { } localized)
        {
            _label.SetText(TfKeyBindings.Replace(localized, State.KeyLookupBinding, context.Localize), null);
        }
        else
        {
            _label.SetText(label, context?.Localize);
        }

        _label.FgColor = LabelTextColor();
    }

    /// <summary>`IsEnabled`: `m_bEnabled`.</summary>
    /// <returns>Whether the meter is enabled.</returns>
    public virtual bool IsEnabled() => MeterEnabled;

    /// <summary>`GetLabelText` (:533): the feign death or dagger watch's, else "#TF_CLOAK".</summary>
    /// <returns>The label token.</returns>
    public virtual string LabelText() => CloakLabelText();

    /// <summary>`CHudItemEffectMeter::GetLabelText` itself, which a specialisation reaches past the template for.</summary>
    /// <returns>The label token.</returns>
    protected string CloakLabelText()
    {
        if (Player is { PlayerClass: ClassSpy } spy && Invis(spy) is { } watch)
        {
            switch (InvisType(spy, watch))
            {
                case 1:
                    return "#TF_Feign";
                case 2:
                    return "#TF_CloakDagger";
                default:
                    break;
            }
        }

        return "#TF_CLOAK";
    }

    /// <summary>`GetIconName`.</summary>
    /// <returns>The icon.</returns>
    public virtual string IconName() => "../hud/ico_stickybomb_red";

    /// <summary>`GetProgress` (:560): the cloak meter over 100, else full.</summary>
    /// <returns>The fraction.</returns>
    public virtual float Progress() => Player is { } player ? (player.CloakMeter ?? 0f) / 100.0f : 1f;

    /// <summary>`ShouldBeep`: a spy whose watch feigns death (tf_hud_itemeffectmeter.h:75).</summary>
    /// <returns>Whether a refill beeps.</returns>
    public virtual bool ShouldBeep() =>
        Player is { PlayerClass: ClassSpy } spy && Invis(spy) is { } watch && InvisType(spy, watch) == 1;

    /// <summary>`GetBeepSound`.</summary>
    /// <returns>The sound script.</returns>
    public virtual string BeepSound() => "TFPlayer.ReCharged";

    /// <summary>`GetResFile`.</summary>
    /// <returns>The `.res` file.</returns>
    public virtual string ResFile() => "resource/UI/HudItemEffectMeter.res";

    /// <summary>`GetCount`: -1 for none.</summary>
    /// <returns>The count.</returns>
    public virtual int Count() => -1;

    /// <summary>`ShouldFlash`.</summary>
    /// <returns>Whether the bars flash.</returns>
    public virtual bool ShouldFlash() => false;

    /// <summary>`ShowPercentSymbol`.</summary>
    /// <returns>Whether the count carries a `%`.</returns>
    public virtual bool ShowPercentSymbol() => false;

    /// <summary>`GetNumProgressBar`.</summary>
    /// <returns>The number of bars.</returns>
    public virtual int NumProgressBar() => 1;

    /// <summary>`GetProgressBarColor`.</summary>
    /// <returns>The color.</returns>
    public virtual (byte Red, byte Green, byte Blue, byte Alpha) ProgressBarColor() => (255, 255, 255, 255);

    /// <summary>`GetLabelTextColor`.</summary>
    /// <returns>The color.</returns>
    public virtual (byte Red, byte Green, byte Blue, byte Alpha) LabelTextColor() => (255, 255, 255, 255);

    /// <summary>`GetState`: -1 unless a meter lays out again on a change.</summary>
    /// <returns>The state.</returns>
    public virtual int MeterState() => -1;

    /// <summary>`IsKillstreakMeter`.</summary>
    /// <returns>Whether this is the kill streak meter.</returns>
    public virtual bool IsKillstreakMeter() => false;

    /// <summary>`ShouldAutoAdjustPosition`.</summary>
    /// <returns>Whether the layout slides the meter over.</returns>
    public virtual bool ShouldAutoAdjustPosition() => true;

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( player, value, name )`; the value unchanged with no schema.</summary>
    /// <param name="player">The player.</param>
    /// <param name="name">The attribute class.</param>
    /// <param name="value">The value.</param>
    /// <returns>The hooked value.</returns>
    protected float PlayerHook(ScenePlayer player, string name, float value) =>
        Manager.Viewport.PlayerAttribute?.Invoke(player, name, value) ?? value;

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT` on an item; the value unchanged with no schema.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="item">The item.</param>
    /// <param name="name">The attribute class.</param>
    /// <param name="value">The value.</param>
    /// <returns>The hooked value.</returns>
    protected float ItemHook(ScenePlayer player, SceneItem item, string name, float value) =>
        Manager.Viewport.WeaponAttribute?.Invoke(player, item, name, value) ?? value;

    /// <summary>`CALL_ATTRIB_HOOK_INT` on an item from 0.</summary>
    /// <param name="player">The owner.</param>
    /// <param name="item">The item.</param>
    /// <param name="name">The attribute class.</param>
    /// <returns>The hooked value.</returns>
    protected int ItemHookInt(ScenePlayer player, SceneItem item, string name) =>
        AttributeHooks.RoundFloatToInt(ItemHook(player, item, name, 0f));

    /// <summary>`CALL_ATTRIB_HOOK_INT_ON_OTHER( player, … )` from 0.</summary>
    /// <param name="player">The player.</param>
    /// <param name="name">The attribute class.</param>
    /// <returns>The hooked value.</returns>
    protected int PlayerHookInt(ScenePlayer player, string name) => AttributeHooks.RoundFloatToInt(PlayerHook(player, name, 0f));

    /// <summary>`Weapon_OwnsThisID( TF_WEAPON_INVIS )`: the first `CTFWeaponInvis` among the weapons.</summary>
    private static SceneItem? Invis(ScenePlayer player)
    {
        foreach (SceneItem item in player.Items ?? [])
        {
            if (item.IsWeapon && item.ClassName == "CTFWeaponInvis")
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`GetInvisType`: `set_weapon_mode` (tf_weapon_invis.h:66) — 1 feigns death, 2 is the motion cloak.</summary>
    private int InvisType(ScenePlayer player, SceneItem watch) => ItemHookInt(player, watch, "set_weapon_mode");
}

/// <summary>`CHudItemEffectMeter_Rune` (:1497-1531): the Supernova rune's charge.</summary>
/// <param name="manager">The manager.</param>
/// <param name="playerIndex">`m_pPlayer`.</param>
public sealed class TfItemEffectMeterRune(TfItemEffectMeterManager manager, int playerIndex) : TfHudItemEffectMeter(manager, playerIndex)
{
    // `TF_COND_RUNE_SUPERNOVA` (tf_shareddefs.h:801): `CanRuneCharge` (tf_player_shared.cpp:7823).
    private const int ConditionRuneSupernova = 111;

    /// <inheritdoc/>
    public override bool IsEnabled() => Player is { } player && player.Conditions.Has(ConditionRuneSupernova);

    /// <inheritdoc/>
    public override float Progress() => Player is { } player ? (player.RuneCharge ?? 0f) / 100.0f : 0f;

    /// <inheritdoc/>
    /// <remarks>`IsRuneCharged`: `m_flRuneCharge == 100.f` (tf_player_shared.h:475).</remarks>
    public override bool ShouldFlash() => Player is { RuneCharge: { } charge } && charge >= 100f && charge <= 100f;

    /// <inheritdoc/>
    /// <remarks>Only the rune: the base meter's refusals are not asked.</remarks>
    public override bool ShouldDraw(HudState state) => IsEnabled();

    /// <inheritdoc/>
    public override string LabelText() => "Powerup";

    /// <inheritdoc/>
    public override string ResFile() => "resource/UI/HudPowerupEffectMeter.res";
}

/// <summary>
/// `CHudItemEffectMeter_ItemAttribute` (:1636-1711): the charge meter of whatever item sits in a loadout slot, for items
/// whose `item_meter_charge_type` asks for one.
/// </summary>
public sealed class TfItemAttributeEffectMeter : TfHudItemEffectMeter
{
    private readonly int _loadoutSlot;
    private readonly string _labelText;
    private readonly bool _beeps;
    private int? _entity;

    /// <summary>The constructor (:1637): the slot's entity, or disabled when there is none.</summary>
    /// <param name="manager">The manager.</param>
    /// <param name="player">`pPlayer`, as it is when the meter is made.</param>
    /// <param name="loadoutSlot">The slot.</param>
    /// <param name="labelText">The label.</param>
    /// <param name="beeps">Whether a refill beeps.</param>
    public TfItemAttributeEffectMeter(TfItemEffectMeterManager manager, ScenePlayer player, int loadoutSlot, string labelText, bool beeps)
        : base(manager, player.EntityIndex)
    {
        _loadoutSlot = loadoutSlot;
        _labelText = labelText;
        _beeps = beeps;

        if (Manager.Viewport.Items is { } schema)
        {
            _entity = EntityForLoadoutSlot(player, loadoutSlot, schema)?.EntityIndex;
        }

        if (_entity is null)
        {
            MeterEnabled = false;
        }
    }

    /// <summary>`GetItem` (:1666): the item while its handle holds.</summary>
    /// <returns>The item, or null.</returns>
    /// <remarks>**Interpolated:** the handle holds while the player still carries the entity — the demo's view of it.</remarks>
    public SceneItem? Item()
    {
        foreach (SceneItem item in Player?.Items ?? [])
        {
            if (item.EntityIndex == _entity)
            {
                return item;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override string LabelText() => _labelText;

    /// <inheritdoc/>
    /// <remarks>`GetItemChargeMeter( m_iLoadoutSlot )` over 100 (:1672).</remarks>
    public override float Progress()
    {
        if (Item() is null || Player is not { } player)
        {
            return 0f;
        }

        return player.ItemChargeMeter is { } meters && _loadoutSlot < meters.Count ? meters[_loadoutSlot] / 100.0f : 0f;
    }

    /// <inheritdoc/>
    public override bool ShouldBeep() => _beeps;

    /// <inheritdoc/>
    /// <remarks>`ShouldDrawMeter()` is false only for the Dragon's Fury, which has a meter on the gun (tf_weapon_dragons_fury.cpp:346).</remarks>
    public override bool ShouldDraw(HudState state)
    {
        if (Item() is not { } item || item.ClassName == "CTFWeaponFlameBall")
        {
            return false;
        }

        return base.ShouldDraw(state);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// `OnTick` (:1697), every 100 ms by `AddTickSignal`: a lost item is looked for again. **Interpolated:** each frame's
    /// think, which finds the same item no later than the tick would.
    /// </remarks>
    protected override void OnThink()
    {
        if (Item() is null && Player is { } player && Manager.Viewport.Items is { } schema
            && EntityForLoadoutSlot(player, _loadoutSlot, schema) is { } found)
        {
            _entity = found.EntityIndex;
        }

        base.OnThink();
    }

    /// <summary>
    /// `GetEntityForLoadoutSlot( slot, true )` (tf_player_shared.cpp:11928): a worn item in that slot first, then the first
    /// weapon whose definition's slot for the class matches.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="slot">The loadout slot.</param>
    /// <param name="schema">The item schema.</param>
    /// <returns>The item, or null.</returns>
    internal static SceneItem? EntityForLoadoutSlot(ScenePlayer player, int slot, Content.Assets.ItemSchema schema)
    {
        int playerClass = player.PlayerClass ?? 0;
        IReadOnlyList<SceneItem> items = player.Items ?? [];

        foreach (SceneItem item in items)
        {
            if (!item.IsWeapon && item.DefinitionIndex is { } worn && schema.LoadoutSlot(worn, playerClass) == slot)
            {
                return item;
            }
        }

        foreach (SceneItem item in items)
        {
            if (item.IsWeapon && item.DefinitionIndex is { } definition && schema.LoadoutSlot(definition, playerClass) == slot)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`FindAttribute( pItem, pAttrDef, &amp;value )`: the item's attribute of that name as its view resolves them.</summary>
    /// <param name="item">The item.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The attribute, or null.</returns>
    internal static EconAttributeValue? FindAttribute(SceneItem item, Content.Assets.ItemSchema schema, string name)
    {
        if (schema.AttributeDefinitionIndex(name) is not { } index)
        {
            return null;
        }

        IReadOnlyList<EconAttributeValue> resolved = EconAttributes.Resolve(
            item.Wire.Local,
            item.Wire.NetworkedForDemos,
            item.Wire.HasValidItemId,
            item.DefinitionIndex is { } definition ? schema.DefinitionAttributesFor(definition) : []);

        foreach (EconAttributeValue attribute in resolved)
        {
            if (attribute.DefinitionIndex == index)
            {
                return attribute;
            }
        }

        return null;
    }
}

/// <summary>`UTIL_ReplaceKeyBindings` (cdll_util.cpp:868): each `%command%` replaced by the key bound to it.</summary>
public static class TfKeyBindings
{
    /// <summary>The string with every `%binding%` replaced, `%%` kept as one `%`.</summary>
    /// <param name="text">The localised string.</param>
    /// <param name="lookupBinding">`engine->Key_LookupBinding`, or null for none bound.</param>
    /// <param name="localize">`g_pVGuiLocalize->Find`, the `#` stripped.</param>
    /// <returns>The string.</returns>
    /// <remarks>
    /// A leading `+` is dropped before the lookup; an unbound command reads "&lt; not bound &gt;"; the key name is
    /// upper-cased and drawn as its localised name when one exists. The Steam Controller's origins are not asked: no
    /// controller drives a demo viewer.
    /// </remarks>
    public static string Replace(string text, Func<string, string?>? lookupBinding, Func<string, string?>? localize)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder output = new(text.Length);
        int at = 0;

        while (at < text.Length)
        {
            if (text[at] != '%')
            {
                output.Append(text[at]);
                at++;
                continue;
            }

            at++;

            if (at >= text.Length)
            {
                break;
            }

            int end = text.IndexOf('%', at);

            // "make sure we handle %% in the string, which should be treated in the output as %".
            if (end < 0 || end == at)
            {
                output.Append(text[at]);
                at++;
                continue;
            }

            string binding = text[at..end];
            string key = lookupBinding?.Invoke(binding.StartsWith('+') ? binding[1..] : binding) ?? "< not bound >";
            string friendlyName = key.ToUpperInvariant();

            output.Append(localize?.Invoke(friendlyName) is { Length: > 0 } localized ? localized : friendlyName);
            at = end + 1;
        }

        return output.ToString();
    }
}

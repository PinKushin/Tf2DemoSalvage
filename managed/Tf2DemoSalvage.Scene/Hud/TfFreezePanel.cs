using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFFreezePanelHealth` (tf_hud_freezepanel.h:30): the killer's health, its own `.res`, and no think.</summary>
/// <param name="parent">The parent.</param>
/// <param name="name">The name.</param>
public sealed class TfFreezePanelHealth(VguiPanel? parent, string? name) : TfHudPlayerHealth(parent, name)
{
    /// <inheritdoc/>
    public override string ClassName => "CTFFreezePanelHealth";

    /// <inheritdoc/>
    protected override string ResFilename => "resource/UI/FreezePanelKillerHealth.res";

    /// <inheritdoc/>
    /// <remarks>"Do nothing. We're just preventing the base health panel from updating."</remarks>
    protected override void OnThink()
    {
        // The freeze panel sets the killer's health itself; the local player's must not overwrite it.
    }
}

/// <summary>
/// `CTFFreezePanel` (tf_hud_freezepanel.cpp): the card over a point-of-view recorder's freeze cam: who killed him, how
/// much health the killer has left, a nemesis line, and the screenshot prompt (B748).
/// </summary>
/// <remarks>
/// **Driven by events, as the engine's is**: `show_freezepanel`, `hide_freezepanel` and `freezecam_started`, which the
/// client fires itself (`c_baseplayer.cpp:905`, `:922`, `:1721`) and the HUD here derives from the recorder's observer
/// mode (<see cref="FreezeEvents"/>), and `player_death`, `teamplay_win_panel` and `training_complete` off the demo.
///
/// *Not built:* the Halloween bosses' names, which need the killer entity's class; taking a screenshot, which the viewer
/// does not do from this key, so the prompt only shows; the taunt acknowledgements the callouts send the server, which a
/// demo has no server for.
/// </remarks>
public sealed class TfFreezePanel : VguiEditablePanel, IHudElement
{
    /// <summary>`GetResFilename` (:671), whatever the killer.</summary>
    public const string ResFile = "resource/UI/FreezePanel_Basic.res";

    /// <summary>`TF_DEATH_DOMINATION` (tf_shareddefs.h).</summary>
    private const int DeathDomination = 0x0001;

    /// <summary>`TF_DEATH_REVENGE`.</summary>
    private const int DeathRevenge = 0x0004;

    /// <summary>`TF_TEAM_BLUE`.</summary>
    private const int TeamBlue = 3;

    /// <summary>`GR_STATE_TEAM_WIN`: `RoundHasBeenWon()`.</summary>
    private const int RoundTeamWin = 5;

    /// <summary>`ShowSnapshotPanelIn( 1.25 )` (:256).</summary>
    private const float SnapshotDelay = 1.25f;

    /// <summary>`ShowCalloutsIn( 1.0 )` (:255).</summary>
    private const float CalloutDelay = 1f;

    /// <summary>`m_flShowCalloutsAt`: when `OnThink` places the callouts, 0 for never.</summary>
    private float _showCalloutsAt;

    /// <summary>`m_pCalloutPanels`.</summary>
    private readonly List<TfFreezePanelCallout> _callouts = [];

    /// <summary>The callouts' `RandomInt` stream; see <see cref="TfFreezePanelCallout.UpdateForGib"/>.</summary>
    private readonly Core.Primitives.UniformRandomStream _random = new();

    private enum Nemesis
    {
        None,
        New,
        Revenge,
    }

    private Nemesis _showNemesis;
    private int _killerIndex;
    private int _yBase = -1;
    private (int X, int Y) _baseOrigin = (-1, -1);
    private (int X, int Y) _killerOrigin;
    private float _showSnapshotAt;

    private VguiEditablePanel? _basePanel;
    private VguiLabel? _freezeLabel;
    private VguiLabel? _killerLabel;
    private TfImagePanel? _background;
    private VguiEditablePanel? _nemesisSubPanel;
    private TfFreezePanelHealth? _killerHealth;
    private VguiPanel? _avatar;
    private VguiEditablePanel? _screenshotPanel;

    /// <summary>`CTFFreezePanel( pElementName )` (:90): parented to the viewport, hidden.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfFreezePanel(VguiPanel viewport)
        : base(viewport, "FreezePanel")
    {
        Visible = false;

        // `m_pItemPanel = new CItemModelPanel( this, "itempanel" )` (:109), before any `.res`.
        ItemPanel = new TfItemModelPanel(this, "itempanel");
    }

    /// <summary>`m_pItemPanel`: the killer's weapon or powerup (B751).</summary>
    public TfItemModelPanel ItemPanel { get; }

    private (int X, int Y) _itemPanelOrigin = (-1, -1);

    /// <summary>`Powerup Strength` to `Powerup Supernova`, by `RuneTypes_t` (:414-425).</summary>
    private static readonly string[] PowerupItems =
    [
        "Powerup Strength", "Powerup Haste", "Powerup Regen", "Powerup Resist", "Powerup Vampire", "Powerup Reflect",
        "Powerup Precision", "Powerup Agility", "Powerup Knockout", "Powerup King", "Powerup Plague", "Powerup Supernova",
    ];

    /// <inheritdoc/>
    public override string ClassName => "CTFFreezePanel";

    /// <inheritdoc/>
    public int HiddenBits => 0;

    /// <summary>The events `Init` listens for (:139-144).</summary>
    public static IReadOnlySet<string> ListensFor { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "show_freezepanel", "hide_freezepanel", "freezecam_started", "player_death", "teamplay_win_panel", "training_complete",
    };

    /// <summary>`ShouldDraw` (:908): `IsVisible()`.</summary>
    /// <param name="state">Unread.</param>
    /// <returns>Whether the panel is up.</returns>
    public bool ShouldDraw(HudState state) => Visible;

    /// <summary>The killer the card names, `m_iKillerIndex`: a building's builder once one is resolved.</summary>
    public int KillerIndex => _killerIndex;

    /// <summary>`m_pFreezeLabel`.</summary>
    public VguiLabel? FreezeLabel => _freezeLabel;

    /// <summary>`m_pKillerHealth`.</summary>
    public TfFreezePanelHealth? KillerHealth => _killerHealth;

    /// <summary>`m_pNemesisSubPanel`.</summary>
    public VguiEditablePanel? NemesisSubPanel => _nemesisSubPanel;

    /// <summary>`m_pScreenshotPanel`.</summary>
    public VguiEditablePanel? ScreenshotPanel => _screenshotPanel;

    /// <summary>`m_pFreezePanelBG`.</summary>
    public TfImagePanel? Background => _background;

    /// <summary>`m_pCalloutPanels`: the callouts over the recorder's gibs and ragdoll (B752).</summary>
    public IReadOnlyList<TfFreezePanelCallout> Callouts => _callouts;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:174): the base, the `.res`, the children it keeps, and their first positions.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings(ResFile, context);

        _basePanel = FindChildByName("FreezePanelBase") as VguiEditablePanel;

        if (_basePanel is { } basePanel)
        {
            _freezeLabel = basePanel.FindChildByName("FreezeLabel") as VguiLabel;
            _killerLabel = basePanel.FindChildByName("FreezeLabelKiller") as VguiLabel;
            _background = basePanel.FindChildByName("FreezePanelBG") as TfImagePanel;
            _nemesisSubPanel = basePanel.FindChildByName("NemesisSubPanel") as VguiEditablePanel;
            _killerHealth = basePanel.FindChildByName("FreezePanelHealth") as TfFreezePanelHealth;
            _avatar = basePanel.FindChildByName("AvatarImage");
            _baseOrigin = (basePanel.X, basePanel.Y);
        }

        _screenshotPanel = FindChildByName("ScreenshotPanel") as VguiEditablePanel;

        // "Move killer panels when the win panel is up".
        _yBase = Y;

        if (_killerLabel is { } killer)
        {
            _killerOrigin = (killer.X, killer.Y);
        }

        _itemPanelOrigin = (ItemPanel.X, ItemPanel.Y);
    }

    /// <summary>`Reset` (:123): hidden.</summary>
    public void Reset() => Hide();

    /// <summary>`FireGameEvent` (:223).</summary>
    /// <param name="fired">The event.</param>
    /// <param name="state">The game state.</param>
    public void HandleGameEvent(HudGameEvent fired, HudState state)
    {
        ArgumentNullException.ThrowIfNull(fired);

        SceneGameEvent e = fired.Event;

        switch (e.Name)
        {
            case "player_death":
                // "see if the local player died", and whether as a new nemesis or a revenge.
                if (state.HasLocalPlayer && e.PlayerForUserId(e.GetInt("userid")) == state.LocalIndex)
                {
                    _showNemesis = NemesisOf(e.GetInt("death_flags"));
                }

                break;

            case "hide_freezepanel" or "teamplay_win_panel" or "training_complete":
                Hide();
                break;

            case "freezecam_started":
                _showCalloutsAt = state.CurTime + CalloutDelay;
                _showSnapshotAt = state.CurTime + SnapshotDelay;
                break;

            case "show_freezepanel":
                ShowFor(e.GetInt("killer"), state);
                break;
        }
    }

    /// <summary>A new nemesis, a revenge, or neither, by the death's flags (:235-246).</summary>
    private static Nemesis NemesisOf(int flags)
    {
        if ((flags & DeathDomination) != 0)
        {
            return Nemesis.New;
        }

        return (flags & DeathRevenge) != 0 ? Nemesis.Revenge : Nemesis.None;
    }

    /// <summary>`show_freezepanel`'s branch (:293-665).</summary>
    private void ShowFor(int killer, HudState state)
    {
        _killerIndex = killer;
        ScenePlayer? killerPlayer = state.Player(killer);
        SceneBuilding? killerBuilding = killerPlayer is null ? state.Building(killer) : null;

        // `pTFPlayerKiller`: the player, or a building's owner.
        ScenePlayer? owner = killerPlayer ?? (killerBuilding?.BuilderEntityIndex is { } builder ? state.Player(builder) : null);

        // `if ( !g_TF_PR )`: no player resource, no panel.
        if (state.Players is null)
        {
            _nemesisSubPanel?.SetDialogVariable("nemesisname", string.Empty);
            return;
        }

        Show();
        ShowSnapshotPanel(false, state);

        if (_basePanel is { } basePanel && _baseOrigin.X > -1 && _baseOrigin.Y > -1)
        {
            (basePanel.X, basePanel.Y) = _baseOrigin;
        }

        if (_itemPanelOrigin.X > -1 && _itemPanelOrigin.Y > -1)
        {
            (ItemPanel.X, ItemPanel.Y) = _itemPanelOrigin;
        }

        // "RoundHasBeenWon": fifty units higher, over the win panel.
        Y = state.RoundState == RoundTeamWin ? (int)(_yBase - HudViewport.YRes(this, 50)) : _yBase;

        // Hidden for any killer (:371-374); the player branch shows it again.
        ItemPanel.Visible = false;

        if (killerPlayer is { } player)
        {
            ShowPlayerKiller(player, state);
            ShowItemPanel(player, state);
        }
        else if (killerBuilding is { } building)
        {
            ShowBuildingKiller(building, owner, state);
        }
        else
        {
            ShowOtherKiller();
        }

        // `if ( pKiller )` (:353): any entity at the index — the world at 0 included — gets the background and the label's
        // place. *Interpolated:* an index whose entity is gone is answered as one that stands, which no state here says.
        int? team = killerPlayer?.Team ?? owner?.Team ?? killerBuilding?.Team;

        // "use the killer's team for the background color".
        _background?.SetImage(team == TeamBlue ? "../hud/color_panel_blu" : "../hud/color_panel_red");

        if (_avatar is { } avatar && _killerLabel is { } label)
        {
            // `m_pAvatar->IsValid()` is never true: a demo fetches no Steam avatar, so the name takes its place.
            label.X = avatar.X;
            label.Y = _killerOrigin.Y;
        }

        // `ToTFPlayer( pKiller )`, and the building branch has made `pKiller` its builder (:508).
        ShowNemesis(owner?.EntityIndex, state);
    }

    /// <summary>The player killer's card (:353-490): health, the label by nemesis and life, the name.</summary>
    private void ShowPlayerKiller(ScenePlayer player, HudState state)
    {
        int health = player.IsAlive ? player.EntityHealth ?? 0 : 0;
        int max = player.MaxHealth ?? 1;

        if (_killerHealth is { } healthPanel)
        {
            healthPanel.Building = false;
            healthPanel.SetHealth(health, max, HudState.GetMaxBuffedHealth(player.MaxHealthForBuffing ?? 1, max, player.EntityHealth ?? 0));
        }

        // "If this was just a regular kill but this guy is our nemesis then just show it."
        bool nemesis = Recorder(state) is { } victim && victim.IsDominatedBy(player.EntityIndex);

        _freezeLabel?.SetText(
            (nemesis, player.IsAlive) switch
            {
                (true, false) => "#FreezePanel_Nemesis_Dead",
                (true, true) => "#FreezePanel_Nemesis",
                (false, false) => "#FreezePanel_Killer_Dead",
                _ => "#FreezePanel_Killer",
            },
            Localize);

        _basePanel?.SetDialogVariable("killername", PlayerName(state, player.EntityIndex));
    }

    /// <summary>
    /// The item panel (:411-490): the killer's powerup as a fake unique item, else his active weapon when its quality is not
    /// normal; then pinned twelve units above the screen's bottom.
    /// </summary>
    /// <remarks>
    /// *Not built:* the stat panel, which hides the item when a new record is showing (:455-460); it is not ported, so it
    /// is never up.
    /// </remarks>
    private void ShowItemPanel(ScenePlayer killer, HudState state)
    {
        if (HudViewport.Of(this)?.Items is not { } schema)
        {
            return;
        }

        string name = PlayerName(state, killer.EntityIndex);

        // `IsCarryingRune()`: `GetCarryingRuneType() != RUNE_NONE`.
        if (TfAmmo.CarryingRuneType(killer.Conditions) is var rune and >= 0)
        {
            if (rune < PowerupItems.Length && schema.ItemDefinitionIndex(PowerupItems[rune]) is { } powerup)
            {
                // "Create a fake, temp item": unique, level 0, origin invalid, so the definition's attributes.
                IReadOnlyList<Core.Scene.EconAttributeValue> attributes = schema.DefinitionAttributesFor(powerup);
                Dictionary<int, Core.Scene.EconAttributeValue> byIndex = [];

                foreach (Core.Scene.EconAttributeValue attribute in attributes)
                {
                    byIndex.TryAdd(attribute.DefinitionIndex, attribute);
                }

                ItemPanel.SetDialogVariable("killername", name);
                ItemPanel.SetItem(new TfItemView(powerup, TfItemDescription.QualityUnique, byIndex) { Iterated = attributes, IteratesDefinition = true });
                ItemPanel.Visible = true;
            }
        }
        else if (killer.ActiveWeaponItem() is { } weapon &&(weapon.Quality ?? 0) != TfItemDescription.QualityNormal
            && TfItemView.From(weapon, schema) is { } item)
        {
            // `FindControl<Label>( "ItemLabel" )`: a direct child only.
            if (ItemPanel.FindChildByName("ItemLabel", false) is VguiLabel itemLabel)
            {
                // "Change the label text depending on if they're holding someone else's item".
                int? owner = state.PlayerByAccountId(item.AccountId)?.EntityIndex;
                itemLabel.SetText(owner is null || owner == killer.EntityIndex ? "#FreezePanel_Item" : "#FreezePanel_ItemOtherOwner", Localize);
                ItemPanel.SetDialogVariable("ownername", owner is { } ownerIndex ? PlayerName(state, ownerIndex) : string.Empty);
            }

            ItemPanel.SetDialogVariable("killername", name);
            ItemPanel.SetItem(item);
            ItemPanel.Visible = true;
        }

        if (ItemPanel.Visible)
        {
            // `ScreenHeight() - YRES(12) - tall`, a double truncated once (:489).
            ItemPanel.Y = (int)((HudViewport.Of(this)?.Tall ?? 480) - HudViewport.YRes(this, 12) - ItemPanel.Tall);
        }
    }

    /// <summary>The building killer's card (:492-540): its health, its builder's name, and what it was.</summary>
    private void ShowBuildingKiller(SceneBuilding building, ScenePlayer? owner, HudState state)
    {
        if (_killerHealth is { } healthPanel)
        {
            // `iMaxBuffedHealth` is `pTFPlayerKiller`'s, which for a building is its builder (:308, :357-360).
            int buffed = owner is { } buffing
                ? HudState.GetMaxBuffedHealth(buffing.MaxHealthForBuffing ?? 1, buffing.MaxHealth ?? 1, buffing.EntityHealth ?? 0)
                : 0;

            healthPanel.Building = true;
            healthPanel.SetHealth(building.Health, building.MaxHealth, buffed);
        }

        if (owner is { } builder)
        {
            _killerIndex = builder.EntityIndex;
            _basePanel?.SetDialogVariable("killername", PlayerName(state, builder.EntityIndex));
        }

        // Shown with an owner, hidden without (:502-516).
        if (_avatar is { } avatar)
        {
            avatar.Visible = owner is not null;
        }

        // `pKiller` is the builder once there is one, and a building is alive while it stands.
        _freezeLabel?.SetText(owner is { IsAlive: false } ? "#FreezePanel_KillerObject_Dead" : "#FreezePanel_KillerObject", Localize);

        // `GetStatusName()`, localized where it can be, else as it is.
        string status = TfTargetId.StatusName(building, HudViewport.Of(this)?.Scripts?.ObjectInfo(building.ObjectType, building.ObjectMode).StatusName);
        _basePanel?.SetDialogVariable("objectkiller", Localize(status.TrimStart('#')) ?? status);
    }

    /// <summary>
    /// Any other killer (:353-369, :568-578): the world, a boss, a tank. Its health as the entity has it, which for the
    /// world is 0 of 0, and the plain label by its life; the name is left as the last card set it, as the engine leaves it.
    /// </summary>
    /// <remarks>
    /// *Not built:* the three Halloween bosses' names (:541-567), which need the killer entity's class; and their health,
    /// which no state here carries, so every such killer is answered as the world is.
    /// </remarks>
    private void ShowOtherKiller()
    {
        if (_killerHealth is { } healthPanel)
        {
            healthPanel.Building = false;
            healthPanel.SetHealth(0, 0, 0);
        }

        // `C_BaseEntity::IsAlive` is `m_lifeState == LIFE_ALIVE`, which the world's is.
        _freezeLabel?.SetText("#FreezePanel_Killer", Localize);
    }

    /// <summary>The nemesis line (:601-664).</summary>
    private void ShowNemesis(int? playerKiller, HudState state)
    {
        string? token = _showNemesis switch
        {
            // "If this was just a regular kill but this guy is our nemesis then just show it." Also checked for a new
            // nemesis: "victim may have managed to kill him after victim's death by grenade or some such".
            Nemesis.None or Nemesis.New when playerKiller is { } killer && Recorder(state) is { } victim && victim.IsDominatedBy(killer) =>
                _showNemesis == Nemesis.New ? "TF_NewNemesis" : "TF_FreezeNemesis",
            Nemesis.Revenge => "TF_GotRevenge",
            _ => null,
        };

        string? text = token is null ? null : Localize(token);

        if (_nemesisSubPanel is { } subPanel)
        {
            subPanel.SetDialogVariable("nemesisname", text ?? string.Empty);

            if (subPanel.FindChildByName("NemesisLabel2") is { } advice)
            {
                advice.Visible = false;
            }
        }

        ShowNemesisPanel(text is not null);
        _showNemesis = Nemesis.None;
    }

    /// <summary>`ShowNemesisPanel` (:1196): shown, and its label, icon and background widened to the text, right side pinned.</summary>
    private void ShowNemesisPanel(bool show)
    {
        if (_nemesisSubPanel is not { } subPanel)
        {
            return;
        }

        subPanel.Visible = show;

        if (!show
            || subPanel.FindChildByName("NemesisLabel") is not VguiLabel label
            || subPanel.FindChildByName("NemesisLabel2") is not VguiLabel advice
            || subPanel.FindChildByName("NemesisPanelBG") is not { } background
            || subPanel.FindChildByName("NemesisIcon") is not { } icon)
        {
            return;
        }

        VguiLabel measured = advice.Visible ? advice : label;
        (int wide, int tall) = measured.GetContentSize();
        int diffX = wide - measured.Wide;
        int diffY = tall - measured.Tall;

        if (diffX == 0 && diffY == 0)
        {
            return;
        }

        (icon.X, icon.Y) = (icon.X - diffX, icon.Y - diffY);

        foreach (VguiPanel grown in (VguiPanel[])[label, advice, background])
        {
            (grown.X, grown.Y) = (grown.X - diffX, grown.Y - diffY);
            grown.SetSize(grown.Wide + diffX, grown.Tall + diffY);
        }

        subPanel.Y -= diffY;
        subPanel.SetSize(subPanel.Wide, subPanel.Tall + diffY);
    }

    /// <inheritdoc/>
    /// <remarks>`OnThink` (:916): the screenshot prompt once its time comes, if the panel is still up.</remarks>
    protected override void OnThink()
    {
        // The callouts, once a second after the freeze cam started, if the panel is still up (:929-936).
        if (_showCalloutsAt > 0f && HudViewport.Of(this) is { } viewport && _showCalloutsAt < viewport.State.CurTime)
        {
            if (Visible)
            {
                UpdateCallout(viewport);
            }

            _showCalloutsAt = 0f;
        }

        if (_showSnapshotAt > 0f && HudViewport.Of(this) is { } hud && _showSnapshotAt < hud.State.CurTime)
        {
            if (Visible)
            {
                ShowSnapshotPanel(true, hud.State);
            }

            _showSnapshotAt = 0f;
        }
    }

    /// <summary>`ShowSnapshotPanel` (:992): the prompt with the key bound to `screenshot`, hidden when none is.</summary>
    private void ShowSnapshotPanel(bool show, HudState state)
    {
        if (_screenshotPanel is not { } panel)
        {
            return;
        }

        string? key = state.KeyLookupBinding?.Invoke("screenshot");

        if (key is null)
        {
            show = false;
        }

        if (show && Localize("TF_freezecam_snapshot") is { } format)
        {
            panel.SetDialogVariable("text", VguiLocalize.ConstructString(format, 256, key!));
            HudViewport.Of(this)?.Animations.StartAnimationSequence(this, "HudSnapShotReminderIn");
        }

        panel.Visible = show;
    }

    /// <summary>`Show` (:863): no callouts pending, and up.</summary>
    private void Show()
    {
        _showCalloutsAt = 0f;
        Visible = true;
    }

    /// <summary>`Hide` (:887): hidden, its callouts deleted. The prompt's timer runs on, as `m_flShowSnapshotReminderAt` does.</summary>
    private void Hide()
    {
        Visible = false;
        DeleteCalloutPanels();
    }

    /// <summary>`DeleteCalloutPanels` (:875): each callout taken off the viewport.</summary>
    private void DeleteCalloutPanels()
    {
        foreach (TfFreezePanelCallout callout in _callouts)
        {
            callout.SetParent(null);
        }

        _callouts.Clear();
    }

    /// <summary>
    /// `UpdateCallout` (:750): a callout over each of the local player's spawned gibs, in their order, then his ragdoll,
    /// each where `TestAndAddCallout` finds room.
    /// </summary>
    /// <remarks>
    /// **The freeze panel's own box is its background's position within its parent**, as `m_pFreezePanelBG->GetPos` gives it,
    /// compared against screen coordinates: Valve's own, kept. The stat panel is not ported, so its box is the empty one the
    /// engine starts from (:774-775). *Not built:* the sentry builder's and the gibs' taunt acknowledgements (:817-835), sent
    /// to a server.
    /// </remarks>
    private void UpdateCallout(HudViewport viewport)
    {
        HudState state = viewport.State;

        if (state.FreezeCallouts is not { } sources || (sources.Gibs.Count == 0 && sources.Ragdoll is null) ||
            _background is not { } background || state.WorldToScreen is not { } matrix)
        {
            return;
        }

        Vector3 freezeTopLeft = new(background.X, background.Y, 0f);
        Vector3 freezeBottomRight = new(background.X + background.Wide, background.Y + background.Tall, 1f);
        Vector3 statTopLeft = Vector3.Zero;
        Vector3 statBottomRight = new(0f, 0f, 1f);
        List<(Vector3 TopLeft, Vector3 BottomRight)> placed = [];
        int count = 0;

        foreach ((int index, Vector3 origin) in sources.Gibs)
        {
            if (TestAndAddCallout(viewport, matrix, sources, origin, placed, (freezeTopLeft, freezeBottomRight), (statTopLeft, statBottomRight)) is { } callout)
            {
                callout.UpdateForGib(index, count, _random);
                count++;
            }
        }

        if (sources.Ragdoll is { } ragdoll)
        {
            TestAndAddCallout(viewport, matrix, sources, ragdoll, placed, (freezeTopLeft, freezeBottomRight), (statTopLeft, statBottomRight))
                ?.UpdateForRagdoll();
        }
    }

    /// <summary>
    /// `TestAndAddCallout` (:687): the arrow's tip at the point, the callout fully on screen and clear of the bottom 40, of
    /// the freeze and stat panels and of every callout placed, and in sight of the view; then made.
    /// </summary>
    private TfFreezePanelCallout? TestAndAddCallout(
        HudViewport viewport,
        float[] matrix,
        FreezeCalloutSources sources,
        Vector3 origin,
        List<(Vector3 TopLeft, Vector3 BottomRight)> placed,
        (Vector3 TopLeft, Vector3 BottomRight) freeze,
        (Vector3 TopLeft, Vector3 BottomRight) stat)
    {
        // "This is the offset from the topleft of the callout to the arrow tip": `const int` of `XRES(25)` and `YRES(50)`.
        int xOffset = (int)HudViewport.XRes(this, 25);
        int yOffset = (int)HudViewport.YRes(this, 50);
        int screenWide = viewport.Wide;
        int screenTall = viewport.Tall;

        (bool inFront, int x, int y) = HudSpace.GetVectorInHudSpace(matrix, (origin.X, origin.Y, origin.Z), screenWide, screenTall);

        if (!inFront)
        {
            return null;
        }

        x -= xOffset;
        y -= yOffset;

        // `CALLOUT_WIDE` is `XRES(100)` and `CALLOUT_TALL` `XRES(50)`, both of the width, added as floats and kept as `int`.
        double calloutWide = HudViewport.XRes(this, 100);
        double calloutTall = HudViewport.XRes(this, 50);
        int right = (int)(x + calloutWide);
        int bottom = (int)(y + calloutTall);

        if (x <= 0 || y <= 0 || right >= screenWide || bottom >= screenTall - HudViewport.YRes(this, 40))
        {
            return null;
        }

        Vector3 topLeft = new(x, y, 0f);
        Vector3 bottomRight = new(right, bottom, 1f);

        if (Intersects(topLeft, bottomRight, freeze.TopLeft, freeze.BottomRight) ||
            Intersects(topLeft, bottomRight, stat.TopLeft, stat.BottomRight))
        {
            return null;
        }

        foreach ((Vector3 otherTopLeft, Vector3 otherBottomRight) in placed)
        {
            if (Intersects(topLeft, bottomRight, otherTopLeft, otherBottomRight))
            {
                return null;
            }
        }

        // "Verify that we have LOS to the gib": `MASK_OPAQUE` from it to `MainViewOrigin()`.
        if (sources.OpaqueTrace is { } trace && viewport.State.View is { } view &&
            trace(origin, new Vector3(view.Origin.X, view.Origin.Y, view.Origin.Z)) < 1f)
        {
            return null;
        }

        TfFreezePanelCallout callout = new(viewport);

        if (viewport.Context is { } context)
        {
            callout.PerformApplySchemeSettings(context);
        }

        _callouts.Add(callout);
        placed.Add((topLeft, bottomRight));
        callout.Visible = true;
        (callout.X, callout.Y) = (x, y);
        callout.SetSize((int)calloutWide, (int)calloutTall);

        return callout;
    }

    /// <summary>`QuickBoxIntersectTest` (mathlib.h:1398): strictly overlapping on all three axes; boxes that touch do not.</summary>
    private static bool Intersects(Vector3 min1, Vector3 max1, Vector3 min2, Vector3 max2) =>
        min1.X < max2.X && max1.X > min2.X && min1.Y < max2.Y && max1.Y > min2.Y && min1.Z < max2.Z && max1.Z > min2.Z;

    /// <summary>`g_PR->GetPlayerName( index )`: the player resource's, not the event's — a derived event carries no roster.</summary>
    private static string PlayerName(HudState state, int index) => state.Names?.GetValueOrDefault(index) ?? string.Empty;

    /// <summary>`C_TFPlayer::GetLocalTFPlayer()`.</summary>
    private static ScenePlayer? Recorder(HudState state) => state.HasLocalPlayer ? state.Player(state.LocalIndex) : null;

    /// <summary>`g_pVGuiLocalize->Find`, by token without its `#`.</summary>
    private string? Localize(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token);
}

/// <summary>
/// The client-side freeze events (`c_baseplayer.cpp:885-935`, `:1719-1729`), which no demo carries: derived from the
/// recorder's observer mode as it changes, as <c>localplayer_changedisguise</c> is (B748).
/// </summary>
/// <remarks>
/// `show_freezepanel` on the data update that enters the freeze cam, naming the observer target as the killer;
/// `hide_freezepanel` on the one that leaves it; and `freezecam_started` the first frame `spec_freeze_traveltime` after the
/// entry, when `CalcFreezeCamView` calls `view->FreezeFrame`. *Interpolated:* the entry is the first frame whose state is
/// in the freeze cam, where the engine's is the packet; after a seek into a freeze cam the entry is the seek's frame.
/// </remarks>
public sealed class FreezeEvents
{
    /// <summary>`OBS_MODE_FREEZECAM`.</summary>
    private const int FreezeCam = ObserverModes.FreezeCam;

    private bool _freezing;
    private float _enteredAt;
    private bool _sentStarted;

    /// <summary>The events this frame's state fires, in order.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="reset">Whether this frame follows a seek: the last frame's mode is not known.</param>
    /// <returns>The events, each named as the engine names it; `show_freezepanel` carries `killer`.</returns>
    public IReadOnlyList<SceneGameEvent> Advance(HudState state, bool reset)
    {
        List<SceneGameEvent> fired = [];
        bool freezing = state.HasLocalPlayer && state.ObserverMode == FreezeCam;

        if (reset)
        {
            _freezing = false;
        }

        if (freezing && !_freezing)
        {
            _enteredAt = state.CurTime;
            _sentStarted = false;
            fired.Add(Event("show_freezepanel", new Dictionary<string, object?> { ["killer"] = state.ObserverTarget }));
        }
        else if (!freezing && _freezing)
        {
            fired.Add(Event("hide_freezepanel", new Dictionary<string, object?>()));
        }

        // Only `CalcFreezeCamView` fires it, which falls to the death cam with no observer target (c_baseplayer.cpp:1654-1659).
        if (freezing && !_sentStarted && state.ObserverTarget != 0 && state.CurTime - _enteredAt >= ObserverCamera.FreezeTravelTime)
        {
            _sentStarted = true;
            fired.Add(Event("freezecam_started", new Dictionary<string, object?>()));
        }

        _freezing = freezing;

        return fired;
    }

    private static SceneGameEvent Event(string name, Dictionary<string, object?> fields) =>
        new(0, name, fields, new Dictionary<int, Core.Net.PlayerInfo>());
}

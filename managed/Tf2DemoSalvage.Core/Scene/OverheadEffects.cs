using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What one update hands a player's overhead effects, each the engine's own input (B765).</summary>
/// <param name="IsLocal">`IsLocalPlayer()`: the recorder.</param>
/// <param name="SourceTv">`IsHLTV()`: a SourceTV demo, whose `OnDataChanged` skips the IT, dueling and nemesis icons.</param>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="DisguiseTeam">`m_Shared.GetDisguiseTeam()`.</param>
/// <param name="IsEnemy">`IsEnemyPlayer()`, against the recorder's team.</param>
/// <param name="Conditions">His conditions.</param>
/// <param name="Alive">`IsAlive()`.</param>
/// <param name="HasCustomModel">`m_PlayerClass.HasCustomModel()`.</param>
/// <param name="Connected">`g_PR-&gt;IsConnected( entindex() )`.</param>
/// <param name="NemesisOfLocal">`IsNemesisOfLocalPlayer()`: the recorder's `m_bPlayerDominatingMe` for him.</param>
/// <param name="IsIt">`TFGameRules()-&gt;IsIT( this )`: the game rules' `m_itHandle`.</param>
/// <param name="HideDominationIcons">`IsInTournamentMode() &amp;&amp; tf_tournament_hide_domination_icons`.</param>
public readonly record struct OverheadInputs(
    bool IsLocal,
    bool SourceTv,
    int? Team,
    int? DisguiseTeam,
    bool IsEnemy,
    PlayerConditions Conditions,
    bool Alive,
    bool HasCustomModel,
    bool Connected,
    bool NemesisOfLocal,
    bool IsIt,
    bool HideDominationIcons);

/// <summary>
/// One player's `m_mapOverheadEffects`: the icons over his head, in the order `UpdateOverheadEffects` spaces them (B765).
/// </summary>
/// <remarks>
/// The engine's rules are quoted in `OverheadEffectsConformanceTests`. Stepped once a tick, standing in for both
/// `OnDataChanged` (the IT, dueling and nemesis icons) and the `ClientThink` after it (the rune, then the mark), which is
/// the order a frame runs them in.
///
/// **The order is a `CUtlMap`'s node order** (`FOR_EACH_MAP_FAST`, `c_tf_player.cpp:11636`): a removed icon frees its
/// node, and the next one added takes the last node freed, so a list of the slots in use is the walk.
///
/// **The dueling icon is never shown**: its opponent comes only from Game Coordinator messages
/// (`tf_consumables.cpp:1937-1946`), which no demo carries.
/// </remarks>
public sealed class OverheadEffects
{
    /// <summary>`ShowIconForIT`'s effect (`c_tf_player.cpp:5477-5488`).</summary>
    public const string ItIcon = "halloween_boss_victim";

    /// <summary>`UpdatedMarkedForDeathEffect`'s effect (`:5197`).</summary>
    public const string MarkIcon = "mark_for_death";

    /// <summary>`ShowNemesisIcon`'s, by team (`:5417-5442`).</summary>
    private const string NemesisRed = "particle_nemesis_red", NemesisBlue = "particle_nemesis_blue";

    /// <summary>`TF_COND_MARKEDFORDEATH`, `_SILENT` and `TF_COND_PASSTIME_PENALTY_DEBUFF` (tf_shareddefs.h:720, :738, :809).</summary>
    private const int MarkedForDeath = 30, MarkedForDeathSilent = 48, PasstimePenalty = 119;

    /// <summary>`RUNE_STRENGTH` to `RUNE_SUPERNOVA`'s conditions, `GetConditionFromRuneType` (tf_shareddefs.h:2659-2681).</summary>
    private static readonly int[] RuneConditions = [90, 91, 92, 93, 94, 95, 96, 97, 103, 109, 110, 111];

    /// <summary>`s_pszRuneIcons`' type names, in `RuneTypes_t` order (tf_shareddefs.cpp:1961-1993).</summary>
    private static readonly string[] RuneNames =
        ["strength", "haste", "regen", "resist", "vampire", "reflect", "precision", "agility", "knockout", "king", "plague", "supernova"];

    /// <summary>Each rune's icon: [0] RED, [1] any other team, as `GetPowerupIconName` indexes it.</summary>
    private static readonly string[][] RuneIcons =
    [
        [.. System.Linq.Enumerable.Select(RuneNames, name => $"powerup_icon_{name}_red")],
        [.. System.Linq.Enumerable.Select(RuneNames, name => $"powerup_icon_{name}_blue")],
    ];

    /// <summary>Every icon this can carry: IT, the two nemesis icons, the mark, then the runes, RED's then BLU's.</summary>
    public static readonly IReadOnlyList<string> Icons =
        [ItIcon, NemesisRed, NemesisBlue, MarkIcon, .. RuneIcons[0], .. RuneIcons[1]];

    /// <summary>The map's nodes by index; null for a freed one.</summary>
    private readonly List<string?> _nodes = [];

    /// <summary>Freed nodes, the last freed on top.</summary>
    private readonly Stack<int> _free = new();

    /// <summary>`m_bIsDisplayingNemesisIcon` and `m_bIsDisplayingIconForIT`; and whether he is dormant.</summary>
    private bool _nemesis, _it, _dormant;

    /// <summary>`m_eDisplayingRuneIcon`; -1 for `RUNE_NONE`.</summary>
    private int _rune = -1;

    /// <summary>The icons in walk order, rebuilt only when they change.</summary>
    public IReadOnlyList<string> Names { get; private set; } = [];

    /// <summary>One update: `OnDataChanged`'s icons, then `ClientThink`'s.</summary>
    /// <param name="inputs">What the update reads.</param>
    public void Step(in OverheadInputs inputs)
    {
        _dormant = false;
        DataChanged(inputs);
        Think(inputs);
    }

    /// <summary>
    /// A tick out of the PVS: entering it stops every icon (`C_TFPlayer::SetDormant`, :4308-4330), and the think goes on
    /// regardless, so the rune and the mark come back on what he last held while nothing new arrives for the rest.
    /// </summary>
    /// <param name="inputs">What he last held.</param>
    /// <remarks>
    /// `CClientThinkList::PerformThinkFunction` asks nothing about dormancy (client_thinklist.cpp:186-220), and an effect
    /// made on a dormant owner is not itself dormant: `OwnerSetDormantTo` reaches only those already made
    /// (particle_property.cpp:465-475), and `CNewParticleEffect::DrawModel` does not ask (particles_new.cpp:509-539).
    /// </remarks>
    public void Dormant(in OverheadInputs inputs)
    {
        if (!_dormant)
        {
            _dormant = true;

            if (_nemesis)
            {
                ShowNemesis(false, team: null);
            }

            if (_it)
            {
                Toggle(ItIcon, false);
                _it = false;
            }

            UpdateMark(default, forceStop: true);
            UpdateRune(default, forceStop: true);
        }

        Think(inputs);
    }

    /// <summary>`ClientThink`'s icons (:6109-6111).</summary>
    private void Think(in OverheadInputs inputs)
    {
        UpdateRune(inputs, forceStop: false);
        UpdateMark(inputs, forceStop: false);
    }

    /// <summary>`OnDataChanged`'s icons, inside `if ( !IsHLTV() )` (:4557, :4564-4587).</summary>
    private void DataChanged(in OverheadInputs inputs)
    {
        if (!inputs.SourceTv)
        {
            bool it = inputs.IsIt && !inputs.IsLocal;

            if (it != _it)
            {
                Toggle(ItIcon, it);
                _it = it;
            }

            // The dueling icon is never shown here (see the remarks), so `!bShouldShowDuelingIcon` holds.
            bool nemesis = !_it && ShowsNemesis(inputs);

            if (nemesis != _nemesis)
            {
                ShowNemesis(nemesis, inputs.Team);
            }
        }
    }

    /// <summary>
    /// `IsNemesisOfLocalPlayer`: the local player's `IsPlayerDominatingMe( entindex() )`, his `m_bPlayerDominatingMe` read at
    /// the other player's entity index (tf_player_shared.cpp:7737-7756).
    /// </summary>
    /// <param name="nemeses">The local player's bits by entity index; null when none arrived.</param>
    /// <param name="entity">The other player.</param>
    /// <returns>Whether he dominates the local player.</returns>
    public static bool Dominates(System.UInt128? nemeses, int entity) =>
        nemeses is { } bits && entity is >= 0 and < 128 && ((bits >> entity) & System.UInt128.One) != System.UInt128.Zero;

    /// <summary>`ShouldShowNemesisIcon` (:9376-9395).</summary>
    private static bool ShowsNemesis(in OverheadInputs inputs) =>
        !inputs.HideDominationIcons &&
        !inputs.HasCustomModel &&
        inputs.NemesisOfLocal && inputs.Connected &&
        inputs.Alive && !inputs.Conditions.IsStealthed && !inputs.Conditions.Has(PlayerConditions.Disguised);

    /// <summary>`ShowNemesisIcon`: a team with no icon returns before the flag is set, so it is asked again (:5419-5441).</summary>
    private void ShowNemesis(bool show, int? team)
    {
        if (show)
        {
            string? icon = team switch
            {
                SceneTeams.Red => NemesisRed,
                SceneTeams.Blu => NemesisBlue,
                _ => null,
            };

            if (icon is null)
            {
                return;
            }

            Add(icon);
        }
        else
        {
            Remove(NemesisRed);
            Remove(NemesisBlue);
        }

        _nemesis = show;
    }

    /// <summary>`UpdatedMarkedForDeathEffect` (:5173-5199).</summary>
    private void UpdateMark(in OverheadInputs inputs, bool forceStop)
    {
        if (inputs.IsLocal)
        {
            return;
        }

        PlayerConditions conditions = inputs.Conditions;
        bool show = !forceStop && !conditions.IsStealthed && !conditions.Has(PlayerConditions.Disguised) &&
            (conditions.Has(MarkedForDeath) || conditions.Has(MarkedForDeathSilent) || conditions.Has(PasstimePenalty));

        Toggle(MarkIcon, show);
    }

    /// <summary>`UpdateRuneIcon` (:5205-5243).</summary>
    private void UpdateRune(in OverheadInputs inputs, bool forceStop)
    {
        if (inputs.IsLocal)
        {
            return;
        }

        int carrying = forceStop ? -1 : Carrying(inputs.Conditions);
        bool allowed = carrying >= 0 && !inputs.Conditions.IsStealthed;

        if (!allowed || forceStop || carrying != _rune)
        {
            foreach (string[] team in RuneIcons)
            {
                foreach (string icon in team)
                {
                    Remove(icon);
                }
            }

            _rune = -1;
        }

        if (allowed && carrying != _rune)
        {
            int? team = inputs.IsEnemy && inputs.Conditions.Has(PlayerConditions.Disguised) ? inputs.DisguiseTeam : inputs.Team;

            if (Add(RuneIcons[team == SceneTeams.Red ? 0 : 1][carrying]))
            {
                _rune = carrying;
            }
        }
    }

    /// <summary>`GetCarryingRuneType`: the first rune whose condition he has, or -1 (tf_player_shared.cpp:11498-11514).</summary>
    private static int Carrying(PlayerConditions conditions)
    {
        for (int rune = 0; rune < RuneConditions.Length; rune++)
        {
            if (conditions.Has(RuneConditions[rune]))
            {
                return rune;
            }
        }

        return -1;
    }

    private void Toggle(string icon, bool show)
    {
        if (show)
        {
            Add(icon);
        }
        else
        {
            Remove(icon);
        }
    }

    /// <summary>`AddOverheadEffect`: false when it is there already.</summary>
    private bool Add(string icon)
    {
        if (_nodes.Contains(icon))
        {
            return false;
        }

        if (_free.TryPop(out int node))
        {
            _nodes[node] = icon;
        }
        else
        {
            _nodes.Add(icon);
        }

        Rebuild();

        return true;
    }

    /// <summary>`RemoveOverheadEffect`, instantly.</summary>
    private void Remove(string icon)
    {
        int node = _nodes.IndexOf(icon);

        if (node < 0)
        {
            return;
        }

        _nodes[node] = null;
        _free.Push(node);
        Rebuild();
    }

    private void Rebuild()
    {
        List<string> names = new(_nodes.Count);

        foreach (string? icon in _nodes)
        {
            if (icon is not null)
            {
                names.Add(icon);
            }
        }

        Names = names;
    }
}

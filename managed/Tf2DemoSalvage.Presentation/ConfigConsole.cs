using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Runs a Source config the way the engine does: binds, aliases, and <c>+</c>/<c>-</c> buttons.
/// </summary>
/// <remarks>
/// **A TF2 config is a program, not a table, and that is why this class exists.** The owner's
/// requirement for D69 was that a real config work wholesale, and his reason names the difficulty
/// exactly:
///
/// > *"since we are going to take valve cfgs, we have to allow scripting or it wont work. valve
/// > configs are little state machines themselves"*
///
/// The first attempt read a config statically — follow the bind to its alias, take the first
/// command that named an action, record `w -> FlyForward`. That works on `config_default.cfg` and
/// falls apart on any config worth pasting, because `alias` is a **runtime** command that redefines
/// other aliases as it runs. In the standard null-cancelling movement script, `checkfwd` means
/// `none` before W is pressed and `+forward` afterwards. There is no single static answer to pick.
///
/// **What this implements, all read from `src/game/client/in_main.cpp` and `kbutton.h`:**
///
/// - a `+foo` command presses a button and `-foo` releases it, which is how every bind in Source
///   knows what a key release means;
/// - a button remembers **two** keys, so two keys bound to one action release independently;
/// - `state` carries impulse bits, so a key tapped inside one frame still counts for something;
/// - reading the state clears those bits, exactly as Valve's <c>key-&gt;state &amp;= 1</c> does.
///
/// Client ConVars are kept, and one set by a key at run time is announced through <see cref="ConVarSet"/> (D215),
/// including by `toggle`, `incrementvar` and `multvar`. Once <see cref="ReadSettingsFrom"/> attaches the viewer's
/// settings, every client ConVar is read from them and no longer kept here.
///
/// **One dispatcher runs every line** (B820, `engine.dll` `Cmd_ExecuteCommand`): a config's line, a key's bind and an
/// alias's body reach the same commands, `exec` among them, which reads <see cref="FindConfig"/>.
///
/// **Text is queued as TF2's command buffer queues it** (B832, B834): whole before it runs, in 8192 bytes, with `wait`
/// putting commands off to a later <see cref="Execute"/>.
///
/// **What it deliberately does not implement**: the several hundred game commands this viewer has no concept of. Those are skipped in
/// silence — a config is mostly commands we ignore, and objecting to them would reject every real
/// file. <see cref="Applied"/> counts what actually landed so a caller can tell "your config loaded
/// and bound nothing" from "your config did not load", which otherwise look identical.
/// </remarks>
public sealed class ConfigConsole
{
    /// <summary>How far an alias may expand before it is treated as a loop.</summary>
    /// <remarks>
    /// Null-cancel scripts nest three or four deep; a `+jumpbug` or class-switch script can go
    /// further. Twenty is far past anything a person writes by hand and still terminates instantly.
    /// A config that loops is a plausible mistake, and hanging at startup is the worst possible
    /// place to discover one.
    /// </remarks>
    public const int MaximumExpansion = 20;

    /// <summary>Stands in for the key number a command reached through an alias never gets.</summary>
    /// <remarks>
    /// Valve's equivalent is the literal <c>-1</c> that `KeyDown` leaves in `k` when handed an empty
    /// argument, which it then stores in `down[0]`. A sentinel rather than null because null already
    /// means "this slot is empty", and the two must stay distinguishable.
    /// </remarks>
    private const string AliasIssued = "-1";

    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Each key's binding, in the order keys were first bound: nothing removes a key (`unbind` and `unbindall`
    /// store `""`), and a rebind keeps its place, so <see cref="Bindings"/> gives an action to the first key bound to it by
    /// design rather than by a <see cref="Dictionary{TKey, TValue}"/>'s unspecified order (B823 review 9, Fable).</summary>
    private readonly OrderedDictionary<string, string> _binds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every key a <c>bind</c> line has set; it only grows. <see cref="Bound"/> and <see cref="Applied"/> count those whose
    /// binding is still a command — `unbind` and `unbindall` empty the binding, not this set — so the defaults
    /// <see cref="WithDefaults"/> seeds into <see cref="_binds"/> are never a config's (B823 reviews 5 and 6).
    /// </summary>
    private readonly HashSet<string> _configBound = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _conVars = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ViewerAction, Button> _buttons = [];

    /// <summary>The viewer's settings' value for a ConVar, once attached; see <see cref="ReadSettingsFrom"/>.</summary>
    private Func<string, string?>? _settingValue;

    /// <summary>What this viewer had on each key before any config ran.</summary>
    /// <remarks>
    /// **Kept separately so a config can override a key without destroying it**, which is the whole
    /// of <see cref="CommandFor"/>. Empty for a console built with <c>new</c>, so the conformance
    /// tests see the engine's behaviour with nothing underneath it.
    /// </remarks>
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached <see cref="Claimed"/>, invalidated whenever a bind changes.</summary>
    private HashSet<ViewerAction>? _claimed;

    /// <summary>Raised when a command runs that is an instant action rather than a held button.</summary>
    /// <remarks>
    /// Switching camera or cycling the spectated player happens once per press. Flight is held.
    /// The distinction is <see cref="HeldActions"/>, not the command's spelling — TF2 spells the
    /// camera switch `+jump` even though nothing about it is continuous.
    /// </remarks>
    public event EventHandler<ViewerActionEventArgs>? Triggered;

    /// <summary>Raised when a command run by a key sets a client ConVar (D215).</summary>
    public event EventHandler<ConVarSetEventArgs>? ConVarSet;

    /// <summary>Where `exec` reads a <c>cfg/…</c> path: its bytes, or null when missing. Null reads nothing.</summary>
    /// <remarks><see cref="LoadFrom"/> sets it to the search path its configs came from (<see cref="Tf2ConfigFiles.Finder"/>).</remarks>
    public Func<string, byte[]?>? FindConfig { get; set; }

    /// <summary>Where the console prints what TF2 prints to its own: `exec`'s refusals.</summary>
    public ILogger Log { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>Whether <see cref="Load"/> is running a config, as opposed to a key: a ConVar set at load is not announced (D215).</summary>
    private bool _loading;

    /// <summary>Whether launch-line text that fires its one-shots is running, its `exec`s included (B829).</summary>
    private bool _stuffing;

    /// <summary>
    /// How many of the keys <see cref="Bound"/> counts have a command, as the config bound it, that names something the
    /// console runs: a viewer action, a set of a client (non-replicated) ConVar or `sv_cheats`, an alias the dispatcher
    /// consults, or a console definer (B823 reviews 6 to 9).
    /// </summary>
    /// <remarks>
    /// **What it names, not what it changes.** An alias counts whatever its body, `alias none ""` included, because TF2 runs
    /// the alias and the bind owns the key; a definer counts whatever its arguments; an `incrementvar` or `multvar` with the
    /// wrong token count, which `CvarCommand` drops, and a refused cheat set still count (B823 reviews 8 and 9). This is the
    /// project's own diagnostic, so its rule is this one; the engine has no such count.
    /// </remarks>
    /// <remarks>
    /// **Computed on demand rather than counted as binds arrive, because the count depends on
    /// aliases that may not exist yet.** TF2 execs `config.cfg` before `autoexec.cfg`, and the binds
    /// are in the first while the aliases they name are in the second — so a counter incremented at
    /// bind time reports the movement binds as unrecognised and is wrong by exactly the amount that
    /// matters. Measured: 5 counted in file order against 13 actually reachable.
    ///
    /// That failure is invisible, which is the argument for not having it: the viewer would work
    /// correctly and its own diagnostic would say the config barely loaded.
    /// </remarks>
    public int Applied
    {
        get
        {
            int applied = 0;

            // The keys `Bound` counts: a config's (never a seeded default, B823 review 5), and not one emptied since
            // (`unbind`, `unbindall`), whatever default it falls back to.
            foreach (string key in _configBound)
            {
                // The config's own command, not `CommandFor`'s fallback to the viewer's default (B823 review 6), asked
                // `CommandFor`'s own question (review 7): a bind that does nothing here is bound and not applied.
                if (DoesSomething(_binds[key]))
                {
                    applied++;
                }
            }

            return applied;
        }
    }

    /// <summary>How many keys a <c>bind</c> line set that still hold a command, whether or not it means anything here.</summary>
    /// <remarks>
    /// **The pair of numbers is what lets a caller say something true.** A config full of `mat_*`
    /// and one that failed to load are both "loaded"; only `0 applied of 206 binds` versus
    /// `0 of 0` distinguishes them.
    ///
    /// **Keys, not `bind` lines, because <see cref="Applied"/> is per key** (B823 review 4, Fable, Sonnet): the log line is
    /// "{Applied} of {Bound} binds applied", so a key bound twice is one and a key `unbind` or `unbindall` emptied is none —
    /// after `unbindall`, the two it spares if a config bound them. The keys <see cref="WithDefaults"/> seeds are not a
    /// config's and are never counted (B823 review 5, Fable, Sonnet), so a config with no `bind` reads `0 of 0`.
    /// </remarks>
    public int Bound => _configBound.Count(key => _binds[key].Length > 0);

    /// <summary>Counts every <c>bind</c>, <c>unbind</c>, <c>unbindall</c> and <c>alias</c> run, so a host can tell its projection is stale.</summary>
    /// <remarks>
    /// **TF2 has one bind table, and its binding menu reads it** (*not read*; the menu is closed): a bind an alias makes at
    /// run time shows at once (B821). <see cref="Bindings"/> expands aliases, so an alias
    /// change moves it too. The defaults <see cref="WithDefaults"/> writes are not counted: they are the starting table.
    /// </remarks>
    public int BindsRevision { get; private set; }

    /// <summary>The actions that are held down rather than triggered once.</summary>
    /// <remarks>
    /// **This is also the set a host must SWALLOW**, and since 2026-08-26 it is the only copy of it
    /// (B208). `FreeFlight.FlightActions` listed the same seven independently, under a comment
    /// claiming they were "listed once so `IsFlightKey` and the console cannot disagree about what
    /// counts as flight" — they could, and nothing would have said so.
    ///
    /// The failure that comment describes is the reason it matters: **a key swallowed but never
    /// pressed into the console, or pressed but never swallowed, produces a camera that moves once
    /// and stops.**
    ///
    /// **`FlyWalk` is in the set, and it did not used to be** (D69). Shift was read straight off
    /// `Control.ModifierKeys`, on the grounds that a modifier's state is something the toolkit
    /// already knows. That stopped being true when the console took over the controls: `+speed` is
    /// a bound command like any other, so Shift has to be pressed INTO the console or the modifier
    /// never fires — and it fails silently, as a camera whose speed simply never changes.
    /// </remarks>
    public static IReadOnlySet<ViewerAction> HeldActions { get; } = new HashSet<ViewerAction>
    {
        ViewerAction.FlyForward,
        ViewerAction.FlyBack,
        ViewerAction.FlyLeft,
        ViewerAction.FlyRight,
        ViewerAction.FlyUp,
        ViewerAction.FlyDown,
        ViewerAction.FlyWalk,

        // `+showscores`/`-showscores`: the scoreboard shows only while the key is down, the same
        // shape as the flight keys — see `TfClientScoreBoardDialog`.
        ViewerAction.ShowScores,
    };

    /// <summary>Whether a key drives anything this console holds down.</summary>
    /// <param name="keyName">The key's Source name, such as <c>w</c> or <c>SPACE</c>.</param>
    /// <param name="bindings">What the keys are bound to.</param>
    /// <returns>Whether the key should be treated as held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    /// <remarks>
    /// **This was a loop inside `FreeFlight`, a WinForms helper in the viewer project** (B188, D90).
    /// It walked <see cref="HeldActions"/> comparing each against `KeyBindings.KeyFor` — two tables
    /// that live here, joined in a view. Turning a toolkit's key value into a NAME is genuinely the
    /// view's; deciding what the name means is not.
    ///
    /// **ANY held action is enough, not all of them.** `SPACE` is bound to both "fly up" and "switch
    /// camera mode" by default, exactly as TF2's jump is both jump and the spectator's mode switch.
    /// Requiring every action to be held would stop the free camera rising.
    ///
    /// **Why the window asks at all**: it must know whether to swallow the key before a control sees
    /// it. A key swallowed but never pressed into the console, or pressed but never swallowed, gives
    /// a camera that moves once and stops — which is why this answer and the console's own must come
    /// from one list rather than from two that agree by inspection.
    /// </remarks>
    public static bool IsHeldKey(string keyName, KeyBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (string.IsNullOrEmpty(keyName))
        {
            return false;
        }

        foreach (ViewerAction action in bindings.ActionsFor(keyName))
        {
            if (HeldActions.Contains(action))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A console bound the way the viewer ships, before any config is loaded.</summary>
    /// <returns>A console carrying <see cref="KeyBindings.Defaults"/>.</returns>
    /// <remarks>
    /// **Built from the same two tables the settings screen reads**, rather than from a config
    /// string written out beside them. A literal here would be a second copy of the defaults that
    /// nothing forces to agree with the first, and the way that fails is a viewer whose keys work
    /// until somebody edits `Defaults` and does not think to edit this.
    ///
    /// **The counters stay at zero deliberately.** <see cref="Bound"/> and <see cref="Applied"/>
    /// answer "what did the user's config do", and seeding the defaults did not come from a config.
    /// Counting them would make an empty config look like a successful one.
    /// </remarks>
    public static ConfigConsole WithDefaults()
    {
        ConfigConsole console = new();

        foreach ((ViewerAction action, string key) in KeyBindings.Defaults)
        {
            if (KeyBindings.Commands.TryGetValue(action, out string? command))
            {
                console._binds[key] = command;
                console._defaults[key] = command;
            }
        }

        return console;
    }

    /// <summary>What a key does, falling back to this viewer's default when the config's is a no-op.</summary>
    /// <remarks>
    /// **A key whose config binding means nothing here keeps whatever this viewer had on it**, and
    /// that rule was written after watching a real config disable three controls at once. Loading
    /// the owner's `config.cfg` logged:
    ///
    /// <code>
    /// no key reaches: ResetCamera, PlayPause, FlyFast
    /// </code>
    ///
    /// (Quoted as it was observed. That third action is <see cref="ViewerAction.FlyWalk"/> since
    /// B215, so a run today prints the new name.)
    ///
    /// (`playpause` became Valve's own `demo_togglepause` in B214 — "Toggles demo playback" — so
    /// only `resetcamera` is still ours among these three.)
    ///
    /// **`resetcamera` and `playpause` are this project's own command names.** TF2 has no concept of
    /// either, so no TF2 config can ever bind them — it simply uses `f` and `k` for its own purposes
    /// and the viewer's controls vanish. A config cannot express a preference about a feature the
    /// game does not have, so treating its silence as one is reading intent that is not there.
    ///
    /// **`+speed` is the same in practice.** TF2 has no sprint, so the command appears in
    /// essentially no config, while `bind "SHIFT" "+duck"` is ordinary — meaning fly-fast would lose
    /// its key for most players who paste a config in.
    ///
    /// **No conflict is possible, which is what makes this safe rather than a guess.** The fallback
    /// only applies when the config's command for that key does nothing in this viewer, so the key
    /// was going to be inert either way. A config that binds the key to something we *do* implement
    /// wins outright, and the action that used to live there is then genuinely unbound and reported
    /// as such by <see cref="Unbound"/>.
    /// </remarks>
    /// <remarks>
    /// **The fallback yields the moment the config gives the action a new home**, which is the
    /// refinement the first version missed. Binding <c>CTRL</c> to <c>+speed</c> and <c>SHIFT</c> to
    /// <c>+duck</c> is a player moving fly-fast, not losing it — so Shift must stop doing it, or the
    /// rebind leaves two keys answering to one action and the settings screen has to pick one
    /// arbitrarily. A conformance test caught that as a wrong key rather than as a crash.
    /// </remarks>
    private string? CommandFor(string key)
    {
        if (_binds.TryGetValue(key, out string? command) && DoesSomething(command))
        {
            return command;
        }

        if (!_defaults.TryGetValue(key, out string? fallback))
        {
            return command;
        }

        foreach (ViewerAction action in Expand(fallback, depth: 0))
        {
            if (Claimed.Contains(action))
            {
                // The config gave this action a key of its own. Ours is not needed and would be a
                // second answer to the same question.
                return command;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Whether a bound command names something the console runs: a viewer action, a set of a client (non-replicated)
    /// ConVar or `sv_cheats`, an alias the dispatcher consults, or a console definer (B823 reviews 7 to 9).
    /// </summary>
    /// <remarks>One question for <see cref="CommandFor"/>, which keeps such a command over the viewer's default, and for
    /// <see cref="Applied"/>, which counts it.</remarks>
    private bool DoesSomething(string command) => Expand(command, depth: 0).Count > 0 || SetsState(command);

    /// <summary>
    /// Whether a command line names a set of a client (non-replicated) ConVar or `sv_cheats`, an alias the dispatcher
    /// consults, or a console definer — what it names, not what it changes: any alias whatever its body, any definer
    /// whatever its arguments, an `incrementvar` or `multvar` with the wrong token count (which `CvarCommand` drops) and
    /// a refused cheat set all count (B823 reviews 8 and 9).
    /// </summary>
    /// <remarks>
    /// **An alias bind is the config's, whatever its body.** TF2 runs the alias; the bind owns the key and no viewer
    /// default stands behind it, so <see cref="CommandFor"/> taking it is parity and this is not narrowed.
    ///
    /// **A `toggle` is not a no-op because it names no action** (B547). The fallback above was written for binds that
    /// mean nothing here; `bind HOME "toggle cl_showpos"` means something, and fell back to `resetspeed`.
    /// </remarks>
    private bool SetsState(string commandLine)
    {
        foreach (string clause in SourceConfig.Clauses(commandLine))
        {
            IReadOnlyList<string> tokens = SourceConfig.Tokens(clause);

            if (tokens.Count == 0)
            {
                continue;
            }

            string verb = tokens[0];
            bool cvarCommand = verb.Equals("toggle", StringComparison.OrdinalIgnoreCase)
                || verb.Equals("incrementvar", StringComparison.OrdinalIgnoreCase)
                || verb.Equals("multvar", StringComparison.OrdinalIgnoreCase);

            // The same tests `Run` acts on: the console's own ConCommands (`exec` and `bind` included, B820 review 1, Fable),
            // an alias the dispatcher would consult, and a name `SetConVar` takes (`TakesSet`, the one test both ask: a client
            // ConVar or `sv_cheats`, B823 review 9) - which is how the console treats a typed line; a name the game does not
            // declare is no ConVar in the engine either.
            if (Definers.Contains(verb)
                || (ConsultsAlias(verb) && _aliases.ContainsKey(verb))
                || (cvarCommand && tokens.Count >= 2 && TakesSet(tokens[1]))
                || (tokens.Count >= 2 && TakesSet(verb)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Actions the loaded config binds a key to, ignoring this viewer's own defaults.</summary>
    /// <remarks>
    /// **Computed from the raw bind table on purpose**, because <see cref="CommandFor"/> consults it
    /// and consulting itself would not terminate. It is the answer to "did the config speak about
    /// this action at all", which is a question about the config alone.
    ///
    /// Rebuilt whenever a bind changes rather than on every keystroke: a config has a few hundred
    /// binds and a keystroke arrives sixty times a second.
    /// </remarks>
    private HashSet<ViewerAction> Claimed
    {
        get
        {
            if (_claimed is not null)
            {
                return _claimed;
            }

            _claimed = [];

            foreach ((string key, string command) in _binds)
            {
                if (_defaults.TryGetValue(key, out string? ours) && ours == command)
                {
                    // Untouched by the config; it says nothing about this action.
                    continue;
                }

                foreach (ViewerAction action in Expand(command, depth: 0))
                {
                    _claimed.Add(action);
                }
            }

            return _claimed;
        }
    }

    /// <summary>Executes a config's text, top to bottom.</summary>
    /// <param name="text">The config's contents.</param>
    /// <remarks>
    /// **Order matters and later wins**, because that is what the engine does — a config is
    /// executed, not merged. Calling this again layers a second file over the first, which is how
    /// `autoexec.cfg` after `config.cfg` behaves.
    /// </remarks>
    public void Load(string? text) => AtStartup(stuffing: false, () => FromOutside(() => RunFile(text ?? string.Empty, depth: 0)));

    /// <summary>Runs launch-line text as `stuffcmds` does: straight to `Cbuf_AddText`, not through a file's line reader.</summary>
    /// <param name="text">The commands.</param>
    /// <remarks>
    /// `Cmd_Exec_f`'s line reader (its 1023-byte cap, its `\"` and `\\` unescape) is a file's (B820 review 8, Sonnet); the
    /// launch line reaches the buffer whole: `stuffcmds` (`engine.dll` build 1007 FUN_1801683d0, B824) pastes every
    /// `+command` into one text and adds it once (B830), so the viewer hands its whole launch line here in one call.
    /// </remarks>
    public void Stuff(string? text) => Stuff(text, oneShots: true);

    /// <summary>Runs launch-line text as <see cref="Stuff(string?)"/> does, its one-shots fired or not.</summary>
    /// <param name="text">The commands.</param>
    /// <param name="oneShots">
    /// Whether its one-shots fire: they do on the launch (B829; `stuffcmds` runs `+screenshot` like any command, the owner's
    /// pick), its `exec`s' included, and not on the viewer's replay of the line after a game folder is chosen, which is no
    /// second launch.
    /// </param>
    public void Stuff(string? text, bool oneShots) =>
        AtStartup(oneShots, () => FromOutside(() => AsCommand(() => RunHere(text ?? string.Empty, key: null, depth: 0))));

    /// <summary>
    /// Runs one command typed with budgets of its own (B820 review 5, Fable): a key, each line of a config
    /// <see cref="Load"/> reads, and the whole launch line, whose `+command`s share one set (B830). Put back after, so a
    /// handler that presses a key or loads a config inside a command leaves that command's budgets as they were.
    /// </summary>
    private void AsCommand(Action run)
    {
        Budget outer = _budget;
        _budget = new Budget(new CommandBudget());

        try
        {
            run();
        }
        finally
        {
            _budget = outer;
        }
    }

    /// <summary>`ARGS_BUFFER_LENGTH`: the bytes every queued command shares, each with its terminator (`commandbuffer.h:92`).</summary>
    /// <remarks>`engine.dll` x64 build 1007's `CCommandBuffer` constructor (FUN_180294fb0) sets its limit to 0x2000 (B834).</remarks>
    public const int ArgumentBufferLength = 8192;

    /// <summary>One command `AddText` queued: its size with the terminator, its text, the text from it on, and its tick.</summary>
    private readonly record struct Added(int Size, string Command, int Tick);

    /// <summary>
    /// What one `CCommandBuffer::AddText` call queues (`src/tier1/commandbuffer.cpp:209-247`, B834, B832), read in `engine.dll`
    /// x64 build 1007 as FUN_1802952a0 and FUN_180295740: each command named, in order, at the tick a `wait` before it in
    /// the text puts it; stopping, and dropping the rest, at a command of 511 bytes or more (`InsertCommand`'s
    /// `Command too long`) or at one the 8192-byte argument buffer cannot take beside what is already queued (`:129-134`:
    /// `Compact` leaves only the queued commands, so the test is against those).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="queued">The bytes already queued.</param>
    /// <param name="tick">The tick the text is added at.</param>
    /// <param name="waitEnabled">Whether `wait` is taken here rather than queued as a command (`IsWaitEnabled`).</param>
    /// <returns>The commands queued; the text from a refused command on, when one was refused for its length; whether
    /// `AddText` returned false, which its caller says as `Cbuf_AddText: buffer overflow`.</returns>
    private static (List<Added> Queued, string? TooLong, bool Refused) Insert(string text, int queued, int tick, bool waitEnabled)
    {
        List<Added> added = [];

        foreach ((int length, string command, string rest) in BufferCommands(text))
        {
            IReadOnlyList<string> tokens = SourceConfig.Tokens(command);

            // `ParseArgV0` finding an empty first token skips the command before `InsertCommand` measures it (`:230-232`):
            // a blank command, or one whose first token is `""`, is no command of any length (B820 reviews 6 and 7, Fable).
            if (!NamesACommand(tokens))
            {
                continue;
            }

            // `wait`, any case, moves the commands after it in this text on by its argument, or by one tick with none
            // (`:235-240`; `m_nWaitDelayTicks` is 1, FUN_180166970).
            if (waitEnabled && string.Equals(tokens[0], WaitCommand, StringComparison.OrdinalIgnoreCase))
            {
                tick += WaitTicks(command, rest);
                continue;
            }

            if (IsTooLong(length))
            {
                return (added, rest, true);
            }

            // `m_nArgSBufferSize + nCommandSize + 1 > m_nMaxArgSBufferLength`, after `Compact` (`:129-134`).
            if (queued + length + 1 > ArgumentBufferLength)
            {
                return (added, null, true);
            }

            queued += length + 1;
            added.Add(new Added(length + 1, command, tick));
        }

        return (added, null, false);
    }

    /// <summary>The command that delays the rest of its text (`commandbuffer.cpp:235`).</summary>
    private const string WaitCommand = "wait";

    /// <summary>
    /// The ticks a `wait` adds: `atoi( pArgS )`, the text after its first token, or one when nothing follows it
    /// (`ParseArgV0`, `commandbuffer.cpp:86-87`). So `wait 3` is three, `wait` one, and `wait x` or `wait ` none.
    /// </summary>
    /// <param name="command">The `wait` command.</param>
    /// <param name="fromHere">The text from it on: `pArgS` points into the whole text, so `atoi` reads past the command's
    /// end, through a line feed's white space, and `wait \n5` waits five (B832 review 1, Fable).</param>
    private static int WaitTicks(string command, string fromHere)
    {
        int at = 0;

        while (at < command.Length && command[at] <= ' ')
        {
            at++;
        }

        // The first token is `wait`, bare or quoted, so it ends four bytes on, or six with its quotes.
        at += command[at] == '"' ? WaitCommand.Length + 2 : WaitCommand.Length;

        return at >= command.Length ? 1 : Core.Primitives.CStdlib.Atoi(fromHere.AsSpan(at));
    }

    /// <summary>
    /// Whether one `AddText` call queues the first named command of a launch line, and whether it queues the whole of it,
    /// beside what the launch line's earlier lines queued (B830, B834).
    /// </summary>
    /// <param name="text">The line, as it would be added.</param>
    /// <param name="queued">The bytes the launch line's earlier lines queued, moved on by this one's.</param>
    /// <param name="whole">Whether every command in it is queued.</param>
    /// <returns>False only when a command is refused before any is queued; true when the text names none.</returns>
    /// <remarks>
    /// The same split, the same skip of a command with no name, and the same bounds as the console's run, with nothing else
    /// queued: `stuffcmds` (`engine.dll` build 1007 FUN_1801683d0) pastes the whole launch line into a growable buffer and adds
    /// it in one call. The commands before a refused one are queued and run, so a set first on its line runs even when a later
    /// command on that line is refused.
    /// </remarks>
    internal static bool Adds(string text, ref int queued, out bool whole)
    {
        (List<Added> added, _, bool refused) = Insert(text, queued, tick: 0, waitEnabled: true);
        queued += added.Sum(command => command.Size);
        whole = !refused;

        return added.Count > 0 || !refused;
    }

    /// <summary>The bytes the queued commands hold in the argument buffer (B834).</summary>
    private int _queuedBytes;

    /// <summary>`m_nCurrentTick`: the tick the console's commands run at now (B832).</summary>
    private int _tick;

    /// <summary>`m_nLastTickToProcess`: the last tick the commands running now may run.</summary>
    private int _lastTick;

    /// <summary>`m_bWaitEnabled`, as the last <see cref="Execute"/> set it.</summary>
    private bool _waitEnabled = true;

    /// <summary>`m_bIsProcessingCommands`: an <see cref="Execute"/>, or the console's run of new text, is running.</summary>
    private bool _processing;

    /// <summary>
    /// The budgets one command typed spends, and everything it runs (B820): files exec'd and clauses run. A key, each line
    /// of a config <see cref="Load"/> reads, and the whole launch line each begin one.
    /// </summary>
    private sealed class CommandBudget
    {
        /// <summary>Files exec'd; see <see cref="MaximumExecs"/>.</summary>
        public int Execs { get; set; }

        /// <summary>Clauses run; see <see cref="MaximumCommandClauses"/>.</summary>
        public int Clauses { get; set; }
    }

    /// <summary>The budgets a line spends: its command's, and its own alias runs (B820 review 3: per file line).</summary>
    private sealed class Budget(CommandBudget command)
    {
        /// <summary>The command's budgets.</summary>
        public CommandBudget Command { get; } = command;

        /// <summary>Aliases run; see <see cref="MaximumAliasRuns"/>.</summary>
        public int AliasRuns { get; set; }
    }

    /// <summary>Whether a key's commands did something, which <see cref="KeyDown"/> answers (B547).</summary>
    private sealed class Answer
    {
        /// <summary>Whether one did.</summary>
        public bool Acted { get; set; }
    }

    /// <summary>
    /// A queued command (`Command_t`): its tick, its size with the terminator, its text, and what it runs with: the key
    /// that added it, its alias depth, the context it was added in, its budgets, the key it answers to, the call that
    /// added it, and, for the command that stands for text from outside (an `exec` or `stuffcmds`), the run that adds it.
    /// </summary>
    private sealed record Entry(
        int Tick,
        int Size,
        string Command,
        string? Key,
        int Depth,
        bool Loading,
        bool Stuffing,
        bool ViewerIssued,
        bool SavedLayer,
        Budget Budget,
        Answer? Answer,
        int Adding,
        Action? Run);

    /// <summary>`m_Commands`: every queued command, one list, in the order the engine links them (B840).</summary>
    private readonly LinkedList<Entry> _queue = new();

    /// <summary>`m_hNextCommand`: the head when the last command was taken; text added while processing goes before it.</summary>
    private LinkedListNode<Entry>? _next;

    /// <summary>The budgets the command running now spends.</summary>
    private Budget _budget = new(new CommandBudget());

    /// <summary>The key the command running now answers to, if any.</summary>
    private Answer? _answer;

    /// <summary>The call from outside that added the command running now, numbered, so a throw drops what it added.</summary>
    private int _adding;

    /// <summary>The last call from outside numbered.</summary>
    private int _addings;

    /// <summary>
    /// One `Cbuf_Execute` (`engine.dll` x64 build 1007 FUN_180166a10, B832): sets whether `wait` is taken from
    /// <c>sv_allow_wait_command</c>, runs every command queued for this tick or before, in order, then moves the tick on.
    /// </summary>
    /// <param name="waitAllowed">Whether <c>sv_allow_wait_command</c> is on (a replicated ConVar whose default is 1).</param>
    /// <remarks>
    /// TF2 runs one at the top of every frame and one in each host tick's input step (`_Host_RunFrame`, FUN_1801a5eb0;
    /// `_Host_RunFrame_Input`, FUN_1801a74d0). Inside another it does nothing, as `IsProcessingCommands` guards it.
    /// </remarks>
    public void Execute(bool waitAllowed)
    {
        // Nor inside the console's run of new text, which stands in for one (B832 review 3, Fable).
        if (_processing)
        {
            return;
        }

        _waitEnabled = waitAllowed;
        int last = _tick;

        try
        {
            Processing(last, () => Pump(until: null));
        }
        finally
        {
            _tick = last + 1;
        }
    }

    /// <summary>
    /// Runs as one `Cbuf_Execute` does, between `BeginProcessingCommands` and `EndProcessingCommands` (`:277-288,
    /// 376-381`): commands processing, the last tick set, the next-command handle at the head.
    /// </summary>
    /// <param name="last">The last tick to run.</param>
    /// <param name="run">What runs.</param>
    private void Processing(int last, Action run)
    {
        (_processing, _lastTick, _next) = (true, last, _queue.First);

        try
        {
            run();
        }
        finally
        {
            (_processing, _next) = (false, null);
        }
    }

    /// <summary>
    /// Runs new text from outside every command as the next `Cbuf_Execute` would, at once (B832): the command that adds it
    /// (an `exec`, `stuffcmds`, or a key's `Key_Event`) is linked by tick at the current tick, and the queue runs from the
    /// head, so what is queued ahead of it runs first and what it adds runs where the engine runs it. The tick is left as
    /// it was, as the call that stands for this one has not ended. Inside a command, the text runs where it stands, as
    /// `Cmd_Exec_f` runs a file line. A throw drops what this call added and has not run by now.
    /// </summary>
    /// <param name="add">Adds the text, and runs it where it stands.</param>
    /// <param name="linkedByTick">Whether the text is added as while no commands are processing, a key's (`Key_Event`), so
    /// it is linked by tick among what is queued, and the run then takes the queue as it stands.</param>
    private void FromOutside(Action add, bool linkedByTick = false)
    {
        if (_processing)
        {
            add();
            return;
        }

        int now = _tick;
        int adding = _adding;
        _adding = ++_addings;

        try
        {
            if (linkedByTick)
            {
                add();
                Processing(now, () => Pump(until: null));
            }
            else
            {
                // **Where TF2's `exec` or `stuffcmds` stands** (B840 review 2, Fable): that command is linked by tick at the
                // current tick, behind what is queued for it, and its run adds the text where it stands. One command with
                // no bytes stands for it here, so what runs before it, and at what tick, is the engine's.
                Link(new Entry(
                    now, 0, string.Empty, null, 0, _loading, _stuffing, _viewerIssued, _savedLayer, _budget, _answer, _adding, add));
                Processing(now, () => Pump(until: null));
            }
        }
        catch (Exception)
        {
            DropUnrun(_adding, now);
            throw;
        }
        finally
        {
            (_tick, _adding) = (now, adding);
        }
    }

    /// <summary>Adds a text and runs it where it stands, as `Cmd_Exec_f` runs one file line: until the head is the
    /// command that was next before it was added.</summary>
    /// <param name="text">The text.</param>
    /// <param name="key">The key that added it, if any.</param>
    /// <param name="depth">Its alias depth.</param>
    /// <remarks>
    /// The handle is `GetNextCommandHandle`'s, which is the head straight after `exec` was taken; text added now goes
    /// before it (`InsertImmediateCommand`), so the run ends there.
    /// </remarks>
    private void RunHere(string text, string? key, int depth)
    {
        LinkedListNode<Entry>? until = _next;
        _ = AddText(text, key, depth);
        Pump(until);
    }

    /// <summary>The server ConVar that allows `wait` (`engine.dll` build 1007 FUN_18000b680: replicated, default 1).</summary>
    public const string AllowWaitConVar = "sv_allow_wait_command";

    /// <summary>Whether a value of <see cref="AllowWaitConVar"/> allows `wait`: its integer not zero, as `Cbuf_Execute`
    /// reads `m_nValue`; its default, 1, when the demo's server never set it.</summary>
    /// <param name="value">The value the demo's server set, or null.</param>
    /// <returns>True when `wait` is taken.</returns>
    public static bool WaitAllowed(string? value) => value is null || ConVarNumber.GetBool(value);

    /// <summary>
    /// `DequeueNextCommand` in a loop (`commandbuffer.cpp:294-329`): runs the queue's head while it is due by
    /// <see cref="_lastTick"/>, each at its own tick, the next-command handle moved to the new head after each, and stops
    /// at the first command not due or at <paramref name="until"/>, which is how `Cmd_Exec_f` (`engine.dll` build 1007
    /// FUN_1801672f0) runs one file line: until the head is the command that followed `exec`.
    /// </summary>
    /// <param name="until">The command to stop before, or null to run all that is due.</param>
    private void Pump(LinkedListNode<Entry>? until)
    {
        while (_queue.First is { } head && head != until && head.Value.Tick <= _lastTick)
        {
            _queue.RemoveFirst();
            _next = _queue.First;
            RunEntry(head.Value);
        }
    }

    /// <summary>Runs one dequeued command at its tick, in the context it was added in (`Cmd_ExecuteCommand`).</summary>
    private void RunEntry(Entry entry)
    {
        _queuedBytes -= entry.Size;
        _tick = entry.Tick;

        (bool loading, bool stuffing, bool viewerIssued, bool savedLayer, Budget budget, Answer? answer, int adding) =
            (_loading, _stuffing, _viewerIssued, _savedLayer, _budget, _answer, _adding);
        (_loading, _stuffing, _viewerIssued, _savedLayer, _budget, _answer, _adding) =
            (entry.Loading, entry.Stuffing, entry.ViewerIssued, entry.SavedLayer, entry.Budget, entry.Answer, entry.Adding);

        try
        {
            // The command that stands for text added from outside: its run adds the text where it stands (B840 review 2).
            if (entry.Run is { } run)
            {
                run();
                return;
            }

            bool acted = false;
            _ = RunClause(entry.Command, entry.Key, entry.Depth, ref acted);

            if (acted && entry.Answer is { } asked)
            {
                asked.Acted = true;
            }
        }
        finally
        {
            (_loading, _stuffing, _viewerIssued, _savedLayer, _budget, _answer, _adding) =
                (loading, stuffing, viewerIssued, savedLayer, budget, answer, adding);
        }
    }

    /// <summary>
    /// `InsertCommand`'s linking (`commandbuffer.cpp:148-155`): while commands are processing, one due now goes before the
    /// next command (`InsertImmediateCommand`, `:111-114`), so a text added then runs ahead of the rest in order; any
    /// other is linked from the head before the first command with a later tick (`InsertCommandAtAppropriateTime`,
    /// `:95-105`).
    /// </summary>
    private void Link(Entry entry)
    {
        if (_processing && entry.Tick <= _tick)
        {
            _ = _next is { List: not null } next ? _queue.AddBefore(next, entry) : _queue.AddLast(entry);
            return;
        }

        for (LinkedListNode<Entry>? at = _queue.First; at is not null; at = at.Next)
        {
            if (at.Value.Tick > entry.Tick)
            {
                _ = _queue.AddBefore(at, entry);
                return;
            }
        }

        _ = _queue.AddLast(entry);
    }

    /// <summary>The one fresh set of budgets a text's commands put off to a tick share.</summary>
    private static Budget LaterBudget(Dictionary<int, Budget> byTick, int tick)
    {
        if (!byTick.TryGetValue(tick, out Budget? budget))
        {
            budget = new Budget(new CommandBudget());
            byTick[tick] = budget;
        }

        return budget;
    }

    /// <summary>Drops what one call added that is still queued and due by a tick, releasing its bytes: what a throw left.</summary>
    /// <param name="adding">The call.</param>
    /// <param name="upTo">The last tick it was to run.</param>
    private void DropUnrun(int adding, int upTo)
    {
        for (LinkedListNode<Entry>? at = _queue.First; at is not null;)
        {
            LinkedListNode<Entry>? next = at.Next;

            if (at.Value.Adding == adding && at.Value.Tick <= upTo)
            {
                _queuedBytes -= at.Value.Size;
                _queue.Remove(at);
            }

            at = next;
        }
    }

    /// <summary>
    /// Whether a split command is a command at all: `ParseArgV0` finding an empty first token skips it before
    /// `InsertCommand` measures it (`src/tier1/commandbuffer.cpp:230-232`).
    /// </summary>
    private static bool NamesACommand(IReadOnlyList<string> tokens) => tokens.Count > 0 && tokens[0].Length > 0;

    /// <summary>Whether `InsertCommand` refuses a command this long (`src/tier1/commandbuffer.cpp:120-126`).</summary>
    private static bool IsTooLong(int length) => length >= MaximumCommandLength - 1;

    /// <summary>Runs startup text: a config or the launch line.</summary>
    /// <remarks>
    /// A config read at startup or on a folder change, its `exec`s included; a key's `exec` is a run-time command.
    /// Restored rather than cleared, so a load from a handler inside a load leaves the outer one quiet (B820 review 2).
    /// Each config line's budgets are its own, the whole launch line shares one set (B830), and the outer command's are put
    /// back by `AsCommand` (B820 reviews 3-5).
    /// Startup text is never the viewer's own key, even run from a handler inside one (B820 review 7, Fable).
    /// Each load says whether its one-shots fire, restored after it, so a config a handler loads inside the launch line
    /// fires none and the rest of the line still does (B829).
    /// </remarks>
    /// <param name="stuffing">Whether the text is the launch line run for its one-shots (B829).</param>
    /// <param name="run">The load.</param>
    private void AtStartup(bool stuffing, Action run)
    {
        (bool loading, bool viewerIssued, bool wasStuffing) = (_loading, _viewerIssued, _stuffing);
        (_loading, _viewerIssued, _stuffing) = (true, false, stuffing);

        try
        {
            run();
        }
        finally
        {
            (_loading, _viewerIssued, _stuffing) = (loading, viewerIssued, wasStuffing);
        }
    }

    /// <summary>Runs a file's lines through the one dispatcher, each before the next is read, as `Cmd_Exec_f` does (B820).</summary>
    private void RunFile(string text, int depth)
    {
        // A nested file's lines take an alias budget each and their command's other budgets, and the line that exec'd it
        // keeps its own (B820 reviews 3 and 6).
        Budget outer = _budget;

        try
        {
            RunLines(text, depth);
        }
        finally
        {
            _budget = outer;
        }
    }

    /// <summary>
    /// One `AddText` call's commands, split and measured as `CCommandBuffer::GetNextCommandLength`
    /// (`src/tier1/commandbuffer.cpp:163-203`): at a `;` outside quotes or any line feed, quotes counted, a `//` comment and
    /// what follows it not, and everything else counted as it stands, leading spaces and a CR included, in bytes.
    /// </summary>
    /// <returns>Each command with its measured length, its text, and the text from it on (what TF2 prints when refusing it).</returns>
    private static IEnumerable<(int Length, string Command, string FromHere)> BufferCommands(string text)
    {
        int at = 0;

        while (at < text.Length)
        {
            int start = at;
            int length = 0;
            bool quoted = false;
            bool commented = false;
            System.Text.StringBuilder command = new();

            while (at < text.Length)
            {
                char c = text[at];

                if (!commented)
                {
                    if (!quoted && c == '/' && at < text.Length - 1 && text[at + 1] == '/')
                    {
                        commented = true;
                        at += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        quoted = !quoted;
                    }
                    else if (!quoted && c == ';')
                    {
                        break;
                    }
                }

                if (c == '\n')
                {
                    break;
                }

                if (commented)
                {
                    at++;
                    continue;
                }

                length += Core.Primitives.Utf8Escaped.ByteCount(text, at);
                command.Append(c);
                at++;
            }

            // `if ( nCommandLength <= 0 ) continue;` — an empty command is no command (`:224-225`).
            if (length > 0)
            {
                yield return (length, command.ToString(), text[start..]);
            }

            at++;
        }
    }

    /// <summary>
    /// A file's lines as `Cmd_Exec_f` reads them, each handed to `Cbuf_AddText` on its own (`engine.dll` build 1007 FUN_180177310, read
    /// in B820 review 7): a line ends at any control byte but tab, so a CRLF line's CR ends it and is not counted; leading
    /// spaces are kept and leading tabs and non-ASCII characters dropped (the skip between lines is a signed `char` compare);
    /// a line is cut at 1023 bytes and the rest read as the next; and a line that is not a `bind` (the word
    /// at the start once spaces and tabs are skipped, with a space or tab at byte 4 of the line) has `\"` made `"` and `\\`
    /// made `\`.
    /// </summary>
    private static IEnumerable<string> ExecLines(string text)
    {
        const int MaximumLine = 0x3ff;
        int at = 0;

        // `Cmd_Exec_f` runs lines while one's `strlen` is above 0, and the first byte is copied whatever it is, so a file that
        // begins with a NUL runs nothing (B820 review 9, Fable).
        if (text.Length > 0 && text[0] == '\0')
        {
            yield break;
        }

        while (at < text.Length)
        {
            System.Text.StringBuilder line = new();
            int bytes = 0;

            // The first byte is copied whatever it is; the copy then stops at a control byte but tab, or at the cap.
            do
            {
                char c = text[at];
                int size = Core.Primitives.Utf8Escaped.ByteCount(text, at);

                if (bytes + size > MaximumLine)
                {
                    break;
                }

                line.Append(c);
                bytes += size;
                at++;
            }
            while (at < text.Length && (text[at] >= ' ' || text[at] == '\t'));

            string read = line.ToString();
            string body = read.TrimStart(' ', '\t');
            bool isBind = body.StartsWith("bind", StringComparison.Ordinal) && read.Length > 4 && read[4] is ' ' or '\t';

            yield return isBind ? read : read.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

            // The next line starts at the next byte above 0x1f as a SIGNED char (`'\x1f' < (char)b`): a tab is skipped here, so a
            // line's leading tabs are not counted, and so is any byte of 0x80 or more, a non-ASCII character's. A NUL, where
            // the copy stopped or the skip meets it, is the end of the file: nothing after it is read (B820 review 8, Fable).
            while (at < text.Length && text[at] != '\0' && (text[at] < ' ' || text[at] >= (char)0x80))
            {
                at++;
            }

            if (at < text.Length && text[at] == '\0')
            {
                yield break;
            }
        }
    }

    /// <summary>The lines of <see cref="RunFile"/>.</summary>
    private void RunLines(string text, int depth)
    {
        foreach (string line in ExecLines(text))
        {
            // **Each file line is its own `Cbuf_AddText`** (B830 review 4, Fable): `Cmd_Exec_f` (`engine.dll` build 1007
            // FUN_1801672f0) adds every line through FUN_180166e30, which says `Cbuf_AddText: buffer overflow` when `AddText`
            // refuses one of its commands, and runs the line's commands before the next line is read (B834: so only one line
            // of a file is queued at a time, beside whatever the text that ran `exec` still holds).
            // The alias budget is per file line (B820 review 3, Fable, Sonnet): one `exec` line pulls in a whole config
            // tree, whose lines each run their aliases as a line of their own does. The `exec` budget stays per command.
            // Run where it stands, as `Cmd_Exec_f` runs each line until the head is the command after `exec` (B840).
            if (depth == 0)
            {
                AsCommand(() => RunHere(line, key: null, depth));
                continue;
            }

            _budget = new Budget(_budget.Command);
            RunHere(line, key: null, depth);
        }
    }

    /// <summary>Presses a key, running whatever it is bound to.</summary>
    /// <param name="key">The key's Source name, such as <c>w</c> or <c>MOUSE1</c>.</param>
    /// <returns>
    /// Whether the bind did something - set a ConVar, ran an alias, moved a button, or raised one of the viewer's own
    /// one-shots, which the host performs from <see cref="Triggered"/> (B825) - so the key is the bind's (B547). False for
    /// nothing bound and a command this viewer does not implement.
    /// </returns>
    public bool KeyDown(string key)
    {
        // Stryker disable once : a mutant that empties the guard body leaves 'command'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (CommandFor(key ?? string.Empty) is not { } command)
        {
            return false;
        }

        // **The viewer's own default keys are its own feature, like its menus** (D253, D246): its built-in `mat_fullbright`
        // binds work whatever `sv_cheats` says. A bind the watcher's config made is TF2's command and meets TF2's refusal.
        // The defaults sit in the binds too (`WithDefaults`), so "built in" is the command being this key's default.
        bool builtIn = _defaults.TryGetValue(key!, out string? fallback) && string.Equals(fallback, command, StringComparison.Ordinal);

        // Restored rather than cleared, so a key pressed from a handler inside a built-in key's command leaves that command
        // built in (B820 review 6, Fable).
        bool viewerIssued = _viewerIssued;
        _viewerIssued = builtIn;

        try
        {
            // The key travels with the command because a button tracks WHICH keys hold it — see
            // `KeyDown( kbutton_t *b, const char *c )`, where `c` is the key number.
            // `Key_Event` (`engine.dll` build 1007 FUN_1801b6090) adds the bind through FUN_180166500, which says the
            // overflow line when `AddText` refuses it. No bind's own text reaches 511 bytes (B835 reviews 1 and 2, Fable,
            // Sonnet): `bind` stores its value from a statement under 511 whose prefix is at least 5 bytes (`bind'`), so the
            // value is at most 505, and ` %i` adds at most 4 (509). So only a full argument buffer refuses it (B834).
            return RunKey(KeyText(command, key!), key!);
        }
        finally
        {
            _viewerIssued = viewerIssued;
        }
    }

    /// <summary>
    /// The text `Key_Event` (`engine.dll` build 1007 FUN_1801b6090) adds for a bind: a `+` bind as `"%s %i"` with the key's
    /// `ButtonCode_t`, anything else as it stands (B832 review 1, Fable). Its bytes are what the argument buffer holds, and
    /// the number is the last command's last argument, as in TF2. A chord, which TF2 has no code for, adds none.
    /// </summary>
    /// <param name="command">The bind; its release is this text with the `+` made `-`.</param>
    /// <param name="key">The key.</param>
    private static string KeyText(string command, string key) =>
        command.StartsWith('+') && ButtonCodeNames.CodeOf(key) is { } code
            ? string.Create(CultureInfo.InvariantCulture, $"{command} {code}")
            : command;

    /// <summary>Runs a key's text, a command of its own, and answers whether it did something (B547).</summary>
    /// <remarks>
    /// **A key's text is linked into the queue by tick, every command of it** (B832 reviews 1 to 3, Sonnet, Fable):
    /// `Key_Event` adds it while no commands are processing, so `InsertCommand` links each by its tick among those already
    /// queued (`commandbuffer.cpp:95-105, 148-151`), and the next `Cbuf_Execute` runs the queue in that order, which the
    /// console runs at once. A key a handler presses inside a command runs inside it, in order. It answers for its own
    /// commands, their aliases' included, and for one it put off to a later tick.
    /// </remarks>
    private bool RunKey(string text, string key)
    {
        Answer answer = new();
        Answer? outer = _answer;
        _answer = answer;
        bool putOff = false;

        try
        {
            AsCommand(() =>
            {
                if (_processing)
                {
                    LinkedListNode<Entry>? until = _next;
                    putOff = AddText(text, key, depth: 0);
                    Pump(until);
                }
                else
                {
                    FromOutside(() => putOff = AddText(text, key, depth: 0), linkedByTick: true);
                }
            });
        }
        finally
        {
            _answer = outer;
        }

        return answer.Acted || putOff;
    }

    /// <summary>Whether the command running now came from the viewer's own default binds (D253).</summary>
    private bool _viewerIssued;

    /// <summary>Releases a key, running the <c>-</c> half of whatever it is bound to.</summary>
    /// <param name="key">The key's Source name.</param>
    /// <remarks>
    /// **The release command is the bind with its first character changed to <c>-</c>, and nothing
    /// else.** Two independent binding layers in the SDK build it the same way —
    /// `in_sixense_gesture_bindings.cpp`:
    ///
    /// <code>
    /// if( press_command_str[0] == '+' )
    /// {
    ///     binding.m_pDeactivateCommand = strdup( press_command_str.String() );
    ///     binding.m_pDeactivateCommand[0] = '-';
    /// }
    /// </code>
    ///
    /// and `in_steamcontroller.cpp`, which writes `cmdbuf[0] = '-';` over a copy of the whole
    /// command. Both test only `[0]` and both write only `[0]`.
    ///
    /// **Two consequences fall out of that, and both are reproduced deliberately.** A bind that does
    /// not start with <c>+</c> runs *nothing* on release — the Steam controller path guards the
    /// whole call with <c>|| state.cmd[0] == '+'</c>. And a compound bind like
    /// <c>"+forward; +moveright"</c> releases as <c>"-forward; +moveright"</c>, so the second button
    /// stays down for ever. That is a real Source footgun, it is why competitive configs wrap
    /// compound binds in aliases, and a viewer that quietly fixed it would behave differently from
    /// the game the config was written for.
    /// </remarks>
    public void KeyUp(string key)
    {
        // Stryker disable all : the condition spans two lines, and a mutant that empties the guard
        // body leaves 'command' unassigned at its use below (CS0165), and Safe Mode then drops
        // every mutation in this method — B410.
        if (CommandFor(key ?? string.Empty) is not { } command ||
            !command.StartsWith('+'))
        {
            return;
        }

        // Stryker restore all

        _ = RunKey(string.Concat("-", KeyText(command, key!).AsSpan(1)), key!);
    }

    /// <summary>Lets go of every button, leaving the binds and aliases in place.</summary>
    /// <remarks>
    /// **The held-key leak, which is a real bug and not a hypothetical one.** A key released while
    /// this window is not focused never sends its key up here, so without this the camera flies on
    /// for ever after an alt-tab.
    ///
    /// **This is Valve's own reset path.** `KeyUp` given an empty key argument clears both slots and
    /// forces the button up rather than trying to match a key:
    ///
    /// <code>
    /// if ( !c || !c[0] )
    /// {
    ///     b->down[0] = b->down[1] = 0;
    ///     b->state = 4;   // impulse up
    ///     return;
    /// }
    /// </code>
    ///
    /// **What it deliberately does not touch is the alias table.** Those came from the user's
    /// config, and dropping them on a focus change would unbind the controls with nothing to say so.
    /// </remarks>
    public void ReleaseEverything()
    {
        foreach (Button button in _buttons.Values)
        {
            button.Release(null);
        }
    }

    /// <summary>Whether any button at all is down.</summary>
    /// <remarks>
    /// **Non-destructive, unlike <see cref="Intent"/>.** This reads only the down bit, so a
    /// diagnostic can ask it every frame without stealing the impulse credit a tapped key earned.
    /// </remarks>
    public bool AnyHeld
    {
        get
        {
            foreach (Button button in _buttons.Values)
            {
                if (button.Down)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Whether an action's button is currently down.</summary>
    /// <param name="action">The action.</param>
    /// <returns>True while any key holds it.</returns>
    public bool IsHeld(ViewerAction action) =>
        _buttons.TryGetValue(action, out Button? button) && button.Down;

    /// <summary>How much of the last frame an action was held for, from 0 to 1.</summary>
    /// <param name="action">The action.</param>
    /// <returns>0, 0.25, 0.5, 0.75 or 1, as <c>CInput::KeyState</c> returns.</returns>
    /// <remarks>
    /// **Reading this consumes the impulse bits**, which is Valve's behaviour rather than an
    /// accident of this port: the last statement of `CInput::KeyState` is `key->state &amp;= 1`.
    /// Read each action once per frame, as `CreateMove` does.
    /// </remarks>
    public float KeyState(ViewerAction action) =>
        _buttons.TryGetValue(action, out Button? button) ? button.Read() : 0f;

    /// <summary>What the user is currently asking the camera to do.</summary>
    /// <returns>The three axes and whether to hurry, independent of any keyboard.</returns>
    /// <remarks>
    /// **Call this exactly once per frame.** It reads each button's state, and reading is
    /// destructive by design — <c>CInput::KeyState</c> ends with <c>key-&gt;state &amp;= 1</c>, so the
    /// partial-frame credit is consumed on the first read. The engine gets away with that because
    /// `CreateMove` reads each button once; a second call in the same frame silently loses the
    /// fraction and a tapped key stops registering.
    ///
    /// **The fraction is the point, not a detail.** A key pressed partway through a frame counts
    /// 0.5, and a key pressed and released inside one frame counts 0.25 rather than nothing at all.
    /// Without it a quick tap of W moves the camera zero units, because by the time anyone looks the
    /// key is already up.
    /// </remarks>
    public FlightInput Intent() => new(
        Forward: Axis(ViewerAction.FlyForward, ViewerAction.FlyBack),
        Right: Axis(ViewerAction.FlyRight, ViewerAction.FlyLeft),
        Up: Axis(ViewerAction.FlyUp, ViewerAction.FlyDown),
        Walk: IsHeld(ViewerAction.FlyWalk));

    /// <summary>One axis: the positive button's state less the negative one's.</summary>
    /// <remarks>
    /// Both are read, always, even when the first is already 1 — skipping the second would leave its
    /// impulse bits set and make the *next* frame report a stale press. The engine reads every
    /// button every frame for the same reason.
    /// </remarks>
    private float Axis(ViewerAction positive, ViewerAction negative) =>
        KeyState(positive) - KeyState(negative);

    /// <summary>What a name is defined as right now.</summary>
    /// <param name="name">A command or alias name.</param>
    /// <returns>The alias body, or the name itself when nothing defines it.</returns>
    /// <remarks>
    /// **One level, not a full expansion, because the question this answers is "what does this name
    /// mean at this moment".** Expanding all the way would flatten `checkfwd -> none -> ""` to the
    /// empty string, which is true and useless: it hides the fact that `checkfwd` was pointed at
    /// something different.
    ///
    /// **Present so a test can watch an alias change meaning**, which is the observation that
    /// distinguishes a real interpreter from a static reader.
    /// </remarks>
    public string Resolve(string name) =>
        _aliases.TryGetValue(name ?? string.Empty, out string? body) ? body : name ?? string.Empty;

    /// <summary>Actions no key reaches once the user's config has run.</summary>
    /// <returns>The unreachable actions, in enum order.</returns>
    /// <remarks>
    /// **This exists because a real config produced one on the first try.** The owner's `config.cfg`
    /// contains
    ///
    /// <code>
    /// bind "SHIFT" "+duck"
    /// </code>
    ///
    /// and this viewer has no crouch, so Shift now runs a command that does nothing — and
    /// <see cref="ViewerAction.FlyWalk"/>, whose default key was Shift, is left with nothing to
    /// press. **That is the config being honoured correctly**, not a bug: the player said Shift is
    /// duck, and quietly overriding them to mean "fly fast" would be this viewer deciding it knows
    /// better than the file it was asked to obey.
    ///
    /// **But it must be said out loud.** A control that cannot be reached fails exactly the way
    /// nothing else does — the key does something invisible and there is nothing to see. Reporting
    /// it turns "the speed key is broken" into a line in the log naming the command that took it.
    ///
    /// **Not filled in from the defaults**, which is what <see cref="Bindings"/> does and why the
    /// two are separate. A settings screen wants every row populated; a diagnostic wants the truth.
    /// </remarks>
    /// <summary>Read the user's own TF2 configs and take their bindings.</summary>
    /// <param name="installedGameFolder">Where TF2 is, or null if it was not found.</param>
    /// <param name="loggers">For the config reader's own diagnostics.</param>
    /// <param name="config">The config log.</param>
    /// <param name="ownCustomRoot">The program's own <c>custom/</c> folder, or null (D193).</param>
    /// <returns>The bindings, or null when nothing was loaded and the caller should keep its own.</returns>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    /// <remarks>
    /// **This was `MainForm.LoadUserConfig`** (B188, D90). Reading a user's `.cfg` files is the
    /// heart of D69 — a real config must work wholesale — and none of it is window work.
    ///
    /// **Null rather than the default bindings when nothing loads, and that is a faithfulness
    /// point rather than a style one.** `MainForm` assigned `_bindings` only on the success path, so
    /// a failed read left whatever the field already held. Returning `Bindings()` here instead would
    /// look equivalent and would silently overwrite the caller's bindings with this console's — a
    /// difference that shows up only as keys quietly changing behaviour after an unreadable config.
    ///
    /// **Every applied bind is logged at Information, one line each.** That is verbose for
    /// production and it stays: the whole promise of D69 is that a pasted config works, and the only
    /// way a user can check which of their binds this viewer understood is to read them back. The
    /// unbound list exists for the same reason from the other side.
    /// </remarks>
    public KeyBindings? LoadFrom(
        string? installedGameFolder, ILoggerFactory loggers, ILogger config, string? ownCustomRoot = null)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            // **The caller's answer, null included** (B811 review 3, Fable): production's is already `SteamInstall.Machine`'s,
            // so falling back to it again only reached past a test's "no install" to this machine's own TF2 configs.
            string? game = installedGameFolder;

            // **The program's own custom/ folder is checked even with no TF2 install found** (D193):
            // a config a player pasted in there does not depend on the game being installed at all.
            // `Tf2ConfigFiles.ReadFiles` already treats a null/empty gameFolder as "nothing from the
            // game", so passing `game` through unconditionally is enough.
            // **`exec` reads the same search path** (B820), opened once for both.
            Func<string, byte[]?> find = Tf2ConfigFiles.Finder(game, loggers.LogTo(), ownCustomRoot);
            IReadOnlyList<(string Path, string Text)> configs = Tf2ConfigFiles.ReadFiles(find, loggers.LogTo());
            FindConfig = find;
            Log = config;

            if (configs.Count == 0)
            {
                config.LogInformation(
                    "{Message}",
                    game is null
                        ? "no TF2 install found and no configs under the program's own custom/; using the built-in bindings"
                        : $"no configs under {game}; using the built-in bindings");
                return null;
            }

            _refusedCheats.Clear();

            // **TF2's saved configs rank below the viewer's own saved settings** (D264): their ConVars stay in this console's
            // list, which the attached settings shadow, rather than reaching the settings as a run-time set. Their binds
            // and aliases count as always. `autoexec.cfg` reaches the settings, as the player's hand-written layer.
            foreach ((string path, string text) in configs)
            {
                _savedLayer = Tf2ConfigFiles.IsSaved(path);

                try
                {
                    Load(text);
                }
                finally
                {
                    _savedLayer = false;
                }
            }

            // TF2's own line for each cheat ConVar a config set with cheats off (B718). Once the settings are attached, their
            // `CheatRefused` handler has printed it already (B811 review 5, Sonnet): only a console nobody listens to prints here.
            foreach (string refused in CheatRefused is null ? RefusedCheats : [])
            {
                config.LogInformation(
                    "{Message}", $"Can't use cheat cvar {refused} in multiplayer, unless the server has sv_cheats set to 1.");
            }

            KeyBindings bindings = Bindings();

            config.LogInformation(
                "{Message}", $"{configs.Count} files, {Applied} of {Bound} binds applied");

            foreach ((ViewerAction action, string key) in bindings.All())
            {
                config.LogInformation("{Message}", $"  {action,-20} {key}");
            }

            // **The controls their config left unreachable, named rather than left to be noticed.**
            // A key bound to a TF2 command this viewer does not implement — `bind "SHIFT" "+duck"`
            // is the real example — takes that key away from whatever used to answer to it, and the
            // symptom is a control that silently does nothing.
            if (Unbound() is { Count: > 0 } unbound)
            {
                config.LogInformation(
                    "{Message}",
                    $"no key reaches: {string.Join(", ", unbound)} " +
                    "(their config bound those keys to commands this viewer has no equivalent for)");
            }

            return bindings;
        }
        catch (Exception failure) when (failure is IOException or ArgumentException
                                            or UnauthorizedAccessException or NotSupportedException)
        {
            config.LogInformation(failure, "could not read the TF2 configs");
            return null;
        }
    }

    public IReadOnlyList<ViewerAction> Unbound()
    {
        HashSet<ViewerAction> reachable = [];

        // **The defaults' keys are walked too, and leaving them out was a real defect** (B214).
        // Every real TF2 config opens with `unbindall`, which empties `_binds` — so after loading
        // one, this loop could only see keys that config went on to mention. A default sitting on a
        // key no config touches was therefore reported as UNBOUND while `CommandFor`'s fallback was
        // happily still answering for it: the control worked and the report said it did not.
        //
        // It stayed hidden while every default lived on a key TF2 itself binds. Moving the viewer's
        // own actions onto `CTRL` combinations — which no config can name — made twelve of them
        // vanish from a report that is supposed to be the alarm for exactly this.
        foreach (string key in _binds.Keys.Union(_defaults.Keys, StringComparer.OrdinalIgnoreCase))
        {
            foreach (ViewerAction action in Expand(CommandFor(key) ?? string.Empty, depth: 0))
            {
                reachable.Add(action);
            }
        }

        List<ViewerAction> unbound = [];

        foreach (ViewerAction action in Enum.GetValues<ViewerAction>())
        {
            if (!reachable.Contains(action))
            {
                unbound.Add(action);
            }
        }

        return unbound;
    }

    /// <summary>The static view of the bindings, for a settings screen to display.</summary>
    /// <returns>Which key each action answers to.</returns>
    /// <remarks>
    /// **A projection of this console, not a second source of truth.** A settings screen has to show
    /// one key per action, and a script does not necessarily have one — so this reports the key
    /// whose bind reaches the action by expansion, and falls back to the default when none does.
    /// The runtime never consults it.
    /// </remarks>
    public KeyBindings Bindings()
    {
        Dictionary<ViewerAction, string> bound = [];

        foreach (string key in _binds.Keys)
        {
            foreach (ViewerAction action in Expand(CommandFor(key) ?? string.Empty, depth: 0))
            {
                bound.TryAdd(action, key);
            }
        }

        // Every bind, the viewer's commands or not, for the HUD's `%command%` keys (B733) — each key's command as the
        // runtime answers it, so a default the config left alone after `unbindall` still names its key, as in Unbound.
        Dictionary<string, string> commands = new(StringComparer.OrdinalIgnoreCase);

        foreach (string key in _binds.Keys.Union(_defaults.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (CommandFor(key) is { } command)
            {
                commands[key] = command;
            }
        }

        return new KeyBindings(bound, commands);
    }

    /// <summary>The ConCommands <see cref="Define"/> runs: binds, aliases and `exec` (B820).</summary>
    private static readonly HashSet<string> Definers =
        new(["unbindall", "unbind", "alias", "bind", "exec"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The ConCommands this console implements itself, found by name before any alias is (B820).</summary>
    private static readonly HashSet<string> BuiltIns =
        new([.. Definers, "toggle", "incrementvar", "multvar"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the dispatcher looks a name up among the aliases: a `+`/`-` button, or a name no command claims.</summary>
    private static bool ConsultsAlias(string name) => name.StartsWith('+') || name.StartsWith('-') || !IsCommand(name);

    /// <summary>
    /// Whether `Cmd_ExecuteCommand` would find a command or ConVar by this name before it looked at the aliases (B820).
    /// </summary>
    /// <remarks>
    /// The engine consults an alias only when `FindCommandBase` finds nothing, or when the name starts with `+` or `-`
    /// (`engine.dll` build 1007 FUN_1801678a0). Known here: this console's own ConCommands, every declared ConVar, and every command
    /// a viewer action is spelled as. *Interpolated*: a TF2 ConCommand this viewer does not know (`say`, say) is
    /// shadowed by an alias of its name here and is not in TF2; that alias's command does nothing here either way.
    /// </remarks>
    private static bool IsCommand(string name) =>
        BuiltIns.Contains(name) || EngineConVars.TryByConsoleName(name, out _) || KeyBindings.ActionOf(name) is not null;

    /// <summary>Runs one of this console's own ConCommands: binds, aliases and `exec`.</summary>
    /// <returns>Whether the clause named one, whatever its arguments.</returns>
    private bool Define(IReadOnlyList<string> tokens, int depth)
    {
        if (tokens[0].Equals("unbindall", StringComparison.OrdinalIgnoreCase))
        {
            // Every key bound to something becomes "", but ESCAPE and the console key (`Key_Unbindall_f`, build 1007
            // FUN_1801b6b10, the same build; `SourceConfig.SurvivesUnbindAll`, B823 review 2).
            foreach (string key in _binds.Keys.Where(key => !SourceConfig.SurvivesUnbindAll(key) && _binds[key].Length > 0).ToList())
            {
                _binds[key] = string.Empty;
            }

            _claimed = null;
            BindsRevision++;
            return true;
        }

        if (tokens[0].Equals("unbind", StringComparison.OrdinalIgnoreCase))
        {
            // `Key_Unbind_f` (`engine.dll` x64 build 1007 FUN_1801b6a30, B823 review 1): the count, then the name (B826), then
            // `ESCAPE`, which prints its own line; any other key's binding becomes "", not null, so `bind k` then prints
            // `"k" = ""`.
            if (tokens.Count != 2)
            {
                Log.LogInformation("{Message}", "unbind <key> : remove commands from a key");
            }
            else if (ButtonCodeNames.KeyOf(tokens[1]) is not { } key)
            {
                Log.LogInformation("{Message}", $"\"{tokens[1]}\" isn't a valid key");
            }
            else if (SourceConfig.IsEscape(key))
            {
                Log.LogInformation("{Message}", "Can't unbind ESCAPE key");
            }
            else
            {
                _binds[key] = string.Empty;
                _claimed = null;
                BindsRevision++;
            }

            return true;
        }

        if (tokens[0].Equals("alias", StringComparison.OrdinalIgnoreCase))
        {
            // `alias name` with no body clears it, and an empty body is legal — the null-cancel
            // script defines `alias none ""` precisely so a name can mean "do nothing".
            // **Every argument after the name, joined by spaces** — *interpolated*: Quake's `Cmd_Alias_f`, which the
            // closed engine's descends from, copies "the rest of the command line"; `alias x say hi` is `say hi`.
            if (tokens.Count >= 2)
            {
                _aliases[tokens[1]] = string.Join(' ', tokens.Skip(2));

                // What a bind claims is found through the aliases, so a new body can retire a default (B821 review 1, Fable).
                _claimed = null;
                BindsRevision++;
            }

            return true;
        }

        if (tokens[0].Equals("bind", StringComparison.OrdinalIgnoreCase))
        {
            Bind(tokens);
            return true;
        }

        if (tokens[0].Equals("exec", StringComparison.OrdinalIgnoreCase))
        {
            Exec(tokens, depth);
            return true;
        }

        return false;
    }

    /// <summary>`bind`, as `Key_Bind_f` (`engine.dll` x64 build 1007 FUN_1801b67a0, with build 1007 FUN_1801b5ea0) runs it (B823).</summary>
    /// <remarks>
    /// Read in the decompile and disassembly: only `argc - 2 &lt; 2` acts, so one argument or four and more print the usage
    /// line and change nothing — `bind k café` is five arguments by bytes. Two print the key's binding (`"%s" = "%s"`, or
    /// `"%s" is not bound`); three bind `Arg(2)`, except that key 0x46, `KEY_ESCAPE`, is bound to `cancelselect` whatever
    /// the line says. Before either, the name is looked up in the input system's table (B826, <see cref="ButtonCodeNames"/>):
    /// a name it does not know prints `"%s" isn't a valid key` and does nothing, and a known one binds under the table's own
    /// spelling, so `aux1` and `JOY2` are one key. The show prints the name as typed. The binding printed is the table's, seeded defaults
    /// included: TF2's own defaults come from `config_default.cfg` into the same table, so its `bind F5` prints them, and
    /// <see cref="WithDefaults"/> plays that file's part (B823 review 6, Fable).
    /// </remarks>
    private void Bind(IReadOnlyList<string> tokens)
    {
        if (tokens.Count != 2 && !SourceConfig.Binds(tokens))
        {
            Log.LogInformation("{Message}", "bind <key> [command] : attach a command to a key");
            return;
        }

        // **The name is looked up before the show or the set** (B826): build 1007 FUN_1801b5ea0 asks the input system, and
        // a name it does not know prints this and does nothing; a known one binds the key under the table's own name.
        if (ButtonCodeNames.KeyOf(tokens[1]) is not { } key)
        {
            Log.LogInformation("{Message}", $"\"{tokens[1]}\" isn't a valid key");
            return;
        }

        if (tokens.Count == 2)
        {
            Log.LogInformation(
                "{Message}",
                // A key bound to "" holds a string (build 1007 FUN_1801b65d0 copies it; `unbind` sets one), so it prints `= ""`.
                _binds.TryGetValue(key, out string? bound)
                    ? $"\"{tokens[1]}\" = \"{bound}\""
                    : $"\"{tokens[1]}\" is not bound");
            return;
        }

        _binds[key] = SourceConfig.Binding(key, tokens[2]);
        _configBound.Add(key);
        _claimed = null;
        BindsRevision++;
    }

    /// <summary>The file types `Cmd_Exec_f` refuses anywhere in the path, case-sensitively (`engine.dll` build 1007 FUN_1801672f0).</summary>
    private static readonly string[] RefusedFileTypes =
        [".exe", ".vbs", ".com", ".bat", ".dll", ".ini", ".gcf", ".sys", ".zip", ".vpk", ".blob", ".scr"];

    /// <summary>How many files one command at depth 0 may `exec` in all, its nested files included.</summary>
    /// <remarks>*Interpolated* (B820 review 1): the engine's bound is not read. Far past a real config tree, which execs tens.</remarks>
    public const int MaximumExecs = 256;

    /// <summary>How many aliases one line may run in all, nested ones included: a key's line, or one line of a file.</summary>
    /// <remarks>
    /// *Interpolated* (B820 reviews 2 and 3): the engine's bound is not read. Far past a real script, which runs tens per line;
    /// per file line rather than per command, so one `exec` of a whole config tree does not share one budget.
    /// </remarks>
    public const int MaximumAliasRuns = 4096;

    /// <summary>`COMMAND_MAX_LENGTH`: a command of this many bytes less one or more is skipped (`src/public/tier1/convar.h:207`).</summary>
    public const int MaximumCommandLength = 512;

    /// <summary>`COMMAND_MAX_ARGC`: the arguments a command keeps (`src/public/tier1/convar.h:206`).</summary>
    public const int MaximumArguments = 64;

    /// <summary>How many clauses one command at depth 0 may run in all: every alias body's and exec'd file's included.</summary>
    /// <remarks>
    /// *Interpolated* (B820 reviews 4 and 5, Sonnet): the per-line budget resets on each line of a nested `exec`, and counts
    /// alias runs rather than what a body does, so without this a command's work was bounded only by 256 files of 1 MB
    /// lines and by the length of an alias body. A clause is the unit of work, so this bounds the work itself.
    /// </remarks>
    public const int MaximumCommandClauses = 1 << 20;

    /// <summary>What makes a path invalid to `Cmd_Exec_f`, case-sensitively (`engine.dll` build 1007 FUN_180176d10).</summary>
    private static readonly string[] RefusedPathParts = ["\\\\", ":", "..", "\n", "\r"];

    /// <summary>The missing files `Cmd_Exec_f` says nothing about, matched in the argument as typed, ignoring case.</summary>
    private static readonly string[] QuietWhenMissing = ["autoexec.cfg", "joystick.cfg", "game.cfg"];

    /// <summary>`exec &lt;name&gt;`: runs <c>cfg/&lt;name&gt;</c> where the line stands, as `Cmd_Exec_f` (B820).</summary>
    /// <remarks>
    /// Messages are TF2's, from `engine.dll` build 1007 FUN_1801672f0; the path in the two refusals is `//MOD/cfg/…` there and
    /// `cfg/…` here, the search path being <see cref="FindConfig"/>'s. *Interpolated*: nothing in `Cmd_Exec_f` bounds a
    /// file that execs itself; this stops at <see cref="MaximumExpansion"/>, as an alias does.
    /// </remarks>
    private void Exec(IReadOnlyList<string> tokens, int depth)
    {
        if (tokens.Count != 2)
        {
            Log.LogInformation("{Message}", "exec <filename>: execute a script file");
            return;
        }

        // **A budget per command typed, besides the depth** (B820 review 1, Fable, Sonnet): a file holding `exec self`
        // twice reads 2^20 files under the depth bound alone. *Interpolated*, as the depth bound is.
        if (++_budget.Command.Execs > MaximumExecs)
        {
            if (_budget.Command.Execs == MaximumExecs + 1)
            {
                Log.LogInformation("{Message}", $"exec: more than {MaximumExecs} files from one command; not executing the rest.");
            }

            return;
        }

        string name = tokens[1];
        string path = "cfg/" + name;

        // `V_DefaultExtension`: `.cfg` only when the last segment has no dot (build 1007 FUN_180282920).
        if (path.IndexOf('.', path.LastIndexOfAny(['/', '\\']) + 1) < 0)
        {
            path += ".cfg";
        }

        if (RefusedPathParts.Any(part => path.Contains(part, StringComparison.Ordinal)))
        {
            Log.LogInformation("{Message}", $"exec {path}: invalid path.");
            return;
        }

        if (RefusedFileTypes.Any(type => path.Contains(type, StringComparison.Ordinal)))
        {
            Log.LogInformation("{Message}", $"exec {path}: invalid file type.");
            return;
        }

        if (FindConfig?.Invoke(path) is not { } bytes)
        {
            if (!QuietWhenMissing.Any(quiet => name.Contains(quiet, StringComparison.OrdinalIgnoreCase)))
            {
                Log.LogInformation("{Message}", $"'{name}' not present; not executing.");
            }

            return;
        }

        if (bytes.Length > Tf2ConfigFiles.MaximumBytes)
        {
            Log.LogInformation("{Message}", $"exec {name}: file size larger than 1 MB!");
            return;
        }

        if (depth + 1 >= MaximumExpansion)
        {
            Log.LogInformation("{Message}", $"exec {name}: nested deeper than {MaximumExpansion}; not executing.");
            return;
        }

        RunFile(Tf2ConfigFiles.Decode(bytes), depth + 1);
    }

    /// <summary>A typed ConVar name as the ConVar spells it, or as typed when nothing declares it (B791 review 9).</summary>
    private static string OwnName(string typed) =>
        EngineConVars.TryByConsoleName(typed, out EngineConVar? declared) ? declared.Name : typed;

    /// <summary>A client ConVar's value as last set — through the attached settings, else the configs — or null when none did.</summary>
    /// <param name="name">The ConVar's engine name.</param>
    /// <returns>The value, or null.</returns>
    public string? ConVar(string name) => _settingValue?.Invoke(name) ?? _conVars.GetValueOrDefault(name);

    /// <summary>Whether a name is a client ConVar this console sets: declared, and not the server's (FCVAR_REPLICATED).</summary>
    private static bool IsClientConVar(string name) =>
        EngineConVars.TryByConsoleName(name, out EngineConVar? declared) && !declared.Replicated;

    /// <summary>
    /// Whether the console takes a set of this name: a client ConVar, or `sv_cheats` though replicated (B718). The one
    /// test <see cref="SetConVar"/> and <see cref="SetsState"/> share, so what a key counts as running cannot drift from
    /// what it runs (B823 review 9, Fable).
    /// </summary>
    private static bool TakesSet(string name) =>
        name.Equals(CheatsConVar, StringComparison.OrdinalIgnoreCase) || IsClientConVar(name);

    /// <summary>`name value`: sets a declared client ConVar; a replicated one is the server's (iconvar.h, FCVAR_REPLICATED).</summary>
    private bool SetConVar(IReadOnlyList<string> tokens)
    {
        _refusedNow = false;

        if (tokens.Count < 2 || !TakesSet(tokens[0]))
        {
            return false;
        }

        // **`sv_cheats` from the console, though replicated** (B718, measured on live TF2 during demo playback): the console
        // takes it, and `sv_cheats 0` reverts every cheat ConVar to its default. It is not a setting of the viewer's, so its
        // value is kept here whether or not the settings are attached.
        if (tokens[0].Equals(CheatsConVar, StringComparison.OrdinalIgnoreCase))
        {
            bool wereOn = CheatsOn;

            _conVars[CheatsConVar] = tokens[1];

            if (wereOn && !CheatsOn)
            {
                RevertCheats();
            }

            return true;
        }

        if (!EngineConVars.TryByConsoleName(tokens[0], out EngineConVar? declared))
        {
            return false;
        }

        if (RefuseCheat(declared, tokens[1]))
        {
            return false;
        }

        // Once the settings are attached they hold every client ConVar, implemented or not (D215): no second copy, except
        // for TF2's saved configs, which keep their own lines below the settings (D264).
        if (_settingValue is null || _savedLayer)
        {
            _conVars[declared.Name] = tokens[1];
        }

        return true;
    }

    /// <summary>
    /// **A cheat ConVar is refused while cheats are off** (B718, measured): *"Can't use cheat cvar %s in multiplayer, unless
    /// the server has sv_cheats set to 1."*, and the value stays. Kept for the load's log to print. The viewer's own
    /// default keys are exempt (D253 as <see cref="KeyDown"/> applies it; B792).
    /// </summary>
    /// <returns>Whether it was refused.</returns>
    private bool RefuseCheat(EngineConVar declared, string value)
    {
        if (!declared.Cheat || CheatsOn || _viewerIssued)
        {
            return false;
        }

        _refusedCheats.Add(declared.Name);
        _refusedNow = true;
        CheatRefused?.Invoke(this, new ConVarSetEventArgs(declared.Name, value));
        return true;
    }

    /// <summary>The ConVar that allows cheat ConVars.</summary>
    private const string CheatsConVar = "sv_cheats";

    /// <summary>
    /// Whether the line being run comes from TF2's saved configs (<see cref="Tf2ConfigFiles.IsSaved"/>), whose ConVars rank
    /// below the viewer's own saved settings (D264).
    /// </summary>
    private bool _savedLayer;

    /// <summary>
    /// <c>sv_cheats</c> back to 0 as a fresh start has it, reverting nothing: for the configs and the launch line run again
    /// after a game folder is chosen (B811 review 4). A typed <c>sv_cheats 0</c> would revert every cheat, including a
    /// value the viewer's own settings or menu hold, which a fresh start keeps.
    /// </summary>
    public void StartCheatsOff() => _conVars[CheatsConVar] = "0";

    /// <summary>Whether <c>sv_cheats</c> allows cheats now; 0, its default, until set.</summary>
    private bool CheatsOn => _conVars.TryGetValue(CheatsConVar, out string? cheats) && CheatsAllowed(cheats);

    /// <summary>
    /// Whether an <c>sv_cheats</c> value allows cheats, as the engine reads it: its integer, <c>(int)atof(value)</c>, not zero
    /// (`ConVar::InternalSetValue`). So "0.5" is off and "1abc" on.
    /// </summary>
    /// <param name="value">The value as written.</param>
    /// <returns>True when cheats are on.</returns>
    internal static bool CheatsAllowed(string value) => ConVarNumber.GetInt(value) != 0;

    /// <summary>Whether the last <see cref="SetConVar"/> refused a cheat ConVar.</summary>
    private bool _refusedNow;

    private readonly List<string> _refusedCheats = [];

    /// <summary>The cheat ConVars the configs tried to set while cheats were off, in order, for the load's log (B718).</summary>
    public IReadOnlyList<string> RefusedCheats => _refusedCheats;

    /// <summary>Raised when a cheat ConVar is refused, so the host prints TF2's line (B718).</summary>
    public event EventHandler<ConVarSetEventArgs>? CheatRefused;

    /// <summary>
    /// <c>sv_cheats</c> went to 0: every cheat ConVar back to its default (B718, measured), announced to the settings once
    /// they hold the values, as any other set is (D215).
    /// </summary>
    private void RevertCheats()
    {
        foreach (EngineConVar cheat in EngineConVars.All)
        {
            if (!cheat.Cheat || cheat.Replicated)
            {
                continue;
            }

            // TF2's saved configs revert only their own lines, below the viewer's saved settings (D264).
            if (_settingValue is null || _savedLayer)
            {
                _conVars[cheat.Name] = cheat.Default;
            }
            // The value as `CurrentValue` reads it — the settings, then a config's line the console keeps — so a cheat a
            // watcher config set is reverted too (B791 review 15).
            else if ((_settingValue(cheat.Name) ?? _conVars.GetValueOrDefault(cheat.Name)) is { } now &&
                !string.Equals(now, cheat.Default, StringComparison.Ordinal))
            {
                // Only what differs from its default is announced, as `ConVar::Revert` runs a ConVar's callbacks only when its
                // STRING changed (`V_strcmp`, `src/tier1/convar.cpp:844`), so `nan` reverts where a float compare saw no
                // difference (B823 review 1). *Not read:* the filter inside `RevertFlaggedConVars`, which is vstdlib's.
                ConVarSet?.Invoke(this, new ConVarSetEventArgs(cheat.Name, cheat.Default, now));
            }
        }
    }

    /// <summary>
    /// `toggle`, `incrementvar` and `multvar` (D215), as the `name value` command each ends by executing, or null when
    /// the line is not one of them or the engine would set nothing.
    /// </summary>
    /// <remarks>
    /// Read from x64 `engine.dll` (Ghidra `tf2engine`, 2026-10-14), and re-found in build 1007 (B823 review 3): `incrementvar`
    /// build 1007 FUN_1801a9920, `multvar` build 1007 FUN_1801aa500, `toggle` build 1007 FUN_180179390 (the earlier build's
    /// FUN_1801a7fe0, FUN_1801a8bc0 and FUN_180177cd0). The engine's warnings (usage, `cvar "%s" not found`, `%s = %f`) go to its
    /// console, which this viewer does not have, so they are not reproduced. A replicated cvar is refused afterwards by
    /// <see cref="SetConVar"/>, as the engine's executed `name value` would be.
    /// </remarks>
    private string[]? CvarCommand(IReadOnlyList<string> tokens)
    {
        string verb = tokens[0];
        bool increment = verb.Equals("incrementvar", StringComparison.OrdinalIgnoreCase);

        if (increment || verb.Equals("multvar", StringComparison.OrdinalIgnoreCase))
        {
            // Exactly five tokens, else Usage and nothing else.
            if (tokens.Count != 5 || CurrentValue(tokens[1]) is not { } current)
            {
                return null;
            }

            float min = ConVarNumber.GetFloat(tokens[2]);
            float max = ConVarNumber.GetFloat(tokens[3]);
            float step = ConVarNumber.GetFloat(tokens[4]);
            float result = increment ? ConVarNumber.GetFloat(current) + step : ConVarNumber.GetFloat(current) * step;

            if (result > max)
            {
                // incrementvar wraps to min; multvar clamps to max.
                result = increment ? min : max;
            }
            else if (increment ? result < min : result <= min)
            {
                // incrementvar wraps to max below min (equal keeps it); multvar clamps at `<=`, as the binary compares.
                result = increment ? max : min;
            }

            // Executed as `"%s %f"`, so the value is printed with six decimals: `cl_showpos 1.000000`.
            return [tokens[1], result.ToString("F6", CultureInfo.InvariantCulture)];
        }

        if (!verb.Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Fewer than two tokens is Usage; an unknown cvar is "%s is not a valid cvar". Its validity check (build 1007
        // FUN_1801785e0; the earlier build's FUN_180176f20) also refuses
        // FCVAR_DEVELOPMENTONLY (2) and FCVAR_HIDDEN (0x10), which no cvar `EngineConVars` declares carries, and applies
        // multiplayer, cheat and replicated server rules that do not arise in demo playback; none of those is ported.
        if (tokens.Count < 2 || CurrentValue(tokens[1]) is not { } value)
        {
            return null;
        }

        if (tokens.Count == 2)
        {
            // The cvar's INT value: zero becomes 1, anything else 0.
            return [tokens[1], ConVarNumber.GetInt(value) == 0 ? "1" : "0"];
        }

        // Exact byte compare of the STRING value against each listed value; the one after the match, wrapping, or
        // the first when nothing matches.
        for (int i = 2; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i], value, StringComparison.Ordinal))
            {
                return [tokens[1], tokens[i + 1 < tokens.Count ? i + 1 : 2]];
            }
        }

        return [tokens[1], tokens[2]];
    }

    /// <summary>A declared cvar's current string value, or null when no cvar has the name.</summary>
    /// <remarks>
    /// The live settings first (D215), so a value the menu set is the value a key sees; then what a config set before
    /// they were attached; then the declared default.
    /// </remarks>
    private string? CurrentValue(string name) =>
        !EngineConVars.TryByConsoleName(name, out EngineConVar? declared) ? null
            : _settingValue?.Invoke(declared.Name) ?? _conVars.GetValueOrDefault(declared.Name) ?? declared.Default;

    /// <summary>Reads ConVar values from the viewer's settings, which then hold every ConVar they implement (D215).</summary>
    /// <param name="value">A ConVar's current value as the engine holds its string, or null where the settings have none.</param>
    public void ReadSettingsFrom(Func<string, string?> value) => _settingValue = value;

    /// <summary>Runs a command line, expanding aliases, in press or release direction.</summary>
    /// <remarks>
    /// **The key name does not survive into an alias body, and that is the mechanism the whole
    /// null-cancel pattern rests on.** The engine appends the key number to the command a key is
    /// bound to — `+mfwd 32` — and Source aliases take no parameters, so the commands inside
    /// `+mfwd`'s body run with nothing. `KeyUp` treats that as a reset:
    ///
    /// <code>
    /// if ( !c || !c[0] )
    /// {
    ///     b->down[0] = b->down[1] = 0;
    ///     b->state = 4;   // impulse up
    ///     return;
    /// }
    /// </code>
    ///
    /// So `-forward` issued from inside `+mback` releases the forward button **no matter which key
    /// is holding it**, which is exactly what a null-cancel script needs and cannot get any other
    /// way. Had the key number propagated, `-forward` from the S key would have been discarded as a
    /// release with no matching press and the script would do nothing.
    ///
    /// This was found by a failing conformance test rather than by reading ahead: the first version
    /// propagated the key, and `KeyUp_TheNullCancelScript_RestoresTheKeyStillHeld` failed with
    /// forward still held.
    ///
    /// **There is no press/release direction here, because the engine has none either.** The flip
    /// happens once, in <see cref="KeyUp"/>, on the command the key is bound to. Everything below
    /// that runs exactly as written — which is what lets `-mback`'s body call `checkfwd` and have it
    /// *press* forward rather than release it. Carrying a direction flag down the recursion was the
    /// second bug in this class, and it failed the same test.
    /// </remarks>
    /// <returns>Whether a command of it was put off past the current tick, which counts as the key's doing.</returns>
    private bool AddText(string commandLine, string? key, int depth)
    {
        if (depth >= MaximumExpansion)
        {
            return false;
        }

        // **The text is queued whole before any of it runs** (B834): `CCommandBuffer::AddText` inserts every command first, so
        // the commands after the one running hold the argument buffer while it runs, and an alias body it adds, or a file line
        // an `exec` in it adds, fits only beside them. A command of 511 bytes or more is refused with TF2's line, and so is the
        // rest of its text (`InsertCommand`, `src/tier1/commandbuffer.cpp:120-126`; `AddText`, `:242-243`; B820 reviews 5 and
        // 6, Fable). Every adder in the engine (`Cbuf_AddText`, FUN_180166500 and FUN_180166e30 in `engine.dll` build 1007:
        // a key, a file line, an alias body, the launch line) then says `Cbuf_AddText: buffer overflow` (B830 review 3, B835),
        // and says it before any of the text runs. The strings are in TF2's `engine.dll`, so the engine links this tier1 code.
        (List<Added> added, string? tooLong, bool refused) = Insert(commandLine, _queuedBytes, _tick, _waitEnabled);

        if (tooLong is not null)
        {
            Log.LogInformation("{Message}", $"WARNING: Command too long... ignoring!\n{tooLong}");
        }

        if (refused)
        {
            Log.LogInformation("{Message}", "Cbuf_AddText: buffer overflow");
        }

        // **Every command of the text is linked into the one queue as `InsertCommand` links it** (B840, B832): added while
        // commands are processing (an alias body, a file line, the launch line), one due now goes before the next command,
        // in order (`InsertImmediateCommand`), so it runs ahead of the rest; added while none are (`Key_Event`'s), or put
        // off past the current tick, it is linked from the head by tick (`InsertCommandAtAppropriateTime`). The queue's
        // head-only dequeue then runs them in the engine's order. Each holds its bytes until it runs, in the context it was
        // added in, with the budgets and the key's answer of the command that added it.
        // **One put off past this `Cbuf_Execute` starts afresh** (B840 review 2, Fable): `CCommandBuffer` has no depth or
        // budget, so a `wait` loop (`alias lp "x; wait; lp"`) runs every tick for ever in TF2; the viewer's bounds (B820) are
        // per run, and one that runs in a later call is a command of its own, at depth 0 with budgets of its own, as it was
        // before B840. One due within this call keeps them, which bounds a loop that never leaves it.
        // A text's commands put off to one tick share one fresh set, as a text run at once shares one; each later tick is a
        // run of its own (B840 reviews 3 and 4, Fable). *Known bound*: a loop run from a command held about 19 ticks behind
        // the run by a negative `wait` (fewer by its starting depth) re-adds at or before the run's last tick and keeps its
        // depth, so a `wait` loop started there ends at the depth bound inside that run.
        int lastThisRun = _processing ? _lastTick : _tick;
        Dictionary<int, Budget>? laterBudgets = null;
        bool putOff = false;

        foreach (Added queued in added)
        {
            _queuedBytes += queued.Size;
            putOff |= queued.Tick > _tick;
            bool later = queued.Tick > lastThisRun;
            Link(new Entry(
                queued.Tick,
                queued.Size,
                queued.Command,
                key,
                later ? 0 : depth,
                _loading,
                _stuffing,
                _viewerIssued,
                _savedLayer,
                later ? LaterBudget(laterBudgets ??= [], queued.Tick) : _budget,
                _answer,
                _adding,
                Run: null));
        }

        return putOff;
    }

    /// <summary>Runs one queued command (`Cmd_ExecuteCommand`).</summary>
    /// <param name="clause">The command.</param>
    /// <param name="key">The key that ran it, if any.</param>
    /// <param name="depth">Its depth.</param>
    /// <param name="acted">Set when it did something.</param>
    /// <returns>False when the clause budget is spent: every later command on that budget is then refused at its first
    /// clause as it comes off the queue.</returns>
    private bool RunClause(string clause, string? key, int depth, ref bool acted)
    {
        // Every clause counts, so one command's work is bounded whatever its aliases and files hold (B820 review 5).
        if (++_budget.Command.Clauses > MaximumCommandClauses)
        {
            if (_budget.Command.Clauses == MaximumCommandClauses + 1)
            {
                Log.LogInformation("{Message}", $"more than {MaximumCommandClauses} commands run from one command; not running the rest.");
            }

            return false;
        }

        IReadOnlyList<string> tokens = SourceConfig.Tokens(clause);

        // **At most 64 arguments, said once the 64th is taken** (`CCommand::Tokenize`, `src/tier1/convar.cpp:416,460-464`;
        // `COMMAND_MAX_ARGC`, `src/public/tier1/convar.h:206`).
        if (tokens.Count >= MaximumArguments)
        {
            Log.LogInformation("{Message}", "CCommand::Tokenize: Encountered command which overflows the argument buffer.. Clamped!");
            tokens = [.. tokens.Take(MaximumArguments)];
        }

        string command = tokens[0];

        // **The engine's order** (B820, `Cmd_ExecuteCommand`): an alias only where no command or ConVar has the name, or
        // where the name is a `+`/`-` button, which an alias may replace.
        if (ConsultsAlias(command) && _aliases.TryGetValue(command, out string? body))
        {
            // **A budget besides the depth** (B820 review 2, Fable, Sonnet): `alias a "a;a;a;a"` and a line `a` is 4^20
            // runs under the depth bound alone, and a config line now runs its alias at load. *Interpolated*.
            if (++_budget.AliasRuns > MaximumAliasRuns)
            {
                if (_budget.AliasRuns == MaximumAliasRuns + 1)
                {
                    Log.LogInformation("{Message}", $"alias {command}: more than {MaximumAliasRuns} alias runs from one line; not running the rest.");
                }

                acted = true;
                return true;
            }

            // No key, deliberately — see the remark above. The body is its own adder's call, so it says its own
            // refusals and the line that ran it does not hear them (B835; B830 review 6, Fable). `Cmd_ExecuteCommand`
            // (`engine.dll` build 1007 FUN_1801678a0) adds it while commands are processing, so it goes ahead of the rest
            // of this text (`InsertImmediateCommand`), and the queue runs it next.
            _ = AddText(body, key: null, depth + 1);
            acted = true;
            return true;
        }

        // A nested `alias` DEFINES rather than runs, and this is the state machine's whole
        // mechanism: `+mfwd` ends with `alias checkfwd +forward`, so releasing the other key
        // later finds a different meaning than it would have found before. `bind` and `exec` are ConCommands as
        // `alias` is, so a file line, a key and an alias body all reach them (B820).
        if (Define(tokens, depth))
        {
            acted = true;
            return true;
        }

        if (CvarCommand(tokens) is { } assignment)
        {
            // `toggle`, `incrementvar`, `multvar` end by running `name value`, so they land as one.
            tokens = assignment;
        }
        else if (tokens.Count >= 2)
        {
            // **A typed ConVar set takes `ArgS`, not `Arg(1)`** (B823): `name a b` sets `a b`, and a trailing non-ASCII
            // character is trimmed as the engine trims it — see `SourceConfig.ConVarValue`. `bind`, `alias` and `exec`
            // were taken by `Define` above. A command that is no ConVar reaches only `SetConVar`, which refuses it, and
            // then `tokens[0]` and the count: its arguments are read by nothing past here (B823 review 2, Sonnet, Grok —
            // a guard to collapse only for a ConVar had no test that could fail, and was removed).
            tokens = [tokens[0], SourceConfig.ConVarValue(clause)];
        }

        // The value it held, for whoever must still answer as it stood before this set (B839).
        string name = OwnName(tokens[0]);
        string? before = ConVar(name);

        if (SetConVar(tokens))
        {
            // In Source the ConVar's value IS the setting; here the viewer's settings hold it, so a set made
            // after startup is announced for the host to apply (D215). A config line before the settings attach is
            // not, and nor is one from TF2's saved configs, which stays here below the viewer's saved settings (D264).
            // Under the ConVar's own name, whatever case it was typed in, so the settings find it (B791 review 9).
            if (!_loading || (_settingValue is not null && !_savedLayer))
            {
                ConVarSet?.Invoke(this, new ConVarSetEventArgs(name, tokens[1], before));
            }

            acted = true;
            return true;
        }

        // A refused cheat ConVar is the console's answer, as TF2's is: it ends the clause (B718).
        if (_refusedNow)
        {
            acted = true;
            return true;
        }

        // **A bare cheat ConVar's name is the viewer's one-shot for it** (`mat_wireframe` toggles the view), and on TF2's
        // command paths it does nothing without cheats (B791). TF2 prints the value and sets nothing, so it is not a refused
        // SET either: no refusal is reported (B788 review 3).
        if (tokens.Count == 1 && KeyBindings.ActionOf(command) is not null && IsClientConVar(command) &&
            EngineConVars.TryByConsoleName(command, out EngineConVar? bare) && bare.Cheat && !CheatsOn && !_viewerIssued)
        {
            acted = true;
            return true;
        }

        // **A config's one-shot does nothing at load** (B820 review 1, Fable, Sonnet). *Interpolated*: TF2 runs its configs
        // at startup, before a demo plays, so a view or playback command has nothing to act on, and the viewer's own
        // command names (`opendemo`, `togglefullscreen`) are unknown commands to TF2. A `+`/`-` button is state and is
        // kept, as the engine keeps it. A key's one-shot, and a key's `exec`, still act. So does the launch line's, its
        // `exec`s' included (B829): `stuffcmds` runs every command it adds.
        if (_loading && !_stuffing && !command.StartsWith('-') && KeyBindings.ActionOf(command) is { } shot && !HeldActions.Contains(shot))
        {
            return true;
        }

        acted |= Perform(command, key);
        return true;
    }

    /// <summary>Applies one fully-expanded command.</summary>
    /// <returns>Whether it moved a button or raised a one-shot, which the host performs (B825), so the key is taken.</returns>
    private bool Perform(string command, string? key)
    {
        if (command.StartsWith('-'))
        {
            return KeyBindings.ActionOf(string.Concat("+", command.AsSpan(1))) is { } releasing && Release(releasing, key);
        }

        // Stryker disable once : a mutant that empties the guard body leaves 'action'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (KeyBindings.ActionOf(command) is not { } action)
        {
            return false;
        }

        if (!HeldActions.Contains(action))
        {
            // One-shot: fired on the press, and the release half above has nothing to undo. It counts, so the key is the
            // bind's and a menu shortcut on the same key does not fire it again (B825).
            Triggered?.Invoke(this, new ViewerActionEventArgs(action));
            return true;
        }

        Press(action, key ?? AliasIssued);
        return true;
    }

    /// <summary>Presses a button, tracking which key did it.</summary>
    private void Press(ViewerAction action, string key)
    {
        if (!_buttons.TryGetValue(action, out Button? button))
        {
            _buttons[action] = button = new Button();
        }

        button.Press(key);
    }

    /// <summary>Releases a button on behalf of one key, or unconditionally when none is named.</summary>
    /// <returns>Whether there was a button to release.</returns>
    private bool Release(ViewerAction action, string? key)
    {
        if (!_buttons.TryGetValue(action, out Button? button))
        {
            return false;
        }

        button.Release(key);
        return true;
    }

    /// <summary>Every action a command reaches by expansion.</summary>
    /// <remarks>
    /// Used only for the static projection and the applied count. The runtime expands as it goes,
    /// because by then the alias table may have changed.
    /// </remarks>
    /// <param name="commandLine">The command line.</param>
    /// <param name="depth">How deep the expansion is.</param>
    /// <param name="walked">
    /// The aliases already expanded in this projection, each with the shallowest depth it was reached at. An alias is walked
    /// again only from a shallower depth, where the depth bound cuts less of it (B820 review 4, Sonnet); the answer is a set
    /// of actions and the table does not change while it is read, so `alias a "a;a;a;a"` bound to a key costs at most
    /// <see cref="MaximumExpansion"/> walks of each alias rather than 4^20 calls (B820 review 3, Sonnet).
    /// </param>
    private List<ViewerAction> Expand(string commandLine, int depth, Dictionary<string, int>? walked = null)
    {
        List<ViewerAction> found = [];
        walked ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (depth >= MaximumExpansion)
        {
            return found;
        }

        foreach (string clause in SourceConfig.Clauses(commandLine))
        {
            IReadOnlyList<string> tokens = SourceConfig.Tokens(clause);

            if (tokens.Count == 0 ||
                tokens[0].Equals("alias", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The dispatcher's order (B820): see `Run`.
            if (ConsultsAlias(tokens[0]) && _aliases.TryGetValue(tokens[0], out string? body))
            {
                if (!walked.TryGetValue(tokens[0], out int reached) || depth < reached)
                {
                    walked[tokens[0]] = depth;
                    found.AddRange(Expand(body, depth + 1, walked).Where(action => !found.Contains(action)));
                }

                continue;
            }

            // **The WHOLE clause first, then its verb** (B214). A bound command may carry an
            // argument — `bind "F5" "mat_fullbright 1"` is ordinary Source — and three of this
            // viewer's actions are distinguished only by that argument, since `mat_fullbright`
            // takes 0, 1 or 2. Matching `tokens[0]` alone made all three unreachable, and the
            // symptom was that they never appeared in a binding report at all.
            //
            // Rejoined with single spaces rather than compared against the raw text, so
            // `mat_fullbright  1` and a tab both resolve.
            ViewerAction? action =
                KeyBindings.ActionOf(string.Join(' ', tokens)) ?? KeyBindings.ActionOf(tokens[0]);

            if (action is { } named && !found.Contains(named))
            {
                found.Add(named);
            }
        }

        return found;
    }

    /// <summary>
    /// One button, as <c>kbutton_t</c>: two keys and a state word.
    /// </summary>
    /// <remarks>
    /// <code>
    /// struct kbutton_t
    /// {
    ///     // key nums holding it down
    ///     int   down[ 2 ];
    ///     // low bit is down state
    ///     int   state;
    /// };
    /// </code>
    ///
    /// The two slots are not decoration. `KeyUp` returns early while either is filled, which is what
    /// lets two keys bound to one action release independently.
    /// </remarks>
    private sealed class Button
    {
        private const int DownBit = 1;
        private const int ImpulseDown = 2;
        private const int ImpulseUp = 4;

        private readonly string?[] _down = new string?[2];
        private int _state;

        /// <summary>Whether the button is down.</summary>
        public bool Down => (_state & DownBit) != 0;

        /// <summary>Presses on behalf of a key.</summary>
        public void Press(string key)
        {
            if (key == _down[0] || key == _down[1])
            {
                return;         // repeating key
            }

            if (_down[0] is null)
            {
                _down[0] = key;
            }
            else if (_down[1] is null)
            {
                _down[1] = key;
            }
            else
            {
                // "Three keys down for a button" — Valve warns and drops it. The third key is
                // therefore never recorded, so its release is ignored too.
                return;
            }

            if ((_state & DownBit) != 0)
            {
                return;         // still down
            }

            _state |= DownBit | ImpulseDown;
        }

        /// <summary>Releases on behalf of a key.</summary>
        public void Release(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                // Valve's reset path: no key named, so everything lets go.
                _down[0] = _down[1] = null;
                _state = ImpulseUp;
                return;
            }

            if (_down[0] == key)
            {
                _down[0] = null;
            }
            else if (_down[1] == key)
            {
                _down[1] = null;
            }
            else
            {
                return;         // key up without corresponding down (menu pass through)
            }

            if (_down[0] is not null || _down[1] is not null)
            {
                return;         // some other key is still holding it down
            }

            if ((_state & DownBit) == 0)
            {
                return;         // still up
            }

            _state &= ~DownBit;
            _state |= ImpulseUp;
        }

        /// <summary>The fraction of the frame this was held, clearing the impulses as Valve does.</summary>
        public float Read()
        {
            bool impulseDown = (_state & ImpulseDown) != 0;
            bool impulseUp = (_state & ImpulseUp) != 0;
            bool down = (_state & DownBit) != 0;

            float value = (impulseDown, impulseUp, down) switch
            {
                (true, false, true) => 0.5f,        // pressed and held this frame
                (false, false, true) => 1f,         // held the entire frame
                (true, true, true) => 0.75f,        // released and re-pressed this frame
                (true, true, false) => 0.25f,       // pressed and released this frame
                _ => 0f,
            };

            _state &= DownBit;                      // clear impulses
            return value;
        }
    }
}

/// <summary>An action a config asked for.</summary>
/// <param name="action">The action.</param>
public sealed class ViewerActionEventArgs(ViewerAction action) : EventArgs
{
    /// <summary>The action.</summary>
    public ViewerAction Action { get; } = action;
}

/// <summary>A client ConVar a command set.</summary>
/// <param name="name">The name as typed.</param>
/// <param name="value">The value as typed.</param>
/// <param name="before">The value it held before the set, or null where nothing had set it (its default).</param>
public sealed class ConVarSetEventArgs(string name, string value, string? before = null) : EventArgs
{
    /// <summary>The name as typed.</summary>
    public string Name { get; } = name;

    /// <summary>The value as typed.</summary>
    public string Value { get; } = value;

    /// <summary>The value it held before the set, or null where nothing had set it (its default).</summary>
    public string? Before { get; } = before;
}

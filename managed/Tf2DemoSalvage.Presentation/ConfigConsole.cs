using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

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
/// **What it deliberately does not implement**: cvars, `exec`, `wait`, `toggle`, `incrementvar`,
/// and the several hundred game commands this viewer has no concept of. Those are skipped in
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
    private readonly Dictionary<string, string> _binds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ViewerAction, Button> _buttons = [];

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

    /// <summary>How many binds name something this viewer implements.</summary>
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

            foreach (string key in _binds.Keys)
            {
                if (Expand(CommandFor(key) ?? string.Empty, depth: 0).Count > 0)
                {
                    applied++;
                }
            }

            return applied;
        }
    }

    /// <summary>How many <c>bind</c> lines were seen, whether or not they meant anything here.</summary>
    /// <remarks>
    /// **The pair of numbers is what lets a caller say something true.** A config full of `mat_*`
    /// and one that failed to load are both "loaded"; only `0 applied of 206 binds` versus
    /// `0 of 0` distinguishes them.
    /// </remarks>
    public int Bound { get; private set; }

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
    /// **`FlyFast` is in the set, and it did not used to be** (D69). Shift was read straight off
    /// `Control.ModifierKeys`, on the grounds that a modifier's state is something the toolkit
    /// already knows. That stopped being true when the console took over the controls: `+speed` is
    /// a bound command like any other, so Shift has to be pressed INTO the console or the speed
    /// multiplier never fires — and it fails silently, as a camera that simply never goes fast.
    /// </remarks>
    public static IReadOnlySet<ViewerAction> HeldActions { get; } = new HashSet<ViewerAction>
    {
        ViewerAction.FlyForward,
        ViewerAction.FlyBack,
        ViewerAction.FlyLeft,
        ViewerAction.FlyRight,
        ViewerAction.FlyUp,
        ViewerAction.FlyDown,
        ViewerAction.FlyFast,
    };

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
        if (_binds.TryGetValue(key, out string? command) && Expand(command, depth: 0).Count > 0)
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
    public void Load(string? text)
    {
        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            foreach (string clause in SourceConfig.Clauses(line))
            {
                Define(clause);
            }
        }
    }

    /// <summary>Executes several configs in order, as a set of files layered on one another.</summary>
    /// <param name="texts">The configs' contents, earliest first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="texts"/> is null.</exception>
    /// <remarks>
    /// **A bind and the alias it names routinely live in different files.** The owner's `config.cfg`
    /// binds `w` to `+mfwd`, and `+mfwd` is defined in `autoexec.cfg`. Loading either alone finds no
    /// movement bindings at all — which is what happened, and what fifteen synthetic tests built
    /// from `config_default.cfg` could not show, because that file binds movement directly and so
    /// contains no alias to miss.
    /// </remarks>
    public void Load(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        foreach (string text in texts)
        {
            Load(text);
        }
    }

    /// <summary>Presses a key, running whatever it is bound to.</summary>
    /// <param name="key">The key's Source name, such as <c>w</c> or <c>MOUSE1</c>.</param>
    public void KeyDown(string key)
    {
        if (CommandFor(key ?? string.Empty) is not { } command)
        {
            return;
        }

        // The key travels with the command because a button tracks WHICH keys hold it — see
        // `KeyDown( kbutton_t *b, const char *c )`, where `c` is the key number.
        Run(command, key!, depth: 0);
    }

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
        if (CommandFor(key ?? string.Empty) is not { } command ||
            !command.StartsWith('+'))
        {
            return;
        }

        Run(string.Concat("-", command.AsSpan(1)), key!, depth: 0);
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
        Fast: IsHeld(ViewerAction.FlyFast));

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
    /// <see cref="ViewerAction.FlyFast"/>, whose default key was Shift, is left with nothing to
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
        string? installedGameFolder, ILoggerFactory loggers, ILogger config)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            string? game = installedGameFolder ?? Tf2ConfigFiles.DefaultGameFolder;

            if (game is null)
            {
                config.LogInformation(
                    "{Message}", "no TF2 install found; using the built-in bindings");
                return null;
            }

            IReadOnlyList<string> configs = Tf2ConfigFiles.Read(game, loggers.LogTo());

            if (configs.Count == 0)
            {
                config.LogInformation(
                    "{Message}", $"no configs under {game}; using the built-in bindings");
                return null;
            }

            Load(configs);

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

        foreach (string key in _binds.Keys)
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

        return new KeyBindings(bound);
    }

    /// <summary>Acts on one config clause.</summary>
    private void Define(string clause)
    {
        IReadOnlyList<string> tokens = SourceConfig.Tokens(clause);

        if (tokens.Count == 0)
        {
            return;
        }

        if (tokens[0].Equals("unbindall", StringComparison.OrdinalIgnoreCase))
        {
            _binds.Clear();
            Bound = 0;
            _claimed = null;
            return;
        }

        if (tokens[0].Equals("unbind", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 2)
        {
            _binds.Remove(tokens[1]);
            _claimed = null;
            return;
        }

        if (tokens[0].Equals("alias", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 2)
        {
            // `alias name` with no body clears it, and an empty body is legal — the null-cancel
            // script defines `alias none ""` precisely so a name can mean "do nothing".
            _aliases[tokens[1]] = tokens.Count >= 3 ? tokens[2] : string.Empty;
            return;
        }

        if (tokens[0].Equals("bind", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 3)
        {
            _binds[tokens[1]] = tokens[2];
            Bound++;
            _claimed = null;
        }

        // Everything else is a cvar, an exec, or a game command. Not ours, and not an error.
    }

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
    private void Run(string commandLine, string? key, int depth)
    {
        if (depth >= MaximumExpansion)
        {
            return;
        }

        foreach (string clause in SourceConfig.Clauses(commandLine))
        {
            IReadOnlyList<string> tokens = SourceConfig.Tokens(clause);

            if (tokens.Count == 0)
            {
                continue;
            }

            string command = tokens[0];

            // A nested `alias` DEFINES rather than runs, and this is the state machine's whole
            // mechanism: `+mfwd` ends with `alias checkfwd +forward`, so releasing the other key
            // later finds a different meaning than it would have found before.
            if (command.Equals("alias", StringComparison.OrdinalIgnoreCase))
            {
                Define(clause);
                continue;
            }

            if (_aliases.TryGetValue(command, out string? body))
            {
                // No key, deliberately — see the remark above.
                Run(body, key: null, depth + 1);
                continue;
            }

            Perform(command, key);
        }
    }

    /// <summary>Applies one fully-expanded command.</summary>
    private void Perform(string command, string? key)
    {
        if (command.StartsWith('-'))
        {
            if (KeyBindings.ActionOf(string.Concat("+", command.AsSpan(1))) is { } releasing)
            {
                Release(releasing, key);
            }

            return;
        }

        if (KeyBindings.ActionOf(command) is not { } action)
        {
            return;
        }

        if (!HeldActions.Contains(action))
        {
            // One-shot: fired on the press, and the release half above has nothing to undo.
            Triggered?.Invoke(this, new ViewerActionEventArgs(action));
            return;
        }

        Press(action, key ?? AliasIssued);
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
    private void Release(ViewerAction action, string? key)
    {
        if (_buttons.TryGetValue(action, out Button? button))
        {
            button.Release(key);
        }
    }

    /// <summary>Every action a command reaches by expansion.</summary>
    /// <remarks>
    /// Used only for the static projection and the applied count. The runtime expands as it goes,
    /// because by then the alias table may have changed.
    /// </remarks>
    private List<ViewerAction> Expand(string commandLine, int depth)
    {
        List<ViewerAction> found = [];

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

            if (_aliases.TryGetValue(tokens[0], out string? body))
            {
                found.AddRange(Expand(body, depth + 1));
                continue;
            }

            if (KeyBindings.ActionOf(tokens[0]) is { } action && !found.Contains(action))
            {
                found.Add(action);
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

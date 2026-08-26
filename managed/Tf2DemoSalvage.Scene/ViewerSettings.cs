using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Scene;

/// <summary>How the viewer fills the screen.</summary>
public enum FullScreenMode
{
    /// <summary>A borderless window sized to the display.</summary>
    /// <remarks>
    /// What TF2 itself calls borderless, and it covers the taskbar. Alt-tab is instant because
    /// nothing changes about the display, and other windows can still appear over it.
    /// </remarks>
    Borderless = 0,

    /// <summary>The swap chain takes the display.</summary>
    /// <remarks>
    /// A real mode change: presentation skips the desktop compositor, which is the lower-latency
    /// path, at the cost of a slower alt-tab and a display that must be handed back.
    /// </remarks>
    Exclusive = 1,
}

/// <summary>How much texture detail to load.</summary>
/// <remarks>
/// **This picks a mip level out of the game's own chain rather than resampling anything.** Valve
/// generated every size when the texture was made, so asking for the 256-pixel version of a
/// 2048-pixel texture is a smaller read and a smaller upload — not a downscale of something already
/// paid for.
///
/// It is a setting because the right answer depends on the machine and on the view: an overhead
/// shot of a whole map cannot show 2048-pixel detail on anything, while a camera behind a player's
/// eyes can.
/// </remarks>
public enum TextureQuality
{
    /// <summary>Full size, as the game loads it.</summary>
    Full = 0,

    /// <summary>Cap the longest edge at 1024 pixels.</summary>
    High = 1024,

    /// <summary>Cap the longest edge at 512 pixels.</summary>
    Medium = 512,

    /// <summary>Cap the longest edge at 256 pixels.</summary>
    Low = 256,
}

/// <summary>
/// Settings the viewer remembers between runs, in a Source-style <c>.cfg</c>.
/// </summary>
/// <remarks>
/// **The same shape as TF2's own <c>config.cfg</c>**: one command per line, a value after a space,
/// <c>//</c> for comments. That is a deliberate match rather than a flourish — someone who has
/// edited a Source config already knows how to edit this one, and it can be read and diffed
/// without a JSON viewer.
///
/// <code>
///   // TF2 Demo Salvage settings
///   mat_fullscreen_mode 1
///   fps_max 0
///   cl_showfps 2
/// </code>
///
/// Under LocalApplicationData rather than beside the executable: the program may sit in a
/// read-only folder, and settings are per-user in any case.
///
/// **Every failure to read is silent and yields defaults.** A settings file is a convenience;
/// refusing to start because one is missing, corrupt or locked would make a nicety into a
/// dependency. Failures to *write* are reported, because a preference that silently does not stick
/// is worse than one that says it did not.
///
/// **An unknown command is ignored rather than rejected**, which is exactly how Source treats a
/// cvar it does not have: a config written by a later version must not stop an earlier one
/// starting.
/// </remarks>
public sealed record ViewerSettings
{
    /// <summary>Command name for the full-screen mode.</summary>
    /// <remarks>
    /// **Ours, because Valve has none to copy** — `mat_fullscreen` is in neither `engine.dll` nor
    /// `materialsystem.dll`, checked by scanning both. The game changes mode through
    /// `mat_setvideomode &lt;w&gt; &lt;h&gt; &lt;windowed&gt;`, which answers a different question:
    /// this setting picks borderless versus exclusive, and borderless is not a thing the SDK-era
    /// engine offers at all.
    ///
    /// So D79 rule 2 applies rather than rule 1 — invent it in Valve's style, with a subsystem
    /// prefix. `mat_` because video mode is the material system's business in Source.
    /// </remarks>
    public const string FullScreenModeCommand = "mat_fullscreen_mode";

    /// <summary>What to say when DXGI declines exclusive full screen.</summary>
    /// <remarks>
    /// **This sentence was written out TWICE in `MainForm`** (B188, D90) — once where full screen is
    /// entered and once where the mode is changed — which is the repeated literal the standards
    /// forbid outright. Two copies of a sentence are two chances for a reword to reach one of them.
    ///
    /// **It says what happened AND what it did instead**, because exclusive being refused is not a
    /// failure: borderless always works, and a person who reads only "refused" will go looking for
    /// a broken setting.
    /// </remarks>
    public const string ExclusiveFullScreenRefused =
        "Exclusive full screen was refused; using borderless.";

    /// <summary>What to say when a setting could not be written to disk.</summary>
    /// <param name="failure">Why the write failed.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// **The setting still applied**, which is the half a bare error would lose: the value is live
    /// for this session and only the saving failed, so "could not save" alone would read as the
    /// change having been rejected.
    /// </remarks>
    public static string SavedForThisSessionOnly(string failure) =>
        "Setting saved for this session only: " + failure;

    /// <summary>Command name for the texture detail.</summary>
    public const string TextureQualityCommand = "texture_quality";

    /// <summary>Command name for the frame rate cap.</summary>
    /// <remarks>
    /// **Valve's name, and this was `frame_rate_limit` until the owner caught it**: *"our fps cap
    /// should be transfered to actual valve vocab, because peoples configs will have that"*. He is
    /// right and D79 already said so — rule 3, *never invent a name for something Valve already
    /// named*. His own `autoexec.cfg` carries `fps_max 0`, which is precisely the paste that has to
    /// work.
    ///
    /// It ships in `engine.dll`, whose help string is *"Frame rate limiter, cannot be set while
    /// connected to a server."*
    ///
    /// **Zero means uncapped in both**, so the semantics needed no adjustment to match.
    /// </remarks>
    public const string FrameRateLimitCommand = "fps_max";

    /// <summary>Command name for vertical sync.</summary>
    /// <remarks>Valve's name; ships in `engine.dll` and `materialsystem.dll`.</remarks>
    public const string VerticalSyncCommand = "mat_vsync";

    /// <summary>Command name for how much the viewer says about itself.</summary>
    /// <remarks>
    /// **Valve's name, and it ships** — `engine.dll` and `client.dll` both carry the string, and the
    /// engine's own help text elsewhere speaks of being "in developer mode". So this is D79 rule 1:
    /// the game already named this knob and we use its name.
    ///
    /// **It exists because the log had no second level at all, which made Debug meaningless.** The
    /// sink has always filtered on a settable <c>Minimum</c>, but nothing could change it, so a line
    /// written at Debug could never be read and demoting a noisy line to Debug was deletion wearing
    /// a comment. The owner: *"the sink shouldnt be refusing debug i dont think, idk why we are not
    /// already starting to build a, at least 2 level, logging form"*.
    ///
    /// 0 is the ordinary log, 1 adds the per-frame detail, 2 adds everything.
    /// </remarks>
    public const string DeveloperCommand = "developer";

    /// <summary>Command name for the frame rate meter.</summary>
    /// <remarks>
    /// Valve's name, declared in `src/game/client/vgui_fpspanel.cpp:27` and shipping in retail
    /// `client.dll`. <c>FpsMeter</c> reproduces what it draws.
    /// </remarks>
    public const string ShowFrameRateCommand = "cl_showfps";

    /// <summary>Command name for where screenshots are written.</summary>
    /// <remarks>
    /// **A setting rather than an environment variable, on the owner's direction**: "env vars are a
    /// pita because of the shell reboot, it should probably be a runtime/ startup setting". A
    /// variable has to be set in the shell that launches the viewer, so it is lost every time the
    /// terminal restarts and silently absent whenever the viewer is started any other way — by
    /// double-clicking a demo, which is the ordinary route.
    ///
    /// **Valve has no cvar for this to copy**, so the name is ours; TF2 writes to a fixed
    /// `tf/screenshots`. The reason it is settable at all is that the owner's C: drive is nearly
    /// full and captures once occupied 203 MB of it.
    ///
    /// Empty means beside the log, which is where captures went before this existed.
    ///
    /// **Prefixed `cl_` under D79 rule 2**, which asks for Valve's style where Valve has no name:
    /// lower case, subsystem prefix, underscores. It was bare `screenshot_folder`, which is the
    /// same miss as `frame_rate_limit` in a milder form — a name that looks like a cvar without
    /// belonging to any subsystem.
    /// </remarks>
    public const string ScreenshotFolderCommand = "cl_screenshot_folder";

    /// <summary>Command name for the viewmodel's field of view.</summary>
    /// <remarks>
    /// **TF2 lets a player change this, so this viewer does too** — the standing rule in
    /// <c>docs/findings/13-settings-parity.md</c>. It was very nearly shipped as a constant off the
    /// back of reading the SDK, which is the shape of miss that rule exists to catch: the number was
    /// right and the choice was taken away.
    ///
    /// Named as the game names it, so a frag-movie config can be pasted in.
    /// </remarks>
    public const string ViewmodelFieldOfViewCommand = "viewmodel_fov";

    /// <summary>Command name for the player's world field of view.</summary>
    /// <remarks>
    /// What a real config sets, so a pasted one works (D69).
    /// </remarks>
    public const string FieldOfViewCommand = "fov_desired";

    /// <summary>Command name for the field of view used while playing a demo.</summary>
    /// <remarks>
    /// **Valve's own cvar for exactly this program's job**, and finding it was the point of
    /// checking: `ConVar demo_fov_override( "demo_fov_override", "0", FCVAR_CLIENTDLL |
    /// FCVAR_DONTRECORD, "If nonzero, this value will be used to override FOV during demo
    /// playback." )` — <c>c_baseplayer.cpp:120</c>.
    ///
    /// **It wins over <see cref="FieldOfViewCommand"/> when nonzero, which is the engine's own
    /// precedence** (<c>:2438</c>): the engine asks whether a demo is playing and the override is
    /// greater than zero, and only then uses it.
    /// </remarks>
    public const string DemoFieldOfViewCommand = "demo_fov_override";

    /// <summary>The narrowest and widest field of view a demo may be watched at.</summary>
    /// <remarks>
    /// `clamp( demo_fov_override.GetFloat(), 10.0f, 90.0f )` — <c>c_baseplayer.cpp:2444</c>.
    /// </remarks>
    public const float MinimumFieldOfView = 10f;

    /// <inheritdoc cref="MinimumFieldOfView"/>
    public const float MaximumFieldOfView = 90f;

    /// <summary>The world field of view this viewer starts at, in degrees.</summary>
    /// <remarks>
    /// **90, which is the TOP of Valve's own demo-playback clamp rather than a departure from it.**
    /// The owner: *"we shouldnt have a hardcoded fov, but we should be defaulting to 90 not 75,
    /// even though tf2 does default to 75, but every comp player uses 90, and really every good
    /// player period uses 90"*.
    ///
    /// **Checked, and the check changed the answer.** TF2's LIVE default really is 75 — `ConVar
    /// default_fov( "default_fov", "75", FCVAR_CHEAT )`, <c>hl2_clientmode.cpp:17</c>. But for DEMO
    /// PLAYBACK the engine offers <see cref="DemoFieldOfViewCommand"/> and allows 10..90, so 90 is
    /// the widest the game itself will watch a demo at. The only thing this does differently is
    /// default that override ON; the value is Valve's own ceiling.
    ///
    /// **And it applies to the free camera, which was worth confirming rather than assuming.**
    /// `CalcRoamingView` — the engine's free-roaming spectator view — ends with `fov = GetFOV();`
    /// (<c>c_baseplayer.cpp:1646</c>), and `GetFOV` is where the demo override is applied. So a
    /// field of view is not a first-person-only setting.
    ///
    /// **The hardcoding was the worse half of the fault.** Three separate 75s were compiled in —
    /// `FreeCamera.FieldOfView`, `OverheadPlacement.For`'s default, and the call site — so the
    /// choice was nobody's. That is the miss `docs/findings/13-settings-parity.md` exists to catch:
    /// the number was right and the choice was taken away.
    /// </remarks>
    public const float DefaultFieldOfView = 90f;

    /// <summary>This viewer's frame cap, in frames a second.</summary>
    /// <remarks>
    /// **This was called `SourceFrameRateLimit` and documented as "Source's own <c>fps_max</c>
    /// ceiling". There is no such ceiling.** The owner, asked directly: *"there is no actual
    /// ceiling, nocap will run 1000 fps in the real game at certain places in certain maps"*. So
    /// the constant was not merely uncited, it asserted something false — and it asserted it in the
    /// one place a reader would take as authoritative, next to the number.
    ///
    /// The engine has a floor and no ceiling. `engine.dll` carries *"sv_cheats is 0 and fps_max is
    /// being limited to a minimum of 30 (or set to 0)"*, and nothing about a maximum; Valve's
    /// shipped configs never set `fps_max`.
    ///
    /// **Its default is 400, and this entry contradicted our own finding for weeks** (corrected
    /// 2026-08-26). It used to end *"its default could not be recovered from the binary, because the
    /// string pool pairs a cvar's name with its help text and not with its default"*. The reasoning
    /// about the pool is correct and the conclusion is not:
    /// `docs/findings/37-the-engines-demo-vocabulary.md` had already recovered
    /// `ConVar fps_max( "fps_max", "400", 0, ... )` — flags and all — by reconstructing the pooled
    /// NUMERIC block rather than reading adjacency.
    ///
    /// **So this was not a missing source, it was an unrevisited impossibility claim.** Nothing about
    /// finding 37 forced a re-read of a sentence saying the thing it established could not be known,
    /// which is how the two sat here disagreeing. See
    /// `docs/memory/an-impossibility-claim-expires.md`, and
    /// `docs/findings/40-the-game-ships-its-own-cvar-list.md` for the cheaper instrument that
    /// surfaced the contradiction.
    ///
    /// **300 is still OURS**, and it stands on its own measurement rather than on Valve: the swap chain
    /// presents asking for vertical sync and the viewer was still measured at about 600 frames a
    /// second, which is ten times any display and allocates every one of them.
    ///
    /// Kept as a worked example of `docs/memory/a-default-is-not-a-constant.md`. The invented
    /// citation survived weeks precisely because it was plausible and specific.
    /// </remarks>
    public const int DefaultFrameRateLimit = 300;

    /// <summary>The meter is off.</summary>
    /// <remarks>`cl_showfps`'s own default, from its declaration in `vgui_fpspanel.cpp`.</remarks>
    public const int FrameRateMeterOff = 0;

    /// <summary>Where screenshots are written, or null for beside the log.</summary>
    /// <remarks>
    /// Not validated here. Whether the folder can be created is a question about the disk at the
    /// moment a capture is taken, not about whether the config parsed, and answering it at load
    /// would refuse a setting that becomes valid the moment a drive is plugged in.
    /// </remarks>
    public string? ScreenshotFolder { get; init; }

    /// <summary>How full screen is entered.</summary>
    /// <remarks>
    /// Borderless is the default because it always works. Exclusive can be refused by DXGI — when
    /// another application holds the output, or on a WARP device — and a default that sometimes
    /// cannot be honoured is a default that produces support questions.
    /// </remarks>
    public FullScreenMode FullScreenMode { get; init; } = FullScreenMode.Borderless;

    /// <summary>How much texture detail to load.</summary>
    /// <remarks>
    /// **Full by default, which is the frag-movie baseline.** The recording configs people used
    /// for TF2 movies — Chris' maxquality, Lawena, mastercomfig's ultra — all set
    /// <c>mat_picmip -10</c>, meaning drop no mip levels at all. This viewer exists to look at
    /// demos closely, so it should start where those configs start rather than where a competitive
    /// FPS config does.
    ///
    /// **Measured, and it is nearly free.** Decoding every texture in cp_process_final:
    ///
    /// | cap | pixels | time |
    /// |---|---|---|
    /// | 256 | 40 MB | 0.25 s |
    /// | 512 | 120 MB | 0.34 s |
    /// | 1024 | 340 MB | 0.55 s |
    /// | full | 355 MB | 0.58 s |
    ///
    /// Full costs fifteen megabytes and three hundredths of a second over 1024, because very few
    /// TF2 world textures exceed 1024 pixels — the cap mostly binds on a handful of skyboxes. The
    /// lower settings exist for weaker hardware and for the overhead view, where 2048-pixel detail
    /// cannot be seen anyway.
    /// </remarks>
    public TextureQuality TextureQuality { get; init; } = TextureQuality.Full;

    /// <summary>Most frames a second to draw, or zero for no limit.</summary>
    /// <remarks>
    /// **Capped because the measurement said so.** The swap chain presents asking for vertical
    /// sync, and the viewer was still measured at about 600 frames a second — a driver forcing
    /// vsync off outranks the present call, so a program that wants a ceiling has to apply one
    /// itself. Six hundred frames a second is ten times what any display shows, and every one of
    /// them allocates.
    ///
    /// See <see cref="DefaultFrameRateLimit"/> for why 300 is ours rather than Valve's.
    ///
    /// Lower values are for recording rather than for weak hardware: 24 gives film cadence, 30 and
    /// 60 are the ordinary video rates. A viewer that can only run flat out forces a capture tool
    /// to resample, which is where judder comes from.
    ///
    /// **TF2 would refuse most of those, and this viewer deliberately does not.** `engine.dll`
    /// carries the string *"sv_cheats is 0 and fps_max is being limited to a minimum of 30 (or set
    /// to 0)"*, so the game clamps anything between 1 and 29 up to 30. That floor is there because
    /// a very low cap is an advantage in a live match — it is an anti-cheat measure, and there is
    /// no match here. A film cadence of 24 is exactly what a demo viewer should allow, so the clamp
    /// is not reproduced. A small, justified departure under D82.
    ///
    /// Zero is uncapped in both, kept expressible because measuring how fast the renderer can go is
    /// a real question — it is how the 600 was found.
    /// </remarks>
    public int FrameRateLimit { get; init; } = DefaultFrameRateLimit;

    /// <summary>How much the viewer says: 0 ordinary, 1 per-frame detail, 2 everything.</summary>
    /// <remarks>
    /// Maps onto the sink's minimum level — 0 is Information, 1 is Debug, 2 is Trace — so a line
    /// written at Debug is genuinely off by default and genuinely reachable, which it was not
    /// before.
    /// </remarks>
    public int Developer { get; init; }

    /// <summary>The minimum level the log sink should accept, for this <see cref="Developer"/>.</summary>
    /// <remarks>
    /// **The mapping above, made executable** (B208). It was stated in `Developer`'s own
    /// documentation here and implemented as a `switch` inside `MainForm.ApplyLogVerbosity` — the
    /// same rule written twice, in two projects, with only one of them running. A prose copy of a
    /// rule is a copy that can go quietly wrong.
    ///
    /// **`>= 2` rather than `== 2`**, because `developer` is a Source ConVar and a user may type any
    /// number into it. `ViewerSettings` clamps to 0..2 when reading a config, but a value set
    /// another way should still mean "as much as possible" rather than falling to the default.
    /// </remarks>
    public LogLevel Verbosity => Developer switch
    {
        >= 2 => LogLevel.Trace,
        1 => LogLevel.Debug,
        _ => LogLevel.Information,
    };

    /// <summary>Which frame rate meter to draw: 0 none, 1 instantaneous, 2 smoothed.</summary>
    /// <remarks>
    /// **An int rather than a bool, because `cl_showfps` is an int and its two modes differ.** One
    /// shows the raw rate; two shows a moving average with the worst and best single frame beside
    /// it. The second is the one worth having for B163 — the owner cannot currently tell demo
    /// stutter from decode stutter from frame stutter, and a low watermark is what distinguishes an
    /// occasional long frame from a low average.
    ///
    /// Off by default, as the game has it.
    /// </remarks>
    public int ShowFrameRate { get; init; } = FrameRateMeterOff;

    /// <summary>The field of view the first-person weapon is drawn with, in degrees.</summary>
    /// <remarks>
    /// **The game's own default and the game's own limits.** <c>viewmodel_fov</c> is declared in
    /// <c>view.cpp:111</c> with a default of 54 and, in the TF2 build, hard bounds of 54 and 70:
    ///
    /// <code>
    /// ConVar v_viewmodel_fov( "viewmodel_fov", "54", FCVAR_ARCHIVE, ..., true, 54, true, 70, NULL );
    /// </code>
    ///
    /// So a player can raise it and cannot lower it, and this reproduces both ends rather than
    /// picking its own. A value outside them is clamped rather than refused, which is what the
    /// engine's own ConVar bounds do.
    ///
    /// **The DEFAULT here is 70, the top of that range, and not the game's 54** (D43). The owner
    /// asked for it after trying to check the hands: "the 55 doesnt let me see the hands or arms to
    /// check those". This is a tool for looking at what a demo contains, and at 54 the arms sit
    /// mostly outside the frame.
    ///
    /// **It is a divergence from the game's default and not from the game's behaviour**, which is
    /// the distinction that makes it acceptable here. 70 is a value a TF2 player can set, so every
    /// frame drawn at it is a frame the engine could draw; nothing is being invented. The bounds are
    /// still the engine's, so a config asking for 90 gets 70 exactly as it would in game.
    ///
    /// **TF2 reads a different convar while a demo plays** — <c>viewmodel_fov_demo</c>, same
    /// default — and this viewer is always in that case. One setting covers both because their
    /// defaults agree; if a future TF2 separates them, this is the note that says which one applies.
    /// </remarks>
    public float ViewmodelFieldOfView { get; init; } = ViewmodelPass.LargestFieldOfView;

    /// <summary>The world field of view, in degrees.</summary>
    /// <remarks>
    /// **Settable because the game lets a player set it, which is the whole rule** (D69,
    /// <c>docs/findings/13-settings-parity.md</c>). It was compiled in three separate places before
    /// — <see cref="FreeCamera.FieldOfView"/>, <c>OverheadPlacement.For</c>'s default and the call
    /// site — so the choice belonged to nobody. The owner's point when this came up: *"that is
    /// exactly why i want our settings to be settable from a config file, it makes changing them and
    /// changing defaults free"*.
    ///
    /// Two names are honoured and the demo one wins, which is the engine's own precedence — see
    /// <see cref="DemoFieldOfViewCommand"/>. Clamped to 10..90, the range the engine allows a demo
    /// to be watched at.
    /// </remarks>
    public float FieldOfView { get; init; } = DefaultFieldOfView;

    /// <summary>Whether to present in step with the display's refresh.</summary>
    /// <remarks>
    /// **Off by default, deliberately.** It adds latency, and a machine whose driver disables it
    /// globally will ignore the request anyway — so a default of on would be a setting that
    /// silently does nothing on the machine this was built on. The frame limit above is the
    /// mechanism that actually holds.
    /// </remarks>
    public bool VerticalSync { get; init; }

    /// <summary>Where the settings file lives.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage",
        "settings.cfg");

    /// <summary>Reads the settings, or returns defaults.</summary>
    /// <param name="path">File to read; defaults to <see cref="Path"/>.</param>
    /// <returns>The settings.</returns>
    public static ViewerSettings Load(string? path = null)
    {
        string file = path ?? Path;

        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : new ViewerSettings();
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Deliberately silent. See the remarks on the type: a settings file that cannot be
            // read must not stop the viewer opening a demo.
            return new ViewerSettings();
        }
    }

    /// <summary>Parses config text.</summary>
    /// <param name="text">The file's contents.</param>
    /// <param name="onto">Settings to layer onto, or null to start from the defaults.</param>
    /// <returns>The settings, with defaults for anything absent or unreadable.</returns>
    /// <remarks>
    /// A value that is not a number keeps the default rather than failing the whole file, for the
    /// same reason an unknown command is ignored: one bad line must not cost every other setting.
    /// </remarks>
    public static ViewerSettings Parse(string? text, ViewerSettings? onto = null)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            ReadOnlySpan<char> content = line.AsSpan();
            int comment = content.IndexOf("//", StringComparison.Ordinal);

            if (comment >= 0)
            {
                content = content[..comment];
            }

            content = content.Trim();

            if (content.IsEmpty)
            {
                continue;
            }

            int space = content.IndexOfAny(' ', '\t');

            if (space <= 0)
            {
                continue;
            }

            // Quotes around a value are accepted and stripped, as Source accepts them.
            values[content[..space].ToString()] = content[(space + 1)..].Trim().Trim('"').ToString();
        }

        // **Defaults unless the caller supplies something to layer onto.** A config file starts from
        // defaults; a `+command value` on the command line starts from whatever the config already
        // said, so that passing one setting at startup does not silently reset every other.
        ViewerSettings settings = onto ?? new ViewerSettings();

        if (Read(values, FullScreenModeCommand) is { } mode && Enum.IsDefined((FullScreenMode)mode))
        {
            settings = settings with { FullScreenMode = (FullScreenMode)mode };
        }

        if (Read(values, TextureQualityCommand) is { } quality &&
            Enum.IsDefined((TextureQuality)quality))
        {
            settings = settings with { TextureQuality = (TextureQuality)quality };
        }

        // **Negative is ignored rather than obeyed.** A hand-edited file can say anything, and a
        // negative budget makes every frame overdue - which is not "uncapped", it is a cap that
        // looks broken. Zero is the way to say uncapped and is accepted.
        if (Read(values, FrameRateLimitCommand) is { } limit && limit >= 0)
        {
            settings = settings with { FrameRateLimit = limit };
        }

        // **Clamped rather than refused, which is what a ConVar with bounds does.** TF2 declares
        // this one `true, 54, true, 70`, so a config asking for 90 gets 70 in the game and gets 70
        // here — refusing it instead would be this viewer disagreeing with the file it was handed.
        if (ReadNumber(values, ViewmodelFieldOfViewCommand) is { } viewmodelFov)
        {
            settings = settings with
            {
                ViewmodelFieldOfView = Math.Clamp(
                    viewmodelFov,
                    ViewmodelPass.SmallestFieldOfView,
                    ViewmodelPass.LargestFieldOfView),
            };
        }

        // **Both names, and the demo one wins, which is the engine's own precedence.** A config
        // pasted from TF2 sets `fov_desired`; `demo_fov_override` exists specifically to override
        // FOV during demo playback and the engine prefers it when nonzero
        // (`c_baseplayer.cpp:2438`). This viewer is always in the demo case, so honouring only one
        // would either ignore a real config or ignore the setting made for exactly this program.
        //
        // Clamped rather than refused, like the viewmodel above: 10..90 is what
        // `clamp( demo_fov_override.GetFloat(), 10.0f, 90.0f )` allows (`:2444`).
        if (ReadNumber(values, FieldOfViewCommand) is { } desiredFov)
        {
            settings = settings with
            {
                FieldOfView = Math.Clamp(desiredFov, MinimumFieldOfView, MaximumFieldOfView),
            };
        }

        if (ReadNumber(values, DemoFieldOfViewCommand) is { } demoFov and > 0f)
        {
            settings = settings with
            {
                FieldOfView = Math.Clamp(demoFov, MinimumFieldOfView, MaximumFieldOfView),
            };
        }

        // **A string, so it is read from the dictionary rather than through Read.** Every other
        // setting here is a number and the helpers only parse numbers; a path is neither numeric
        // nor bounded, and the only thing to do with it is take it as written.
        if (values.TryGetValue(ScreenshotFolderCommand, out string? folder) &&
            !string.IsNullOrWhiteSpace(folder))
        {
            settings = settings with { ScreenshotFolder = folder };
        }

        if (Read(values, VerticalSyncCommand) is { } sync)
        {
            settings = settings with { VerticalSync = sync != 0 };
        }

        // **Normalised the way the panel reads it, which is not the way a range check would.**
        // `ShouldDraw` tests `cl_showfps.GetInt()` for TRUTH and `Paint` then asks `== 2`, so in
        // the game every non-zero value that is not 2 — including a negative one — draws the
        // unsmoothed meter. Reproduced rather than tightened: rejecting `cl_showfps 3` would be
        // this viewer disagreeing with a config the game accepts, and the whole point of taking
        // Valve's name is that the same line means the same thing (D79).
        // Clamped rather than refused, as a ConVar with bounds is: `developer 9` in a config from
        // somewhere else should turn the detail up, not be ignored.
        if (Read(values, DeveloperCommand) is { } developer)
        {
            settings = settings with { Developer = Math.Clamp(developer, 0, 2) };
        }

        if (Read(values, ShowFrameRateCommand) is { } meter)
        {
            settings = settings with
            {
                ShowFrameRate = meter switch
                {
                    0 => FrameRateMeterOff,
                    2 => 2,
                    _ => 1,
                },
            };
        }

        return settings;
    }

    /// <summary>Writes the settings.</summary>
    /// <param name="path">File to write; defaults to <see cref="Path"/>.</param>
    /// <returns>An error to report, or null on success.</returns>
    /// <remarks>
    /// Returns the failure rather than throwing or swallowing it. The caller has a status line and
    /// is the only place that knows whether anyone is watching.
    /// </remarks>
    public string? Save(string? path = null)
    {
        string file = path ?? Path;

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Write(), Encoding.UTF8);
            return null;
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return failure.Message;
        }
    }

    /// <summary>The values a setting takes when the file says nothing about it.</summary>
    private static ViewerSettings Defaults { get; } = new();

    /// <summary>Writes one setting, commented out when it still holds its default.</summary>
    /// <param name="text">The file being built.</param>
    /// <param name="command">The command name.</param>
    /// <param name="value">Its current value, already formatted.</param>
    /// <param name="isDefault">Whether that value is the default.</param>
    /// <remarks>
    /// **A default that is written into the file stops being a default.** Every setting used to be
    /// written on the first run, so a config recorded the program's opinions as though they were the
    /// user's — and changing a default afterwards reached nobody who had ever run the viewer. The
    /// owner hit exactly that: the viewmodel field of view was changed to 70 and their file, written
    /// months earlier, pinned 54. The change appeared to do nothing, and nothing could distinguish
    /// "I chose 54" from "54 was written for me before you changed it".
    ///
    /// So a default is written as a COMMENT. The file still documents every setting and its current
    /// default, which is what made writing them all attractive in the first place; uncommenting a
    /// line is how a choice is made; and a value the user never chose stays a default for ever,
    /// following the program.
    /// </remarks>
    private static void Setting(StringBuilder text, string command, string value, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(text);

        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture, $"{(isDefault ? "// " : string.Empty)}{command} {value}"));
    }

    /// <summary>Renders the settings as config text.</summary>
    /// <returns>The file's contents.</returns>
    /// <remarks>
    /// Commented, because a config nobody can read without the source is a config nobody edits.
    ///
    /// **Settings still at their default are written commented out**, so that changing a default in
    /// a later version reaches everybody who never chose otherwise. See <see cref="Setting"/>.
    /// </remarks>
    public string Write()
    {
        StringBuilder text = new();

        text.AppendLine("// TF2 Demo Salvage settings");
        text.AppendLine("// Edit by hand if you like; unknown commands are ignored.");
        text.AppendLine("//");
        text.AppendLine("// This is the viewer's OWN config, and it is not your TF2 config. Nothing");
        text.AppendLine("// here is ever written back into the game's files. Settings TF2 already");
        text.AppendLine("// has a cvar for keep the game's name, so a line copied from your config");
        text.AppendLine("// means the same thing here; settings TF2 has no equivalent for -- ");
        text.AppendLine("// cl_screenshot_folder is one -- are named in the same style but exist");
        text.AppendLine("// only here, so this file never invents a cvar the game does have.");
        text.AppendLine("//");
        text.AppendLine("// Anything here can also be passed at startup as +command value, which is");
        text.AppendLine("// how Source sets a cvar from a launch option. A value passed that way");
        text.AppendLine("// applies to that run only and does not rewrite this file.");
        text.AppendLine("//");
        text.AppendLine("// A line that is commented out is still at its default, and will follow");
        text.AppendLine("// that default if a later version changes it. Uncomment to pin a value.");
        text.AppendLine();
        text.AppendLine("// 0 = borderless (covers the taskbar), 1 = exclusive (a real mode change)");
        Setting(
            text,
            FullScreenModeCommand,
            ((int)FullScreenMode).ToString(CultureInfo.InvariantCulture),
            FullScreenMode == Defaults.FullScreenMode);
        text.AppendLine();
        text.AppendLine("// Largest texture edge to load, in pixels. 0 loads them at full size.");
        Setting(
            text,
            TextureQualityCommand,
            ((int)TextureQuality).ToString(CultureInfo.InvariantCulture),
            TextureQuality == Defaults.TextureQuality);
        text.AppendLine();
        text.AppendLine("// Most frames a second to draw, as in TF2. 0 is uncapped, and uncapped");
        text.AppendLine("// really is uncapped -- there is no engine ceiling. 300 is this viewer's");
        text.AppendLine("// own default. 24 gives film cadence and 30 or 60 the ordinary video");
        text.AppendLine("// rates; TF2 would clamp anything under 30 up to it, and this does not.");
        Setting(
            text,
            FrameRateLimitCommand,
            FrameRateLimit.ToString(CultureInfo.InvariantCulture),
            FrameRateLimit == Defaults.FrameRateLimit);
        text.AppendLine();
        text.AppendLine("// Field of view for the weapon in your hands, in degrees. TF2 allows 54");
        text.AppendLine("// to 70 and defaults to 54; anything outside that is clamped, as in game.");
        text.AppendLine("// This viewer defaults to 70 instead, because at 54 the arms are mostly");
        text.AppendLine("// out of frame and this is a tool for looking at them. Set 54 for parity.");
        Setting(
            text,
            ViewmodelFieldOfViewCommand,
            ViewmodelFieldOfView.ToString("0.##", CultureInfo.InvariantCulture),

            // Compared with a tolerance, because this one is a float and a config round-trips it
            // through two decimal places. An exact comparison would call 70 "chosen" after a save.
            Math.Abs(ViewmodelFieldOfView - Defaults.ViewmodelFieldOfView) < 0.005f);
        text.AppendLine();
        text.AppendLine("// Field of view for the world, in degrees. The game allows 10 to 90 while");
        text.AppendLine("// a demo plays and defaults to 75 in live play; this viewer defaults to 90,");
        text.AppendLine("// which is the widest the game itself will watch a demo at and what most");
        text.AppendLine("// players use. `demo_fov_override` overrides this when set, as in game.");
        Setting(
            text,
            FieldOfViewCommand,
            FieldOfView.ToString("0.##", CultureInfo.InvariantCulture),
            Math.Abs(FieldOfView - Defaults.FieldOfView) < 0.005f);
        text.AppendLine();
        text.AppendLine("// Where screenshots go. Empty writes them beside this file's folder.");
        text.AppendLine("// Point it at another drive to keep a long history without spending the");
        text.AppendLine("// system disk; a run of the UI suite writes captures too.");
        Setting(
            text,
            ScreenshotFolderCommand,
            ScreenshotFolder is { Length: > 0 } where ? $"\"{where}\"" : string.Empty,
            ScreenshotFolder is null);
        text.AppendLine();
        text.AppendLine("// 1 presents in step with the display. Off by default: it adds latency,");
        text.AppendLine("// and a driver that disables it globally ignores the request anyway.");
        Setting(
            text,
            VerticalSyncCommand,
            (VerticalSync ? 1 : 0).ToString(CultureInfo.InvariantCulture),
            VerticalSync == Defaults.VerticalSync);
        text.AppendLine();
        text.AppendLine("// How much the viewer writes to its log. 0 is the ordinary account of what");
        text.AppendLine("// it loaded and decided; 1 adds the per-frame detail, which is thousands of");
        text.AppendLine("// lines a second and is for chasing a specific fault; 2 adds everything.");
        Setting(
            text,
            DeveloperCommand,
            Developer.ToString(CultureInfo.InvariantCulture),
            Developer == Defaults.Developer);
        text.AppendLine();
        text.AppendLine("// Frame rate meter, exactly as TF2 draws it: 1 is the raw rate, 2 is a");
        text.AppendLine("// moving average with the worst and best single frame in brackets beside");
        text.AppendLine("// it. 2 is the one worth having when something is stuttering -- a low");
        text.AppendLine("// watermark tells an occasional long frame from a low average.");
        Setting(
            text,
            ShowFrameRateCommand,
            ShowFrameRate.ToString(CultureInfo.InvariantCulture),
            ShowFrameRate == Defaults.ShowFrameRate);

        return text.ToString();
    }

    private static int? Read(Dictionary<string, string> values, string command) =>
        values.TryGetValue(command, out string? value) &&
        int.TryParse(value, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;

    /// <summary>Reads a fractional setting, for the ones the game states as floats.</summary>
    private static float? ReadNumber(Dictionary<string, string> values, string command) =>
        values.TryGetValue(command, out string? value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : null;
}

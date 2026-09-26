using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::AnimationController` (vgui2/vgui_controls/AnimationController.cpp): the `hudanimations` scripts, run.</summary>
/// <remarks>
/// An invisible, proportional child of the viewport. Scripts are `event` blocks of commands (`ParseScriptFile`, :291);
/// a sequence name seen twice keeps the FIRST definition — the code removes the new one, whatever its comment says.
/// `StartAnimationSequence` (:1045) queues the sequence's `Animate` commands and posts the rest; `UpdateAnimations`
/// (:903) fires due messages, each event once a frame per parent (:698), then steps every active animation from the
/// value it had when it began (:815). Values go through the panel's own properties for position, size and colours and
/// through `RequestInfo`/`SetInfo` for everything else — so an int or bool variable, which reports as an int, starts
/// from 0. **Not modelled:** `PlaySound` (reported through <see cref="SoundPlayed"/>), `FireCommand` (no panel here takes
/// a command), `SetInputEnabled` (no input), `CanAnimate` (only `HudScope` refuses).
/// </remarks>
public sealed class VguiAnimationController : VguiPanel
{
    private const int TokenLength = 512;

    // `g_AlignmentLookup` (:167).
    private static readonly (string Name, VguiAlignment Alignment)[] AlignmentNames =
    [
        ("northwest", VguiAlignment.NorthWest), ("north", VguiAlignment.North), ("northeast", VguiAlignment.NorthEast),
        ("west", VguiAlignment.West), ("center", VguiAlignment.Center), ("east", VguiAlignment.East),
        ("southwest", VguiAlignment.SouthWest), ("south", VguiAlignment.South), ("southeast", VguiAlignment.SouthEast),
        ("nw", VguiAlignment.NorthWest), ("n", VguiAlignment.North), ("ne", VguiAlignment.NorthEast),
        ("w", VguiAlignment.West), ("c", VguiAlignment.Center), ("e", VguiAlignment.East),
        ("sw", VguiAlignment.SouthWest), ("s", VguiAlignment.South), ("se", VguiAlignment.SouthEast),
    ];

    // vstdlib's global `RandomFloat` — one `CUniformRandomStream` shared by everything that draws from it.
    private static readonly Core.Primitives.UniformRandomStream Flicker = new();
    private static float _lastBiasExponent;

    private readonly List<Sequence> _sequences = [];
    private readonly List<ActiveAnimation> _active = [];
    private readonly List<PostedMessage> _posted = [];
    private readonly List<string> _scriptFiles = [];
    private (int X, int Y, int Wide, int Tall) _screenBounds = (-1, -1, -1, -1);
    private VguiPanel? _sizingPanel;
    private Func<string, byte[]?> _read = _ => null;
    private VguiContext? _context;
    private float _currentTime;

    /// <summary>`AnimationController( parent )`: invisible and proportional.</summary>
    /// <param name="parent">The viewport.</param>
    public VguiAnimationController(VguiPanel parent)
        : base(parent, null)
    {
        Visible = false;
        Proportional = true;
    }

    /// <summary>The interpolators (`Interpolators_e`).</summary>
    public enum Interpolator
    {
        /// <summary>`INTERPOLATOR_LINEAR`.</summary>
        Linear,

        /// <summary>`INTERPOLATOR_ACCEL`: the fraction squared.</summary>
        Accel,

        /// <summary>`INTERPOLATOR_DEACCEL`: its square root.</summary>
        Deaccel,

        /// <summary>`INTERPOLATOR_PULSE`: a cosine at the given frequency.</summary>
        Pulse,

        /// <summary>`INTERPOLATOR_FLICKER`: 1 with the given probability, else 0.</summary>
        Flicker,

        /// <summary>`INTERPOLATOR_SIMPLESPLINE`: ease in and out.</summary>
        SimpleSpline,

        /// <summary>`INTERPOLATOR_BOUNCE`: three falling bounces.</summary>
        Bounce,

        /// <summary>`INTERPOLATOR_BIAS`.</summary>
        Bias,

        /// <summary>`INTERPOLATOR_GAIN`.</summary>
        Gain,
    }

    private enum CommandType
    {
        Animate,
        RunEvent,
        StopEvent,
        StopAnimation,
        StopPanelAnimations,
        SetFont,
        SetTexture,
        SetString,
        RunEventChild,
        FireCommand,
        PlaySound,
        SetVisible,
        SetInputEnabled,
    }

    /// <summary>`surface()->PlaySound` from a script's `PlaySound`, with the sound's name; null plays nothing.</summary>
    public Action<string>? SoundPlayed { get; set; }

    /// <inheritdoc/>
    public override string ClassName => "AnimationController";

    /// <summary>How many sequences the scripts defined.</summary>
    public int SequenceCount => _sequences.Count;

    /// <summary>How many animations are running — `GetNumActiveAnimations`.</summary>
    public int ActiveAnimationCount => _active.Count;

    /// <summary>`SetScriptFile` (:86): the sizing panel kept, the file remembered for reloads and parsed.</summary>
    /// <param name="sizingPanel">The panel whose bounds `r` and `c` positions measure against.</param>
    /// <param name="fileName">The script.</param>
    /// <param name="wipeAll">Whether to forget every sequence and file first, and cancel what runs.</param>
    /// <param name="context">The scheme and screen.</param>
    /// <returns>Whether the file loaded and parsed.</returns>
    public bool SetScriptFile(VguiPanel sizingPanel, string fileName, bool wipeAll, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(context);

        _sizingPanel = sizingPanel;
        _context = context;
        _read = context.Read ?? (_ => null);

        if (wipeAll)
        {
            _sequences.Clear();
            _scriptFiles.Clear();
            CancelAllAnimations();
        }

        if (!_scriptFiles.Exists(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            _scriptFiles.Add(fileName);
        }

        UpdateScreenSize();

        return LoadScriptFile(fileName);
    }

    /// <summary>`UpdateAnimations` (:903): at a new screen size everything completes and the scripts reload; then messages and animations.</summary>
    /// <param name="currentTime">`gpGlobals->curtime`.</param>
    /// <param name="context">The scheme and screen.</param>
    public void UpdateAnimations(float currentTime, VguiContext context)
    {
        _context = context;
        _currentTime = currentTime;

        if (UpdateScreenSize() && _scriptFiles.Count > 0)
        {
            RunAllAnimationsToCompletion();
            ReloadScriptFile();
        }

        UpdatePostedMessages(runToCompletion: false);
        UpdateActiveAnimations(runToCompletion: false);
    }

    /// <summary>`RunAllAnimationsToCompletion`.</summary>
    public void RunAllAnimationsToCompletion()
    {
        UpdatePostedMessages(runToCompletion: true);
        UpdateActiveAnimations(runToCompletion: true);
    }

    /// <summary>`CancelAllAnimations`: every cancellable animation and message dropped.</summary>
    public void CancelAllAnimations()
    {
        _active.RemoveAll(animation => animation.CanBeCancelled);
        _posted.RemoveAll(message => message.CanBeCancelled);
    }

    /// <summary>`StartAnimationSequence( name )`: under the controller's parent.</summary>
    /// <param name="sequenceName">The event.</param>
    /// <param name="canBeCancelled">Whether a cancel may drop it.</param>
    /// <returns>Whether the sequence exists.</returns>
    public bool StartAnimationSequence(string sequenceName, bool canBeCancelled = true) =>
        StartAnimationSequence(Parent, sequenceName, canBeCancelled);

    /// <summary>`StartAnimationSequence( pWithinParent, name )` (:1045): what it queued before removed, then each command run.</summary>
    /// <param name="withinParent">The panel the sequence's panel names are searched under.</param>
    /// <param name="sequenceName">The event.</param>
    /// <param name="canBeCancelled">Whether a cancel may drop it.</param>
    /// <returns>Whether the sequence exists.</returns>
    public bool StartAnimationSequence(VguiPanel? withinParent, string sequenceName, bool canBeCancelled = true)
    {
        ArgumentNullException.ThrowIfNull(sequenceName);

        RemoveQueuedAnimationCommands(sequenceName, withinParent);

        if (Find(sequenceName) is not { } sequence)
        {
            return false;
        }

        foreach (Command command in sequence.Commands)
        {
            ExecAnimationCommand(sequenceName, command, withinParent, canBeCancelled);
        }

        return true;
    }

    /// <summary>`StopAnimationSequence` (:1087): what the sequence queued under that parent, removed.</summary>
    /// <param name="withinParent">The parent.</param>
    /// <param name="sequenceName">The event.</param>
    public void StopAnimationSequence(VguiPanel withinParent, string sequenceName) =>
        RemoveQueuedAnimationCommands(sequenceName, withinParent);

    /// <summary>`GetAnimationSequenceLength` (:1218): the longest `Animate`'s end, or 0.</summary>
    /// <param name="sequenceName">The event.</param>
    /// <returns>The length in seconds.</returns>
    public float GetAnimationSequenceLength(string sequenceName) => Find(sequenceName)?.Duration ?? 0f;

    /// <summary>`RunAnimationCommand` (:1144, :1179) from code: one variable animated now, clearing its earlier animation first.</summary>
    /// <param name="panel">The panel.</param>
    /// <param name="variable">The variable.</param>
    /// <param name="target">The target — a float in the first channel, or a colour in all four.</param>
    /// <param name="startDelay">Seconds from now.</param>
    /// <param name="duration">Seconds.</param>
    /// <param name="interpolator">The interpolator.</param>
    /// <param name="parameter">Its parameter.</param>
    /// <param name="clearValueQueue">Whether an animation of that variable on that panel is removed first.</param>
    /// <param name="canBeCancelled">Whether a cancel may drop it.</param>
    public void RunAnimationCommand(
        VguiPanel panel,
        string variable,
        (float A, float B, float C, float D) target,
        float startDelay,
        float duration,
        Interpolator interpolator,
        float parameter = 0f,
        bool clearValueQueue = true,
        bool canBeCancelled = true)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (clearValueQueue)
        {
            RemoveQueuedAnimationByType(panel, variable, sequenceToIgnore: null);
        }

        StartCmd_Animate(
            panel,
            sequenceName: null,
            new Command(CommandType.Animate) { Variable = variable, Target = target, Interpolator = interpolator, Parameter = parameter, Start = startDelay, Duration = duration },
            canBeCancelled);
    }

    /// <summary>`GetInterpolatedValue` (:950).</summary>
    /// <param name="interpolator">The interpolator.</param>
    /// <param name="parameter">Its parameter.</param>
    /// <param name="currentTime">Now.</param>
    /// <param name="startTime">When the animation began.</param>
    /// <param name="endTime">When it ends.</param>
    /// <param name="start">The value it began from.</param>
    /// <param name="end">The value it ends at.</param>
    /// <returns>The value now.</returns>
    public static (float A, float B, float C, float D) Interpolate(
        Interpolator interpolator,
        float parameter,
        float currentTime,
        float startTime,
        float endTime,
        (float A, float B, float C, float D) start,
        (float A, float B, float C, float D) end)
    {
        float pos = (currentTime - startTime) / (endTime - startTime);

        pos = interpolator switch
        {
            Interpolator.Accel => pos * pos,
            Interpolator.Deaccel => MathF.Sqrt(pos),
            Interpolator.SimpleSpline => (3 * pos * pos) - (2 * pos * pos * pos),

            // `cos` of a double — `M_PI` is one — narrowed back.
            Interpolator.Pulse => 0.5f + (0.5f * (float)Math.Cos(pos * 2.0f * Math.PI * parameter)),
            Interpolator.Bias => Bias(pos, parameter),
            Interpolator.Gain => pos < 0.5 ? 0.5f * Bias(2 * pos, 1 - parameter) : 1 - (0.5f * Bias(2 - (2 * pos), 1 - parameter)),
            Interpolator.Flicker => Flicker.RandomFloat(0f, 1f) < parameter ? 1f : 0f,
            Interpolator.Bounce => Bounce(pos),
            _ => pos,
        };

        return (
            ((end.A - start.A) * pos) + start.A,
            ((end.B - start.B) * pos) + start.B,
            ((end.C - start.C) * pos) + start.C,
            ((end.D - start.D) * pos) + start.D);
    }

    /// <summary>`Bias` (mathlib_base.cpp:1455): `lastAmt` is never updated, so the exponent is worked out every call — except
    /// for an amount of exactly -1, which matches the stale -1 and reuses the last exponent.</summary>
    private static float Bias(float x, float amount)
    {
        // `lastAmt != biasAmt` against a `lastAmt` stuck at -1: an exact comparison, as the engine makes it.
        if (BitConverter.SingleToInt32Bits(amount) != BitConverter.SingleToInt32Bits(-1f))
        {
            _lastBiasExponent = MathF.Log(amount) * -1.4427f;
        }

        return MathF.Pow(x, _lastBiasExponent);
    }

    private static float Bounce(float pos)
    {
        const float Hit1 = 0.33f;
        const float Hit2 = 0.67f;
        const float Hit3 = 1.0f;

        if (pos < Hit1)
        {
            return 1.0f - (float)Math.Sin(Math.PI * pos / Hit1);
        }

        if (pos < Hit2)
        {
            return 0.5f + (0.5f * (1.0f - (float)Math.Sin(Math.PI * (pos - Hit1) / (Hit2 - Hit1))));
        }

        return 0.8f + (0.2f * (1.0f - (float)Math.Sin(Math.PI * (pos - Hit2) / (Hit3 - Hit2))));
    }

    private Sequence? Find(string name)
    {
        foreach (Sequence sequence in _sequences)
        {
            if (string.Equals(sequence.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return sequence;
            }
        }

        return null;
    }

    private void ReloadScriptFile()
    {
        _sequences.Clear();
        UpdateScreenSize();

        foreach (string file in _scriptFiles)
        {
            if (file.Length > 0)
            {
                LoadScriptFile(file);
            }
        }
    }

    private bool LoadScriptFile(string fileName) =>
        _read(fileName) is { } bytes && ParseScriptFile(Encoding.UTF8.GetString(bytes));

    /// <summary>`UpdateScreenSize` (:873): the sizing panel's bounds, else the screen's.</summary>
    private bool UpdateScreenSize()
    {
        (int, int, int, int) bounds = _sizingPanel is { } sizing
            ? (sizing.X, sizing.Y, sizing.Wide, sizing.Tall)
            : (0, 0, _context?.ScreenWide ?? 0, _context?.ScreenTall ?? 0);

        bool changed = bounds != _screenBounds;

        _screenBounds = bounds;

        return changed;
    }

    /// <summary>`ParseScriptFile` (:291).</summary>
    private bool ParseScriptFile(string text)
    {
        VguiContext context = _context ?? throw new InvalidOperationException("A script is parsed with a scheme.");
        int position = 0;
        string token = ParseFile(text, ref position);

        while (token.Length > 0)
        {
            if (!string.Equals(token, "event", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string name = ParseFile(text, ref position);

            if (name.Length < 1)
            {
                return false;
            }

            Sequence sequence = new(name);
            bool accepted = true;

            _sequences.Add(sequence);
            token = ParseFile(text, ref position);

            if (IsConditional(token))
            {
                accepted = KeyValuesTree.EvaluateConditional(token);
                token = ParseFile(text, ref position);
            }

            if (!string.Equals(token, "{", StringComparison.Ordinal))
            {
                return false;
            }

            while (token.Length > 0)
            {
                token = ParseFile(text, ref position);

                if (token.Length > 0 && token[0] == '}')
                {
                    break;
                }

                if (ParseCommand(text, ref position, token, sequence, context) is not { } command)
                {
                    return false;
                }

                sequence.Commands.Add(command);

                // A conditional after a command decides whether the command stays.
                int peek = position;
                string next = ParseFile(text, ref peek);

                if (IsConditional(next))
                {
                    if (!KeyValuesTree.EvaluateConditional(next))
                    {
                        sequence.Commands.RemoveAt(sequence.Commands.Count - 1);
                    }

                    position = peek;
                }
            }

            // "Attempt to find a collision … replacing the old one" removes the NEW sequence — which changes nothing, since
            // every lookup takes the first of a name. So a rejected conditional is the only removal that matters.
            if (!accepted)
            {
                _sequences.RemoveAt(_sequences.Count - 1);
            }

            token = ParseFile(text, ref position);
        }

        return true;
    }

    private static bool IsConditional(string token) =>
        token.Contains("[$", StringComparison.OrdinalIgnoreCase) || token.Contains("[!$", StringComparison.OrdinalIgnoreCase);

    /// <summary>One command's tokens, as `ParseScriptFile` reads each kind.</summary>
    private Command? ParseCommand(string text, ref int position, string keyword, Sequence sequence, VguiContext context)
    {
        string Next(ref int at) => ParseFile(text, ref at);

        switch (keyword.ToUpperInvariant())
        {
            case "ANIMATE":
                {
                    Command animate = new(CommandType.Animate) { Panel = Next(ref position), Variable = Next(ref position) };
                    string target = Next(ref position);

                    ParseTarget(animate, target, context);
                    ParseInterpolator(animate, text, ref position);
                    animate.Start = PanelLayout.Atof(Next(ref position));
                    animate.Duration = PanelLayout.Atof(Next(ref position));
                    sequence.Duration = Math.Max(sequence.Duration, animate.Start + animate.Duration);

                    return animate;
                }

            case "RUNEVENT":
                return new Command(CommandType.RunEvent) { Event = Next(ref position), Delay = PanelLayout.Atof(Next(ref position)) };
            case "RUNEVENTCHILD":
                return new Command(CommandType.RunEventChild) { Variable = Next(ref position), Event = Next(ref position), Delay = PanelLayout.Atof(Next(ref position)) };
            case "FIRECOMMAND":
                return new Command(CommandType.FireCommand) { Delay = PanelLayout.Atof(Next(ref position)), Variable = Next(ref position) };
            case "PLAYSOUND":
                return new Command(CommandType.PlaySound) { Delay = PanelLayout.Atof(Next(ref position)), Variable = Next(ref position) };
            case "SETVISIBLE":
                return new Command(CommandType.SetVisible)
                {
                    Variable = Next(ref position), Flag = PanelLayout.Atoi(Next(ref position)), Delay = PanelLayout.Atof(Next(ref position)),
                };
            case "SETINPUTENABLED":
                return new Command(CommandType.SetInputEnabled)
                {
                    Variable = Next(ref position), Flag = PanelLayout.Atoi(Next(ref position)), Delay = PanelLayout.Atof(Next(ref position)),
                };
            case "STOPEVENT":
                return new Command(CommandType.StopEvent) { Event = Next(ref position), Delay = PanelLayout.Atof(Next(ref position)) };
            case "STOPPANELANIMATIONS":
                return new Command(CommandType.StopPanelAnimations) { Event = Next(ref position), Delay = PanelLayout.Atof(Next(ref position)) };
            case "STOPANIMATION":
                return new Command(CommandType.StopAnimation)
                {
                    Event = Next(ref position), Variable = Next(ref position), Delay = PanelLayout.Atof(Next(ref position)),
                };
            case "SETFONT":
            case "SETTEXTURE":
            case "SETSTRING":
                return new Command(keyword.ToUpperInvariant() switch { "SETFONT" => CommandType.SetFont, "SETTEXTURE" => CommandType.SetTexture, _ => CommandType.SetString })
                {
                    Event = Next(ref position), Variable = Next(ref position), Variable2 = Next(ref position), Delay = PanelLayout.Atof(Next(ref position)),
                };
            default:
                return null;
        }
    }

    /// <summary>An `Animate` target: positions measured and scaled, otherwise four floats or a scheme colour, sizes scaled.</summary>
    private void ParseTarget(Command animate, string target, VguiContext context)
    {
        (int screenWide, int screenTall) = (_screenBounds.Wide, _screenBounds.Tall);
        string variable = animate.Variable;

        if (Is(variable, "position"))
        {
            float x = SetupPosition(animate, target, screenWide, context);
            int at = 0;

            // The second word of the target, through a 32-character buffer.
            ParseFile(target, ref at, 32);
            string second = ParseFile(target, ref at, 32);

            animate.Target = (x, SetupPosition(animate, second, screenTall, context), 0f, 0f);
        }
        else if (Is(variable, "xpos") || Is(variable, "ypos"))
        {
            animate.Target = (SetupPosition(animate, target, Is(variable, "xpos") ? screenWide : screenTall, context), 0f, 0f, 0f);
        }
        else
        {
            Span<float> values = stackalloc float[4];

            if (PanelLayout.ScanFloats(target, values) == 0)
            {
                // "we don't have a way of seeing if the color is not declared in the scheme": ask again with pink.
                (byte, byte, byte, byte) colour = context.Scheme.GetColor(target, (0, 0, 0, 0));

                if (colour == (0, 0, 0, 0))
                {
                    colour = context.Scheme.GetColor(target, (255, 0, 255, 255));
                }

                animate.Target = (colour.Item1, colour.Item2, colour.Item3, colour.Item4);
            }
            else
            {
                animate.Target = (values[0], values[1], values[2], values[3]);
            }
        }

        // The controller is always proportional, so sizes scale — through an int.
        if (Is(variable, "size"))
        {
            animate.Target = (context.Scale((int)animate.Target.A), context.Scale((int)animate.Target.B), animate.Target.C, animate.Target.D);
        }
        else if (Is(variable, "wide") || Is(variable, "tall"))
        {
            animate.Target = animate.Target with { A = context.Scale((int)animate.Target.A) };
        }
    }

    private static void ParseInterpolator(Command animate, string text, ref int position)
    {
        string name = ParseFile(text, ref position);

        (animate.Interpolator, bool takesParameter) = name.ToUpperInvariant() switch
        {
            "ACCEL" => (Interpolator.Accel, false),
            "DEACCEL" => (Interpolator.Deaccel, false),
            "SPLINE" => (Interpolator.SimpleSpline, false),
            "PULSE" => (Interpolator.Pulse, true),
            "BIAS" => (Interpolator.Bias, true),
            "GAIN" => (Interpolator.Gain, true),
            "FLICKER" => (Interpolator.Flicker, true),
            "BOUNCE" => (Interpolator.Bounce, false),
            _ => (Interpolator.Linear, false),
        };

        if (takesParameter)
        {
            animate.Parameter = PanelLayout.Atof(ParseFile(text, ref position));
        }
    }

    /// <summary>`SetupPosition` (:212): `(alignment:panel)` relative, `r` from the far edge, `c` from the centre; scaled.</summary>
    private static float SetupPosition(Command animate, string text, int screenDimension, VguiContext context)
    {
        bool right = false;
        bool centre = false;
        string rest = text;

        if (rest.StartsWith('('))
        {
            rest = rest[1..];

            if (rest.Contains(')', StringComparison.Ordinal))
            {
                int colon = rest.IndexOf(':', StringComparison.Ordinal);

                if (colon >= 0)
                {
                    VguiAlignment alignment = LookupAlignment(rest[..colon]);
                    string panelName = rest[(colon + 1)..];
                    int close = panelName.IndexOf(')', StringComparison.Ordinal);

                    if (close >= 0 && close > 0)
                    {
                        animate.Align = (true, panelName[..close], alignment);
                    }
                }

                rest = rest[(rest.IndexOf(')', StringComparison.Ordinal) + 1)..];
            }
        }
        else if (rest.Length > 0 && rest[0] is 'r' or 'R')
        {
            right = true;
            rest = rest[1..];
        }
        else if (rest.Length > 0 && rest[0] is 'c' or 'C')
        {
            centre = true;
            rest = rest[1..];
        }

        int pos = context.Scale(PanelLayout.Atoi(rest));

        if (right)
        {
            pos = screenDimension - pos;
        }

        if (centre)
        {
            pos = (screenDimension / 2) + pos;
        }

        return pos;
    }

    private static VguiAlignment LookupAlignment(string token)
    {
        foreach ((string name, VguiAlignment alignment) in AlignmentNames)
        {
            if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase))
            {
                return alignment;
            }
        }

        return VguiAlignment.NorthWest;
    }

    /// <summary>`UpdatePostedMessages` (:698): each due message removed and run, the scan restarting after each.</summary>
    private void UpdatePostedMessages(bool runToCompletion)
    {
        List<(string Event, VguiPanel? Parent)> ranThisFrame = [];

        // "reset the count, start the whole queue again" after each message, because running one can change the queue.
        while (_posted.FindIndex(message => (message.CanBeCancelled || !runToCompletion) && (_currentTime >= message.StartTime || runToCompletion)) is var index and >= 0)
        {
            PostedMessage due = _posted[index];

            _posted.RemoveAt(index);

            if (due.Parent is not { } parent)
            {
                continue;
            }

            switch (due.Type)
            {
                case CommandType.RunEvent:
                    RunEventOnce(ranThisFrame, due.Event, parent, due.CanBeCancelled);
                    break;
                case CommandType.RunEventChild:
                    RunEventOnce(ranThisFrame, due.Event, parent.FindChildByName(due.Variable, recurseDown: true), due.CanBeCancelled);
                    break;
                case CommandType.PlaySound:
                    SoundPlayed?.Invoke(due.Variable);
                    break;
                case CommandType.SetVisible:
                    if (parent.FindChildByName(due.Variable, recurseDown: true) is { } shown)
                    {
                        shown.Visible = due.Flag == 1;
                    }

                    break;
                case CommandType.StopEvent:
                    RemoveQueuedAnimationCommands(due.Event, parent);
                    break;
                case CommandType.StopPanelAnimations:
                    if (FindSiblingByName(due.Event) is { } stopped)
                    {
                        _active.RemoveAll(animation => animation.Panel == stopped && !SameSequence(animation.Sequence, due.Sequence));
                    }

                    break;
                case CommandType.StopAnimation:
                    if (FindSiblingByName(due.Event) is { } target)
                    {
                        RemoveQueuedAnimationByType(target, due.Variable, due.Sequence);
                    }

                    break;
                case CommandType.SetFont:
                    parent.FindChildByName(due.Event, recurseDown: true)?.SetInfo(due.Variable, new VguiKeyValue(VguiKeyValueType.Text, Text: due.Variable2), Context);
                    break;
                case CommandType.SetTexture:
                case CommandType.SetString:
                    FindSiblingByName(due.Event)?.SetInfo(due.Variable, new VguiKeyValue(VguiKeyValueType.Text, Text: due.Variable2), Context);
                    break;
                default:
                    // `FireCommand` and `SetInputEnabled`: no panel here takes a command or input.
                    break;
            }
        }
    }

    private void RunEventOnce(List<(string Event, VguiPanel? Parent)> ranThisFrame, string eventName, VguiPanel? parent, bool canBeCancelled)
    {
        if (ranThisFrame.Exists(ran => ran.Parent == parent && string.Equals(ran.Event, eventName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ranThisFrame.Add((eventName, parent));
        StartAnimationSequence(parent, eventName, canBeCancelled);
    }

    /// <summary>`UpdateActiveAnimations` (:815).</summary>
    private void UpdateActiveAnimations(bool runToCompletion)
    {
        int index = 0;

        while (index < _active.Count)
        {
            ActiveAnimation animation = _active[index];

            if ((!animation.CanBeCancelled && runToCompletion) || (_currentTime < animation.StartTime && !runToCompletion))
            {
                index++;
                continue;
            }

            if (!animation.Started && !runToCompletion)
            {
                animation.StartValue = GetValue(animation);
                animation.Started = true;
            }

            bool done = _currentTime >= animation.EndTime || runToCompletion;
            (float, float, float, float) value = done
                ? animation.EndValue
                : Interpolate(animation.Interpolator, animation.Parameter, _currentTime, animation.StartTime, animation.EndTime, animation.StartValue, animation.EndValue);

            SetValue(animation, value);

            if (done)
            {
                _active.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }
    }

    private void RemoveQueuedAnimationCommands(string sequence, VguiPanel? withinParent)
    {
        _posted.RemoveAll(message => SameSequence(message.Sequence, sequence) && (withinParent is null || message.Parent == withinParent));
        _active.RemoveAll(animation =>
            SameSequence(animation.Sequence, sequence)
            && (withinParent is null || withinParent.FindChildByName(animation.Panel.Name, recurseDown: true) == animation.Panel));
    }

    /// <summary>`RemoveQueuedAnimationByType` (:1287): the FIRST matching animation only.</summary>
    private void RemoveQueuedAnimationByType(VguiPanel panel, string variable, string? sequenceToIgnore)
    {
        int index = _active.FindIndex(animation =>
            animation.Panel == panel && Is(animation.Variable, variable) && !SameSequence(animation.Sequence, sequenceToIgnore));

        if (index >= 0)
        {
            _active.RemoveAt(index);
        }
    }

    private void ExecAnimationCommand(string sequence, Command command, VguiPanel? withinParent, bool canBeCancelled)
    {
        if (command.Type == CommandType.Animate)
        {
            StartCmd_Animate(sequence, command, withinParent, canBeCancelled);
            return;
        }

        _posted.Add(new PostedMessage(
            command.Type, sequence, command.Event, command.Variable, command.Variable2, command.Flag, _currentTime + command.Delay, withinParent, canBeCancelled));
    }

    /// <summary>`StartCmd_Animate` (:1327): the panel under the parent — or the controller's parent itself, by name.</summary>
    private void StartCmd_Animate(string sequence, Command command, VguiPanel? withinParent, bool canBeCancelled)
    {
        if (withinParent is null)
        {
            return;
        }

        VguiPanel? panel = withinParent.FindChildByName(command.Panel, recurseDown: true);

        if (panel is null && Parent is { } parent && string.Equals(parent.Name, command.Panel, StringComparison.OrdinalIgnoreCase))
        {
            panel = parent;
        }

        if (panel is not null)
        {
            StartCmd_Animate(panel, sequence, command, canBeCancelled);
        }
    }

    private void StartCmd_Animate(VguiPanel panel, string? sequenceName, Command command, bool canBeCancelled)
    {
        float start = _currentTime + command.Start;

        _active.Add(new ActiveAnimation(panel, sequenceName, command.Variable, command.Interpolator, command.Parameter, start, start + command.Duration, command.Target, canBeCancelled, command.Align));
    }

    /// <summary>`GetRelativeOffset` (:1496): an edge or centre of the aligned panel — the centres taken as (x + w) / 2.</summary>
    private int GetRelativeOffset((bool Relative, string Panel, VguiAlignment Alignment) align, bool x)
    {
        if (!align.Relative || Parent?.FindChildByName(align.Panel, recurseDown: true) is not { } panel)
        {
            return 0;
        }

        (int left, int top, int wide, int tall) = (panel.X, panel.Y, panel.Wide, panel.Tall);

        return align.Alignment switch
        {
            VguiAlignment.North => x ? (left + wide) / 2 : top,
            VguiAlignment.NorthEast => x ? left + wide : top,
            VguiAlignment.West => x ? left : (top + tall) / 2,
            VguiAlignment.Center => x ? (left + wide) / 2 : (top + tall) / 2,
            VguiAlignment.East => x ? left + wide : (top + tall) / 2,
            VguiAlignment.SouthWest => x ? left : top + tall,
            VguiAlignment.South => x ? (left + wide) / 2 : top + tall,
            VguiAlignment.SouthEast => x ? left + wide : top + tall,
            _ => x ? left : top,
        };
    }

    /// <summary>`GetValue` (:1547).</summary>
    private (float, float, float, float) GetValue(ActiveAnimation animation)
    {
        VguiPanel panel = animation.Panel;
        string variable = animation.Variable;

        if (Is(variable, "position"))
        {
            return (panel.X - GetRelativeOffset(animation.Align, x: true), panel.Y - GetRelativeOffset(animation.Align, x: false), 0f, 0f);
        }

        if (Is(variable, "size"))
        {
            return (panel.Wide, panel.Tall, 0f, 0f);
        }

        if (Is(variable, "fgcolor") || Is(variable, "bgcolor"))
        {
            (byte r, byte g, byte b, byte a) = Is(variable, "fgcolor") ? panel.FgColor : panel.BgColor;

            return (r, g, b, a);
        }

        if (Is(variable, "xpos"))
        {
            return (panel.X - GetRelativeOffset(animation.Align, x: true), 0f, 0f, 0f);
        }

        if (Is(variable, "ypos"))
        {
            return (panel.Y - GetRelativeOffset(animation.Align, x: false), 0f, 0f, 0f);
        }

        if (Is(variable, "wide") || Is(variable, "tall"))
        {
            return (Is(variable, "wide") ? panel.Wide : panel.Tall, 0f, 0f, 0f);
        }

        // Only a float or a colour is read; an int, bool or string answer leaves zero.
        return panel.RequestInfo(variable, Context).Value switch
        {
            { Type: VguiKeyValueType.Real } real => (real.Real, 0f, 0f, 0f),
            { Type: VguiKeyValueType.Color } colour => (colour.Colour.Red, colour.Colour.Green, colour.Colour.Blue, colour.Colour.Alpha),
            _ => (0f, 0f, 0f, 0f),
        };
    }

    /// <summary>`SetValue` (:1639): positions and sizes truncated; colours cast to bytes; a value with only its first channel
    /// non-zero goes to `SetInfo` as a float, any other as a colour.</summary>
    private void SetValue(ActiveAnimation animation, (float A, float B, float C, float D) value)
    {
        VguiPanel panel = animation.Panel;
        string variable = animation.Variable;

        if (Is(variable, "position"))
        {
            (panel.X, panel.Y) = ((int)value.A + GetRelativeOffset(animation.Align, x: true), (int)value.B + GetRelativeOffset(animation.Align, x: false));
        }
        else if (Is(variable, "size"))
        {
            (panel.Wide, panel.Tall) = ((int)value.A, (int)value.B);
        }
        else if (Is(variable, "fgcolor"))
        {
            panel.FgColor = Bytes(value);
        }
        else if (Is(variable, "bgcolor"))
        {
            panel.BgColor = Bytes(value);
        }
        else if (Is(variable, "xpos"))
        {
            panel.X = (int)value.A + GetRelativeOffset(animation.Align, x: true);
        }
        else if (Is(variable, "ypos"))
        {
            panel.Y = (int)value.A + GetRelativeOffset(animation.Align, x: false);
        }
        else if (Is(variable, "wide"))
        {
            panel.Wide = (int)value.A;
        }
        else if (Is(variable, "tall"))
        {
            panel.Tall = (int)value.A;
        }
        else
        {
            VguiKeyValue info = value.B == 0f && value.C == 0f && value.D == 0f
                ? VguiKeyValue.FromReal(value.A)
                : VguiKeyValue.FromColour(Bytes(value));

            panel.SetInfo(variable, info, Context);
        }
    }

    /// <summary>`(unsigned char)` of a float: truncated, and the low byte kept.</summary>
    private static (byte, byte, byte, byte) Bytes((float A, float B, float C, float D) value) =>
        (unchecked((byte)(int)value.A), unchecked((byte)(int)value.B), unchecked((byte)(int)value.C), unchecked((byte)(int)value.D));

    private VguiContext Context => _context ?? throw new InvalidOperationException("The controller runs with a scheme.");

    /// <summary>`FindSiblingByName`: a direct child of the parent, by name without case.</summary>
    private VguiPanel? FindSiblingByName(string name) => Parent?.FindChildByName(name);

    private static bool Is(string variable, string name) => string.Equals(variable, name, StringComparison.OrdinalIgnoreCase);

    private static bool SameSequence(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>`ParseFile` (public/filesystem_helpers.cpp:29): white space and comments skipped, a quoted string whole, one
    /// of `{}()':` alone, otherwise a word up to white space or one of those.</summary>
    private static string ParseFile(string text, ref int position, int length = TokenLength)
    {
        while (true)
        {
            while (position < text.Length && text[position] <= ' ')
            {
                position++;
            }

            if (position >= text.Length)
            {
                return string.Empty;
            }

            if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '*')
            {
                int end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);

                position = end < 0 ? text.Length : end + 2;
                continue;
            }

            break;
        }

        StringBuilder token = new();

        if (text[position] == '"')
        {
            position++;

            while (position < text.Length && text[position] != '"' && token.Length < length - 1)
            {
                token.Append(text[position++]);
            }

            if (position < text.Length && text[position] == '"')
            {
                position++;
            }

            return token.ToString();
        }

        if (IsBreak(text[position]))
        {
            return text[position++].ToString(CultureInfo.InvariantCulture);
        }

        do
        {
            token.Append(text[position++]);
        }
        while (position < text.Length && token.Length < length - 1 && !IsBreak(text[position]) && text[position] > ' ');

        return token.ToString();
    }

    private static bool IsBreak(char character) => character is '{' or '}' or '(' or ')' or '\'' or ':';

    private sealed class Sequence(string name)
    {
        public string Name { get; } = name;

        public float Duration { get; set; }

        public List<Command> Commands { get; } = [];
    }

    private sealed class Command(CommandType type)
    {
        public CommandType Type { get; } = type;

        public string Panel { get; set; } = string.Empty;

        public string Variable { get; set; } = string.Empty;

        public string Variable2 { get; set; } = string.Empty;

        public string Event { get; set; } = string.Empty;

        public int Flag { get; set; }

        public float Delay { get; set; }

        public (float A, float B, float C, float D) Target { get; set; }

        public Interpolator Interpolator { get; set; }

        public float Parameter { get; set; }

        public float Start { get; set; }

        public float Duration { get; set; }

        public (bool Relative, string Panel, VguiAlignment Alignment) Align { get; set; } = (false, string.Empty, VguiAlignment.NorthWest);
    }

    private sealed class ActiveAnimation(
        VguiPanel panel,
        string? sequence,
        string variable,
        Interpolator interpolator,
        float parameter,
        float startTime,
        float endTime,
        (float, float, float, float) endValue,
        bool canBeCancelled,
        (bool Relative, string Panel, VguiAlignment Alignment) align)
    {
        public VguiPanel Panel { get; } = panel;

        public string? Sequence { get; } = sequence;

        public string Variable { get; } = variable;

        public Interpolator Interpolator { get; } = interpolator;

        public float Parameter { get; } = parameter;

        public float StartTime { get; } = startTime;

        public float EndTime { get; } = endTime;

        public (float, float, float, float) EndValue { get; } = endValue;

        public bool CanBeCancelled { get; } = canBeCancelled;

        public (bool Relative, string Panel, VguiAlignment Alignment) Align { get; } = align;

        public bool Started { get; set; }

        public (float, float, float, float) StartValue { get; set; }
    }

    private sealed record PostedMessage(
        CommandType Type,
        string Sequence,
        string Event,
        string Variable,
        string Variable2,
        int Flag,
        float StartTime,
        VguiPanel? Parent,
        bool CanBeCancelled);
}

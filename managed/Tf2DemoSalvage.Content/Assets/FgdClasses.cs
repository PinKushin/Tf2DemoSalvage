using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The entity colours Valve ships in the FGD files, which is Hammer's palette.
/// </summary>
/// <remarks>
/// **Shipped data, not a reimplementation of an editor.** Hammer is not open — `src/utils` in
/// `source-sdk-2013` has vbsp, vrad, vvis and glview and no hammer — but the palette it draws with
/// is a file the game ships and its own tools read: `bin/base.fgd`, `bin/halflife2.fgd` and
/// `bin/tf.fgd`. Reading it is the same category as reading `items_game.txt` or `modevents.res`.
///
/// The owner's reason for wanting Valve's numbers rather than ours: "if our placeholders match
/// valves, and our colors match valves then things become easily compared and you only have one
/// legend to remember."
///
/// **The syntax is regular, which is why this is a parser and not a grammar:**
///
/// <code>
/// @SolidClass base(Targetname) color(0 255 255) = func_areaportal : "description"
/// @PointClass base(Parentname) color(180 10 180) = env_particlelight : "..."
/// </code>
///
/// **Colour is inherited through `base(...)` when a class does not state one**, which most do not —
/// that is how a hundred entities share one colour without repeating it. Resolved lazily and with a
/// depth limit rather than eagerly, because an FGD is hand-edited and a cycle in the base graph is a
/// plausible typo that must not hang a viewer.
///
/// **A class with no colour anywhere returns null rather than a default.** Hammer's own fallback is
/// a property of Hammer, and inventing one here would be exactly the "our colours" this exists to
/// avoid — the caller knows better what an uncoloured entity should look like in its own view.
/// </remarks>
public sealed class FgdClasses
{
    private readonly Dictionary<string, (byte Red, byte Green, byte Blue)?> _colours =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string[]> _bases =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How far a <c>base(...)</c> chain is followed before giving up.</summary>
    /// <remarks>
    /// **A limit rather than a visited set, because the cost of being wrong is asymmetric.** An FGD
    /// is hand-edited text; a cycle in its base graph is a typo somebody will eventually commit, and
    /// the failure it would cause here is a viewer that never finishes loading a map. Eight is far
    /// past the deepest chain Valve ships and cheap to be generous about.
    /// </remarks>
    private const int BaseDepthLimit = 8;

    private FgdClasses()
    {
    }

    /// <summary>How many classes were read.</summary>
    public int Count => _colours.Count;

    /// <summary>Parses one or more FGD files, later ones adding to earlier.</summary>
    /// <param name="files">The FGD text, in the order the game mounts them.</param>
    /// <returns>The classes and their colours.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is null.</exception>
    /// <remarks>
    /// **Later files win, which is what <c>@include</c> means.** `tf.fgd` opens by including
    /// `base.fgd` and then redefines some of what it found; parsing them in mount order and letting
    /// the last definition stand reproduces that without following includes, which would otherwise
    /// need a file resolver for a gain of nothing.
    /// </remarks>
    public static FgdClasses Parse(params string[] files)
    {
        ArgumentNullException.ThrowIfNull(files);

        FgdClasses classes = new();

        foreach (string text in files)
        {
            classes.Read(text ?? string.Empty);
        }

        return classes;
    }

    /// <summary>The colour Valve gives a class, following <c>base(...)</c>, or null.</summary>
    /// <param name="classname">The entity classname, as the BSP spells it.</param>
    /// <returns>The colour, or null when neither the class nor its bases state one.</returns>
    public (byte Red, byte Green, byte Blue)? Colour(string? classname)
    {
        return classname is null ? null : Resolve(classname, BaseDepthLimit);
    }

    private (byte Red, byte Green, byte Blue)? Resolve(string classname, int depth)
    {
        if (depth <= 0)
        {
            return null;
        }

        if (_colours.TryGetValue(classname, out (byte Red, byte Green, byte Blue)? own) &&
            own is { } stated)
        {
            return stated;
        }

        if (!_bases.TryGetValue(classname, out string[]? bases))
        {
            return null;
        }

        foreach (string parent in bases)
        {
            if (Resolve(parent, depth - 1) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    private void Read(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            ReadOnlySpan<char> content = line.AsSpan().TrimStart();

            if (content.Length == 0 || content[0] != '@')
            {
                continue;
            }

            // The classname is what follows the '=' and precedes the ':' or the end of the line.
            int equals = content.IndexOf('=');

            if (equals < 0)
            {
                continue;
            }

            ReadOnlySpan<char> head = content[..equals];
            ReadOnlySpan<char> tail = content[(equals + 1)..];

            int colon = tail.IndexOf(':');

            if (colon >= 0)
            {
                tail = tail[..colon];
            }

            string name = tail.Trim().ToString();

            if (name.Length == 0 || name.Contains(' ', StringComparison.Ordinal))
            {
                continue;
            }

            _colours[name] = ColourIn(head);

            if (BasesIn(head) is { Length: > 0 } bases)
            {
                _bases[name] = bases;
            }
        }
    }

    private static (byte Red, byte Green, byte Blue)? ColourIn(ReadOnlySpan<char> head)
    {
        int at = head.IndexOf("color(", StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return null;
        }

        ReadOnlySpan<char> inside = head[(at + "color(".Length)..];
        int close = inside.IndexOf(')');

        if (close < 0)
        {
            return null;
        }

        Span<byte> channels = stackalloc byte[3];
        int found = 0;

        foreach (Range part in inside[..close].Split(' '))
        {
            ReadOnlySpan<char> number = inside[part].Trim();

            if (number.Length == 0)
            {
                continue;
            }

            if (found == 3 ||
                !int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return null;
            }

            channels[found++] = (byte)Math.Clamp(value, 0, 255);
        }

        return found == 3 ? (channels[0], channels[1], channels[2]) : null;
    }

    private static string[] BasesIn(ReadOnlySpan<char> head)
    {
        int at = head.IndexOf("base(", StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return [];
        }

        ReadOnlySpan<char> inside = head[(at + "base(".Length)..];
        int close = inside.IndexOf(')');

        if (close < 0)
        {
            return [];
        }

        List<string> bases = [];

        foreach (Range part in inside[..close].Split(','))
        {
            string parent = inside[part].Trim().ToString();

            if (parent.Length > 0)
            {
                bases.Add(parent);
            }
        }

        return [.. bases];
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One font as a VGUI scheme declares it.</summary>
/// <remarks>
/// **The fields are Valve's, spelled as the scheme spells them**, so a scheme can be read without a
/// translation table (D79 applied to data rather than to cvars). Only the ones the HUD actually
/// consumes are carried; a scheme also declares <c>scanlines</c>, <c>additive</c>, <c>rotary</c>
/// and <c>custom</c>, which are added when something needs them rather than parsed to be discarded.
///
/// **<c>weight</c> is GDI's, not a boolean.** 0 is <c>FW_DONTCARE</c> and resolves to the regular
/// face; 500 is <c>FW_MEDIUM</c>; 700 is bold. That is why <c>DefaultFixedOutline</c> at weight 0
/// is lighter than <c>DebugFixed</c> beside it at 500.
/// </remarks>
public sealed record SchemeFont
{
    /// <summary>The typeface, as a font family name — <c>Lucida Console</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int Tall { get; init; }

    /// <summary>GDI weight: 0 for the regular face, 700 for bold.</summary>
    public int Weight { get; init; }

    /// <summary>Whether Valve draws a one-pixel border around every glyph.</summary>
    public bool Outline { get; init; }

    /// <summary>Whether the glyph is antialiased.</summary>
    public bool Antialias { get; init; }

    /// <summary>Whether a shadow is drawn one pixel down and right.</summary>
    public bool DropShadow { get; init; }

    /// <summary>Blur radius in pixels, or zero.</summary>
    public int Blur { get; init; }

    /// <summary>Whether the face is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Lowest screen height this candidate covers, or zero for unbounded.</summary>
    /// <remarks>
    /// From <c>"yres" "480 599"</c>. **Inclusive at both ends**, and zero means the candidate has
    /// no range and therefore matches any screen — which is why real schemes list the unbounded
    /// candidate last.
    /// </remarks>
    public int LowestScreenHeight { get; init; }

    /// <summary>Highest screen height this candidate covers, or zero for unbounded.</summary>
    public int HighestScreenHeight { get; init; }

    /// <summary>Whether this candidate covers a screen of the given height.</summary>
    /// <param name="screenHeight">The screen height being drawn to, or zero to ignore ranges.</param>
    /// <returns>Whether the candidate applies.</returns>
    public bool Covers(int screenHeight) =>
        (LowestScreenHeight == 0 && HighestScreenHeight == 0) ||
        screenHeight == 0 ||
        (screenHeight >= LowestScreenHeight && screenHeight <= HighestScreenHeight);
}

/// <summary>Reads font declarations out of a VGUI scheme.</summary>
/// <remarks>
/// **A font is a LIST of candidates, and Valve says why in a comment above them:**
///
/// <code>
/// // fonts are used in order that they are listed
/// // fonts listed later in the order will only be used if they fulfill a range not already filled
/// // if a font fails to load then the subsequent fonts will replace
/// </code>
///
/// So each numbered entry may be bounded to a range of screen heights by <c>yres</c>, and the first
/// whose range covers the current resolution wins. That is how one scheme ships a small font for a
/// 480-line display and a larger one for 1200.
///
/// **The third line — replacement when a font fails to load — is not implemented**, and saying so
/// is better than implying it works. It needs the rasteriser to report that a family is absent, and
/// nothing asks that question yet.
/// </remarks>
public static class SchemeFonts
{
    /// <summary>Depth of a font's name inside a scheme: Scheme, Fonts, then the name.</summary>
    private const int FontNameDepth = 2;

    /// <summary>Depth of a numbered candidate under a font name.</summary>
    private const int CandidateDepth = 3;

    /// <summary>Depth of a candidate's fields.</summary>
    private const int FieldDepth = 4;

    /// <summary>Finds a font by the name the scheme gives it.</summary>
    /// <param name="scheme">The scheme file's bytes.</param>
    /// <param name="name">The font's name, such as <c>DefaultFixedOutline</c>.</param>
    /// <param name="screenHeight">Screen height to satisfy, or zero to take the first candidate.</param>
    /// <returns>The winning candidate, or null when the scheme does not declare the font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <remarks>
    /// **Null rather than a fabricated default when the font is absent.** A HUD asking for a font
    /// nobody declared has a bug in it, and quietly handing back some other face would hide it —
    /// `docs/memory/sentinels-conflate-unknown-with-answer.md`.
    /// </remarks>
    public static SchemeFont? Find(ReadOnlySpan<byte> scheme, string name, int screenHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(name);

        List<SchemeFont> candidates = [];

        // Fields of the candidate being read. Kept flat rather than as an object because the
        // visitor reports one key at a time and a candidate is only complete at its closing brace,
        // which the visitor does not see.
        string? family = null;
        int tall = 0;
        int weight = 0;
        bool outline = false;
        bool antialias = false;
        bool dropShadow = false;
        int blur = 0;
        bool italic = false;
        int lowest = 0;
        int highest = 0;

        bool inFont = false;

        void Finish()
        {
            if (family is null)
            {
                return;
            }

            candidates.Add(new SchemeFont
            {
                Name = family,
                Tall = tall,
                Weight = weight,
                Outline = outline,
                Antialias = antialias,
                DropShadow = dropShadow,
                Blur = blur,
                Italic = italic,
                LowestScreenHeight = lowest,
                HighestScreenHeight = highest,
            });

            family = null;
            tall = 0;
            weight = 0;
            outline = false;
            antialias = false;
            dropShadow = false;
            blur = 0;
            italic = false;
            lowest = 0;
            highest = 0;
        }

        KeyValuesReader.Read(scheme, (key, value, depth) =>
        {
            // A new font name at the font depth ends whichever font was being read.
            //
            // **Its one distinguishing input is the same font declared twice**, which is what a
            // `#base` override produces — the derived scheme redeclares a font the base already
            // had. Without this, the trailing candidate of the first block stays pending and merges
            // with the first candidate of the second, producing a font that existed in neither.
            //
            // Established by sabotage rather than assumed: removing this line left all five tests
            // green, so the comment that used to sit here — claiming it stopped NEIGHBOURING fonts
            // merging — was describing something the control flow already handles. `inFont` is
            // false throughout a font nobody asked for, so nothing accumulates to merge.
            if (depth == FontNameDepth && value is null)
            {
                Finish();
                inFont = key.Equals(name, StringComparison.OrdinalIgnoreCase);
                return true;
            }

            if (!inFont)
            {
                return true;
            }

            // A numbered candidate opening: whatever came before it is complete.
            if (depth == CandidateDepth && value is null)
            {
                Finish();
                return true;
            }

            if (depth != FieldDepth || value is null)
            {
                return true;
            }

            // Compared rather than lowered: `ToLowerInvariant` is CA1308, and an ordinal
            // case-insensitive compare is the more correct instrument anyway — a scheme's keys are
            // ASCII identifiers, not text in the current culture.
            bool Is(string field) => key.Equals(field, StringComparison.OrdinalIgnoreCase);

            if (Is("name"))
            {
                family = value;
            }
            else if (Is("tall"))
            {
                tall = Number(value);
            }
            else if (Is("weight"))
            {
                weight = Number(value);
            }
            else if (Is("outline"))
            {
                outline = Number(value) != 0;
            }
            else if (Is("antialias"))
            {
                antialias = Number(value) != 0;
            }
            else if (Is("dropshadow"))
            {
                dropShadow = Number(value) != 0;
            }
            else if (Is("blur"))
            {
                blur = Number(value);
            }
            else if (Is("italic"))
            {
                italic = Number(value) != 0;
            }
            else if (Is("yres"))
            {
                (lowest, highest) = Range(value);
            }

            // Anything else is a field this HUD does not consume. Ignored rather than refused,
            // exactly as a scheme written by a later version must be.
            return true;
        });

        Finish();

        foreach (SchemeFont candidate in candidates)
        {
            if (candidate.Covers(screenHeight))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Reads an integer field, treating anything unparseable as zero.</summary>
    private static int Number(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : 0;

    /// <summary>Reads a <c>"low high"</c> pair.</summary>
    private static (int Lowest, int Highest) Range(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2 ? (Number(parts[0]), Number(parts[1])) : (0, 0);
    }
}

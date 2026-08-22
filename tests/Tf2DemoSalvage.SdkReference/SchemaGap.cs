using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>
/// Asks whether a wire property name appears anywhere in the shipped code.
/// </summary>
/// <remarks>
/// **Built for the conformance sweep, to make a gap marker able to fail.** Several suites here
/// document a part of TF2's schema that this project does not read — disguise state, stealth timers,
/// the ammo array, damage bits, the flag's status field. Each asserted Valve's declaration and
/// stopped, so it could not fail for any reason concerning this project, and could not notice when
/// the feature was implemented either.
///
/// A gap marker's job (D45) is to fail when its gap closes. That needs a way to ask "does anything
/// of ours read this yet", and the obvious ways are both bad:
///
/// - **Reflection over types and members** cannot see it. A wire name is a string passed to a
///   lookup, not a symbol — <c>Number("DT_TFPlayerShared.m_nDisguiseClass")</c> declares nothing.
/// - **Grepping the source** is the instrument this whole sweep exists to remove. It matches a
///   mention in a comment, misses a name built by concatenation, and goes stale against a build.
///
/// **So it searches the compiled assembly instead.** A C# string literal is stored in the metadata
/// user-string heap as UTF-16, so the literal <c>"m_nDisguiseClass"</c> is present verbatim in the
/// bytes of any assembly that contains it and absent from one that does not. That is a fact about
/// the BUILD rather than about the source tree, which is what makes it worth using: a comment
/// mentioning the name does not appear, and a name that reaches the build does.
///
/// **Every caller must use the control.** <see cref="AnyProductionAssemblyMentions"/> returning
/// false is only evidence if the same search finds something known to be present — otherwise a
/// wrong path, a stale binary or an encoding mistake all read as "not implemented"
/// (<c>docs/memory/an-empty-search-needs-a-control.md</c>).
/// </remarks>
public static class SchemaGap
{
    /// <summary>A wire name this project demonstrably does read, for use as a control.</summary>
    /// <remarks>
    /// <c>EntityState.Fog</c> looks this up, and <c>FogControllerConformanceTests</c> checks it
    /// against Valve's own <c>SENDINFO_STRUCTELEM</c>. If the search cannot find this, it cannot
    /// find anything.
    /// </remarks>
    public const string KnownPresent = "m_fog.start";

    /// <summary>Does any shipped assembly contain this string as a literal?</summary>
    /// <param name="name">A wire property or table name, exactly as it travels.</param>
    public static bool AnyProductionAssemblyMentions(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // **Both encodings, because a name can arrive as either kind of metadata.** A string
        // LITERAL lives in the user-string heap as UTF-16 — that is a wire name passed to a lookup.
        // A type, member or enum name lives in the string heap as UTF-8 — that is what appears when
        // somebody implements the feature properly, as an enum of flags rather than a magic string.
        //
        // Searching only UTF-16 was the first version and it would have missed exactly the good
        // outcome: a `DamageBits` enum with named members closes the gap and leaves no literal
        // behind, so the marker would have gone on skipping for ever.
        byte[] utf16 = Encoding.Unicode.GetBytes(name);
        byte[] utf8 = Encoding.UTF8.GetBytes(name);

        foreach (string path in ProductionAssemblies())
        {
            byte[] image = File.ReadAllBytes(path);

            if (Contains(image, utf16) || Contains(image, utf8))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The shipped assemblies sitting beside the test binary.</summary>
    /// <remarks>
    /// **Found on disk rather than by referencing a type**, so this lives in the reference project
    /// and every test assembly can use it — Core.Tests, Content.Tests and Viewer3D.Tests all need
    /// to ask the same question, and only one of them can name a type from each production project.
    ///
    /// The build copies every referenced assembly next to the tests, so the search sees whichever
    /// of them that test project pulls in. A test asking about a name in a project it does not
    /// reference gets a false "absent" — which is why the positive control is not optional, and why
    /// callers should assert it in the same test rather than once somewhere else.
    /// </remarks>
    private static HashSet<string> ProductionAssemblies()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string beside = AppContext.BaseDirectory;

        foreach (string path in Directory.EnumerateFiles(beside, "Tf2DemoSalvage.*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(path);

            // Test assemblies and the reference helper are not production code, and a wire name
            // mentioned in a TEST must not count as the feature being implemented.
            if (name.EndsWith(".Tests", StringComparison.Ordinal) ||
                name.Equals("Tf2DemoSalvage.SdkReference", StringComparison.Ordinal))
            {
                continue;
            }

            paths.Add(path);
        }

        return paths;
    }

    /// <summary>Plain byte search — the literal is UTF-16 and unaligned in the heap.</summary>
    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty || haystack.Length < needle.Length)
        {
            return false;
        }

        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (haystack.Slice(at, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}

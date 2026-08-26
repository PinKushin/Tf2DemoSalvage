using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Every field this project reads is assigned something other than null somewhere.
/// </summary>
/// <remarks>
/// **This is the instrument B193 asked for, and it found two shipped regressions the hour it was
/// written** (B196). Both wore the same shape, and it is the shape every extraction from
/// <c>MainForm</c> can produce:
///
/// <list type="bullet">
/// <item><c>_level</c> survived the move that created <c>LoadedMap</c>; its assignment did not. It
/// was still declared, still cleared to null, still READ by <c>mat_leafvis</c> — and never given a
/// value, so the overlay drew nothing on every map.</item>
/// <item><c>_shotPath</c> survived the move that created <c>LaunchOptions</c> and was never seeded
/// from it, so <c>--shot</c> did nothing at all.</item>
/// </list>
///
/// **Neither is visible to the compiler.** `_level = null` IS an assignment, so CS0649 stays quiet;
/// the field is read, so the unused-member analyzers stay quiet; and `_level?.Leaves` on a
/// permanently null field is a legal expression with a legal answer. The viewer suite reported
/// 620 green across both.
///
/// **Source text rather than reflection, and that is the only way this works.** "Assigned only
/// null" is a question about instructions, not about state — at runtime the field simply holds
/// null, which is indistinguishable from a field that is legitimately empty right now. IL analysis
/// would answer it properly and is far more machinery than a `private` field declaration needs.
///
/// **What it deliberately does NOT flag:** a field nobody reads. That is a different defect and the
/// analyzers already catch it. Flagging it here would mean two instruments arguing over one finding.
/// </remarks>
public sealed partial class FieldSeedingTests
{
    /// <summary>The viewer's own source, which is what this reads.</summary>
    private static string ProjectPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "managed", "Tf2DemoSalvage.Viewer3D"));

    [Test]
    public void Fields_ThatAreReadInTheViewer_AreAssignedSomethingOtherThanNull()
    {
        List<string> unseeded = [];

        foreach (string file in Directory.EnumerateFiles(ProjectPath, "*.cs", SearchOption.AllDirectories))
        {
            // **Comments are stripped ONCE, before any predicate sees the text, and getting this
            // wrong is what made the first version blind to the bug it was written for.** This file
            // records every field it has ever removed in a note naming it — "`_level = null`
            // alongside `_assets = null`" — and that note is not an assignment. Worse, it does not
            // even read as one: the backtick after `null` defeats a `(?!null\s*;)` guard, so the
            // comment counted as SEEDING the field and `_level` went unreported while `_shotPath`,
            // which happens to have no such note, was caught.
            string code = CommentPattern().Replace(File.ReadAllText(file), string.Empty);

            foreach (string field in Declared(code))
            {
                if (!Seeded(code, field) && Read(code, field))
                {
                    unseeded.Add($"{Path.GetFileName(file)}: {field}");
                }
            }
        }

        // Named rather than counted, because "one field is wrong" is not actionable and the whole
        // value of this test is that it says WHICH — the two it was written for were invisible by
        // every other means available.
        unseeded.ShouldBeEmpty(
            "a field that is read but only ever assigned null is a dropped wiring, not a state: " +
            string.Join("; ", unseeded));
    }

    [Test]
    public void Fields_ThatAreOnlyEverAssignedNull_AreDetectedByThisTest()
    {
        // **The control, and this test is worthless without it.** The scan above passes on a clean
        // repository, which is exactly what it would do if the regex matched no declarations at
        // all — the "wrong instrument" failure, where an empty search reads as a clean result.
        //
        // So the same three predicates are run against a hand-built source that CONTAINS the
        // defect, and against one that does not. If the detector cannot see the bug it was written
        // for, the run above proves nothing.
        const string Broken = """
                private MapLevel? _level;
                private void Clear() { _level = null; }
                private object? Read() => _level?.Leaves;
            """;

        const string Fixed = """
                private MapLevel? _level;
                private void Clear() { _level = null; }
                private void Load(MapLevel map) { _level = map; }
                private object? Read() => _level?.Leaves;
            """;

        Declared(Broken).ShouldBe(["_level"], "the declaration regex must find the field at all");

        Seeded(Broken, "_level").ShouldBeFalse();
        Read(Broken, "_level").ShouldBeTrue();

        // The bystander: one added assignment, and the same field must stop being reported.
        Seeded(Fixed, "_level").ShouldBeTrue();
    }

    [Test]
    public void Fields_AssignedByCompoundOperators_AreNotReportedAsUnseeded()
    {
        // **Three real false positives, all present in `MainForm` today.** A first version of this
        // looked only for `_name =` and flagged `_downloader` (assigned with `??=`),
        // `_loadsRequested` (only ever `++`) and every field with an initializer on its own
        // declaration. A test that cries wolf on working code gets deleted, so each form is
        // covered here rather than discovered later.
        const string Compound = """
                private MapDownloader? _downloader;
                private int _loadsRequested;
                private IReadOnlyList<string> _shown = [];
                private void Use()
                {
                    _downloader ??= MapDownloader.Create();
                    _loadsRequested++;
                }
                private object? Read() => (_downloader, _loadsRequested, _shown);
            """;

        Seeded(Compound, "_downloader").ShouldBeTrue("??= is an assignment");
        Seeded(Compound, "_loadsRequested").ShouldBeTrue("++ is an assignment");
        Seeded(Compound, "_shown").ShouldBeTrue("an initializer on the declaration is an assignment");
    }

    [Test]
    public void Fields_MentionedOnlyInAComment_AreNotCountedAsSeeded()
    {
        // **The input that made the first version of this test blind to `_level`, verbatim in
        // shape.** This project records every field it removes in a note that names it, and one of
        // those notes contains the text `_level = null` inside backticks. That is not an
        // assignment — but it is not `= null;` either, because a backtick follows the word, so a
        // guard written as "an `=` not followed by null-semicolon" reads it as a real assignment
        // and marks the field seeded.
        //
        // A comment cannot assign anything, so the fix is to delete comments before asking. The
        // pairing below is the point: the same source, once with the note and once without, must
        // give the same answer.
        const string WithNote = """
                // The old catch set `_level = null` alongside `_assets = null`, throwing away lumps.
                private MapLevel? _level;
                private void Clear() { _level = null; }
                private object? Read() => _level?.Leaves;
            """;

        string stripped = CommentPattern().Replace(WithNote, string.Empty);

        Seeded(WithNote, "_level").ShouldBeTrue(
            "the raw text really does contain something that looks like an assignment — " +
            "this is the trap, not the behaviour");

        Seeded(stripped, "_level").ShouldBeFalse("a comment cannot assign anything");
        Read(stripped, "_level").ShouldBeTrue("the field is still genuinely read");
    }

    /// <summary>Every private field a source file declares.</summary>
    private static IReadOnlyList<string> Declared(string source) =>
        [.. DeclarationPattern().Matches(source).Select(match => match.Groups["name"].Value).Distinct()];

    /// <summary>Whether anything ever puts a non-null value into a field.</summary>
    /// <remarks>
    /// A declaration initialiser counts, `??=` counts, and any compound operator counts. Plain
    /// <c>= null</c> does not, which is the entire point.
    ///
    /// **The whitespace after `=` is matched ATOMICALLY, and the first version was wrong because it
    /// was not.** `\s*` followed by `(?!null\s*;)` looks like "skip spaces, then refuse null" and is
    /// not: when the lookahead fails, the engine backtracks the `\s*` to consume nothing, the
    /// lookahead then sees a SPACE rather than `null`, and succeeds. So `_level = null;` reported
    /// itself as seeded and the whole scan passed vacuously — which is why the control test below
    /// exists, and it is what caught this.
    /// </remarks>
    private static bool Seeded(string source, string field) =>
        new Regex(
            $@"(?<![\w.]){Regex.Escape(field)}\s*(\?\?=|\+\+|--|[-+*/|&^]=|(?<![=!<>])=(?!=)(?>\s*)(?!null\s*;))"
            + $@"|\+\+\s*{Regex.Escape(field)}|--\s*{Regex.Escape(field)}",
            RegexOptions.None,
            TimeSpan.FromSeconds(2))
            .IsMatch(source);

    /// <summary>Whether anything ever reads a field, as opposed to declaring or clearing it.</summary>
    /// <remarks>
    /// A read is any mention that is not the declaration and not the left-hand side of an
    /// assignment. The caller strips comments first, or a field deleted years ago and explained in
    /// a note would read as live.
    /// </remarks>
    private static bool Read(string code, string field) =>
        new Regex(
            $@"(?<![\w.]){Regex.Escape(field)}\s*(?![\s]*(=(?!=)|\?\?=))",
            RegexOptions.None,
            TimeSpan.FromSeconds(2))
            .IsMatch(DeclarationPattern().Replace(code, string.Empty));

    /// <summary>A private field declaration, capturing its name.</summary>
    /// <remarks>
    /// Deliberately narrow: `private`, optionally `readonly`/`static`/`const`, a type, then a name
    /// starting with an underscore. Anything broader starts matching locals and parameters, and a
    /// detector with false positives is one nobody runs.
    /// </remarks>
    [GeneratedRegex(
        @"^[ \t]*private[ \t]+(?:static[ \t]+)?(?:readonly[ \t]+)?(?:const[ \t]+)?[\w<>,.\?\[\] ]+?[ \t]+(?<name>_\w+)[ \t]*(?=[;=])",
        RegexOptions.Multiline)]
    private static partial Regex DeclarationPattern();

    /// <summary>A line or block comment.</summary>
    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();
}

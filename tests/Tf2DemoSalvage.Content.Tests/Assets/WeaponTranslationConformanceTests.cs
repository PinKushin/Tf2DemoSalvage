using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The whole per-class weapon translation table, against <c>pszWpnEntTranslationList</c>.
/// </summary>
/// <remarks>
/// **Written because the mutation report said the sampling tests could not be enough.**
/// `WeaponScriptName` had 112 surviving mutants after ten hand-written cases, and the reason is
/// structural: the file is mostly a TABLE — eight weapons by ten classes — so a mutant that rewrites
/// one entry survives unless a test happens to exercise that exact cell. Ten cases cover perhaps
/// fifteen of ninety.
///
/// **Restating the table in the test would be a change detector, so this reads Valve's instead.**
/// `tf_shareddefs.cpp` declares `pszWpnEntTranslationList` verbatim — the entity name, then one
/// entry per class in `TF_CLASS_*` order:
///
/// <code>
/// { "tf_weapon_shotgun",
///   { "", "", "", "tf_weapon_shotgun_soldier", "", "",
///     "tf_weapon_shotgun_hwg", "tf_weapon_shotgun_pyro", "", "tf_weapon_shotgun_primary" } },
/// </code>
///
/// So every cell is checked against the source it was transcribed from, which kills the table
/// mutants legitimately AND catches the table going stale — the failure
/// <c>WeaponScriptNameTests</c> exists to prevent for the class-name mapping, applied to the other
/// half of the file.
///
/// **Why the table matters at all:** a wrong translation resolves to no script, the weapon gets no
/// role, and the animation keeps the primary suffix it would have had anyway. Correct and broken
/// look identical for most weapons, which is why none of this can be left to inspection.
/// </remarks>
public sealed class WeaponTranslationConformanceTests
{
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryCellOfTheTranslationTableMatchesTheSdk()
    {
        Dictionary<string, string[]> sdk = TranslationList();

        // The control: a regex that matched nothing would make every assertion below vacuous, and
        // an empty table is exactly what a moved or renamed declaration produces.
        sdk.Count.ShouldBeGreaterThan(5, "the SDK declares several weapons with per-class scripts");

        int checkedCells = 0;

        foreach ((string weapon, string[] byClass) in sdk)
        {
            for (int playerClass = 0; playerClass < byClass.Length; playerClass++)
            {
                // An empty entry means "no translation for this class", and Translate answers with
                // the base name — so the expectation is the base name, not the empty string. That
                // distinction is the one this project got wrong once already.
                string expected = byClass[playerClass].Length > 0 ? byClass[playerClass] : weapon;

                WeaponScriptName.Translate(weapon, playerClass).ShouldBe(
                    expected,
                    $"{weapon} in class {playerClass}");

                checkedCells++;
            }
        }

        // Says how much was actually compared, so a table that shrinks to one row cannot pass
        // quietly.
        checkedCells.ShouldBeGreaterThan(
            50, "eight weapons across ten classes is eighty cells");

        TestContext.Out.WriteLine($"{checkedCells} translation cells checked against the SDK");
    }

    [Test]
    public void EveryWeaponTheSdkTranslatesIsOneThisProjectKnows()
    {
        // The other direction: a weapon Valve translates and this project has never heard of would
        // silently keep its base script in every class. Checked separately from the cell sweep
        // because that one iterates OUR answers for THEIR keys and so cannot notice a missing row.
        Dictionary<string, string[]> sdk = TranslationList();

        foreach ((string weapon, string[] byClass) in sdk)
        {
            bool translatesSomewhere = Array.Exists(byClass, entry => entry.Length > 0);

            if (!translatesSomewhere)
            {
                continue;
            }

            bool known = false;

            for (int playerClass = 0; playerClass < byClass.Length && !known; playerClass++)
            {
                known = WeaponScriptName.Translate(weapon, playerClass) != weapon;
            }

            known.ShouldBeTrue(
                $"{weapon} is translated by the SDK for at least one class and by this project for none");
        }
    }

    /// <summary>Valve's own table, parsed from its declaration.</summary>
    /// <remarks>
    /// Each entry is a braced pair: the entity name, then a braced list of ten class strings in
    /// <c>TF_CLASS_*</c> order with a trailing comment per line. The comments are why the strings
    /// are extracted by their quotes rather than by splitting on commas.
    /// </remarks>
    private static Dictionary<string, string[]> TranslationList()
    {
        string text = SourceSdk.Text(SharedDefs).ShouldNotBeNull();

        int start = text.IndexOf("pszWpnEntTranslationList[]", StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1, "the declaration was not found");

        // The array ends at the first line that closes it at column zero.
        int end = text.IndexOf("\n};", start, StringComparison.Ordinal);
        string body = text[start..(end < 0 ? text.Length : end)];

        Dictionary<string, string[]> table = new(StringComparer.Ordinal);

        // Each record is `{ "name", { ...ten strings... } }`. Matched as a name followed by a brace
        // group so a record with a different arity fails to match rather than silently shifting.
        foreach (Match record in Regex.Matches(
            body,
            @"\{\s*""(?<weapon>[^""]+)""\s*,\s*\{(?<entries>[^}]*)\}",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            List<string> entries = [];

            foreach (Match entry in Regex.Matches(
                record.Groups["entries"].Value,
                @"""(?<value>[^""]*)""",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            {
                entries.Add(entry.Groups["value"].Value);
            }

            if (entries.Count > 0)
            {
                table[record.Groups["weapon"].Value] = [.. entries];
            }
        }

        return table;
    }
}

using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// That the SDK sweep's cache cannot be poisoned by the caller it answers.
/// </summary>
/// <remarks>
/// **`SourceSdk.Names` caches its sweep, and it used to hand the cached set out directly.** A caller
/// that then mutated the result — `SendPropConformanceTests` folds in a second sweep with
/// <c>UnionWith</c> — wrote its additions into the entry cached for the FIRST pattern. Every later
/// caller asking for that pattern got names it never asked for, which is exactly the shared answer
/// the cache's own remarks say must not happen: "two callers sweeping the same directory for
/// different things must not share an answer. That would be a wrong result rather than a slow one".
///
/// **It surfaced as flake before it surfaced as a wrong answer.** NUnit runs these in parallel, so
/// one test's <c>UnionWith</c> overlapped another's <c>Contains</c> on the same unsynchronised
/// <c>HashSet</c>, and `SendProps_Moveparent_IsARealSendPropUnderAnAlias` failed with
/// <c>moveparent</c> visible in its own list of actual values. It passed alone and passed on a
/// re-run — the shape this project treats as a defect in the code rather than as noise.
///
/// The race cannot be reproduced on demand. The pollution can, and it is the more dangerous half:
/// a race announces itself, while a quietly widened denominator makes a conformance suite stop
/// noticing things.
/// </remarks>
public sealed class SourceSdkCacheTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Names_WhenACallerMutatesTheResult_TheNextCallIsUnaffected()
    {
        // A narrow sweep, so this costs a directory of headers rather than the whole of src/game.
        Regex pattern = new(
            @"SendPropEHandle\(\s*SENDINFO\(\s*([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled,
            System.TimeSpan.FromSeconds(10));

        HashSet<string> first = SourceSdk.Names("src/game/shared", "*.cpp", pattern, recursive: true);

        // The control: the sweep found something, or every assertion below is vacuous. Five absence
        // claims in this project have been facts about the search rather than about the data.
        first.ShouldNotBeEmpty("the sweep matched nothing, so nothing here was measured");

        const string Invented = "a_name_no_sdk_file_contains";

        first.Add(Invented);

        HashSet<string> second = SourceSdk.Names("src/game/shared", "*.cpp", pattern, recursive: true);

        second.ShouldNotContain(
            Invented,
            "the cached sweep was handed out by reference, so one caller's mutation reached another");
    }

    [Test]
    public void Names_TwoCallsForTheSameSweep_AreSeparateInstances()
    {
        // Stated directly as well, because the assertion above would also pass if the cache simply
        // re-swept every time — which is a different thing from being safe to mutate, and would be
        // slow rather than correct.
        Regex pattern = new(
            @"SendPropInt\(\s*SENDINFO\(\s*([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled,
            System.TimeSpan.FromSeconds(10));

        HashSet<string> first = SourceSdk.Names("src/game/shared", "*.cpp", pattern, recursive: true);
        HashSet<string> second = SourceSdk.Names("src/game/shared", "*.cpp", pattern, recursive: true);

        first.ShouldNotBeEmpty();
        first.ShouldNotBeSameAs(second);
        second.ShouldBe(first, ignoreOrder: true);
    }
}

namespace Tf2DemoSalvage.SdkReference;

/// <summary>NUnit category names the build's own tooling filters on.</summary>
public static class TestCategories
{
    /// <summary>A test a mutation run leaves out: <c>"test-case-filter": "TestCategory!=MutationHeavy"</c> in each
    /// <c>stryker-config.json</c>.</summary>
    /// <remarks>
    /// **Stryker reruns every test that covers a mutant, once per mutant**, so one test that takes seconds and covers
    /// a whole reader multiplies by thousands. Measured 2026-10-11: Core finished 6,909 mutants in 54 minutes on
    /// 2026-09-29 and could not finish 7,031 in 90 the next day, after one 3.3-second test covering most of
    /// <c>DemoTimeline</c> was added; Content's suite is 502 seconds summed, 439 of them in 95 real-map and real-model
    /// tests, and its first full run was stopped after seven and a half hours. The ordinary test runs and the gate do
    /// not filter on this — only Stryker does. A mutant only these tests would have killed is reported as surviving,
    /// which is the trade: tag a test here when it costs more than about a second, and write the cheap synthetic test
    /// that kills the same mutant (D38). How the list was produced: <c>tests/STRYKER-NOTES.md</c>.
    /// </remarks>
    public const string MutationHeavy = "MutationHeavy";
}

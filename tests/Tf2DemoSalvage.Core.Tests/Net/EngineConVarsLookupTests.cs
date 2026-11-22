using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>The two lookups: a name off the wire, exact; a name a person typed, in any case (B791 review 8).</summary>
public sealed class EngineConVarsLookupTests
{
    /// <remarks>
    /// **The console finds a typed name in any case** (*interpolated*: `CCvar::FindCommandBase` is in the closed `vstdlib`),
    /// and answers with the ConVar's own spelling; a name off the wire is the engine's spelling and is looked up exactly.
    /// </remarks>
    [TestCase("mat_wireframe")]
    [TestCase("MAT_WIREFRAME")]
    [TestCase("Mat_Wireframe")]
    public void TryByConsoleName_AnyCase_FindsTheConVarUnderItsOwnName(string typed)
    {
        EngineConVars.TryByConsoleName(typed, out EngineConVar? declared).ShouldBeTrue();
        declared.Name.ShouldBe("mat_wireframe");
    }

    [Test]
    public void TryByName_AnotherCase_FindsNothing()
    {
        EngineConVars.TryByName("mat_wireframe", out _).ShouldBeTrue("the control: the engine's own spelling");
        EngineConVars.TryByName("MAT_WIREFRAME", out _).ShouldBeFalse();
    }
}

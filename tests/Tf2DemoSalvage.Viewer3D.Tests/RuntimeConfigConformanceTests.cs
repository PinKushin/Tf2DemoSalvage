using System.IO;
using System.Text.Json;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>The viewer runs with the runtime's default tiering: quick JIT on (finding 84).</summary>
/// <remarks>
/// **Turning quick JIT off was measured, and it lost** (finding 84, three runs per side of the published package on f12):
/// it removed the tier-0 warm-up of the first seconds of playback, but loading took 46 s longer (decode 216 s against
/// 169 s) and steady playback ran about 17% slower (53 against 64 fps). This pins the default so the trade is not
/// re-made without new numbers.
/// </remarks>
[TestFixture]
public sealed class RuntimeConfigConformanceTests
{
    /// <remarks>Tiering, quick JIT and dynamic PGO each absent from the viewer's runtimeconfig, or at their default, true.</remarks>
    [TestCase("System.Runtime.TieredCompilation.QuickJit")]
    [TestCase("System.Runtime.TieredCompilation")]
    [TestCase("System.Runtime.TieredPGO")]
    public void RuntimeConfig_ForTheViewer_LeavesTieringAtItsDefault(string setting)
    {
        // The viewer's own file, copied beside the tests by the project reference; reading it is the proof it arrived.
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "tf2demoview.runtimeconfig.json");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement properties = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        bool on = !properties.TryGetProperty(setting, out JsonElement set) || set.GetBoolean();

        on.ShouldBeTrue();
    }

    /// <remarks>ReadyToRun was measured too and made the first seconds of playback worse (finding 84): no build file sets it.</remarks>
    [Test]
    public void BuildFiles_ForTheViewer_DoNotPublishReadyToRun()
    {
        DirectoryInfo? root = new(TestContext.CurrentContext.TestDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Tf2DemoSalvage.slnx")))
        {
            root = root.Parent;
        }

        root.ShouldNotBeNull("the repository root, found by its solution file");

        foreach (string file in new[] { "Directory.Build.props", Path.Combine("managed", "Tf2DemoSalvage.Viewer3D", "Tf2DemoSalvage.Viewer3D.csproj") })
        {
            File.ReadAllText(Path.Combine(root.FullName, file)).ShouldNotContain("PublishReadyToRun", Case.Insensitive, file);
        }
    }
}

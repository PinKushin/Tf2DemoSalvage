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
    [Test]
    public void RuntimeConfig_ForTheViewer_LeavesQuickJitOn()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "tf2demoview.runtimeconfig.json");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement properties = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        bool quickJit = !properties.TryGetProperty("System.Runtime.TieredCompilation.QuickJit", out JsonElement set) || set.GetBoolean();

        quickJit.ShouldBeTrue();
    }
}

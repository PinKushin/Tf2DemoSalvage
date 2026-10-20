using System.IO;
using System.Text.Json;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The viewer's code runs optimized from its first call, as the engine's native code does: no tier-0 warm-up for
/// playback to pay (finding 84).
/// </summary>
/// <remarks>
/// **Measured, not assumed**: with quick JIT on, the render thread ran unoptimized tier-0 code for the first five to
/// eight seconds of every playback while the `.NET Tiered Compilation Worker` thread was busy, and every phase,
/// the HUD included, ran two to three times slower until it went quiet (finding 84).
/// </remarks>
[TestFixture]
public sealed class RuntimeConfigConformanceTests
{
    [Test]
    public void RuntimeConfig_ForTheViewer_TurnsQuickJitOff()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "tf2demoview.runtimeconfig.json");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement properties = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        properties.GetProperty("System.Runtime.TieredCompilation.QuickJit").GetBoolean().ShouldBeFalse();
    }
}

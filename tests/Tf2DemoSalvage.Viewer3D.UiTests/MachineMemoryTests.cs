using System;

using Tf2DemoSalvage.Viewer3D.UiTests;

// Not in the `UiTests` namespace: its `ViewerSession` fixture launches a viewer for every test beneath it.
namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>The machine-memory reading the UI suite logs before each test (B558). No viewer is launched.</summary>
public sealed class MachineMemoryTests
{
    [Test]
    public void Read_OnThisMachine_ReportsPhysicalMemoryAndACommitLimit()
    {
        MachineMemory.Reading reading = MachineMemory.Read();

        // The control: a machine running this test has memory. Zeros would mean the call failed and was not noticed.
        reading.TotalPhysicalBytes.ShouldBeGreaterThan(0UL);
        reading.AvailablePhysicalBytes.ShouldBeGreaterThan(0UL);
        reading.AvailablePhysicalBytes.ShouldBeLessThanOrEqualTo(reading.TotalPhysicalBytes);
        reading.TotalCommitBytes.ShouldBeGreaterThanOrEqualTo(reading.TotalPhysicalBytes);
    }

    [Test]
    public void Describe_AReading_NamesFreePhysicalAndFreeCommitInMegabytes()
    {
        MachineMemory.Reading reading = new(
            TotalPhysicalBytes: 16UL * 1024 * 1024 * 1024,
            AvailablePhysicalBytes: 6UL * 1024 * 1024 * 1024,
            TotalCommitBytes: 18UL * 1024 * 1024 * 1024,
            AvailableCommitBytes: 3UL * 1024 * 1024 * 1024,
            LoadPercent: 62);

        MachineMemory.Describe(reading).ShouldBe(
            "physical 6144 of 16384 MB free, commit 3072 of 18432 MB free, memory load 62%");
    }
}

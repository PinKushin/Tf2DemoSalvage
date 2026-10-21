using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>What a translation costs, beside what it answers (<see cref="PlayerActivityTableConformanceTests"/>).</summary>
public sealed class PlayerActivityTableTests
{
    [Test]
    public void Translate_WithNoPlayerTable_AllocatesNothing()
    {
        // Finding 89: every corpse and player asks with no table of its own, every tick, and an empty
        // dictionary built for each answer was 185 MB per 20 s on the corpse worker.
        PlayerActivityTable.Translate("ACT_MP_RUN", "PRIMARY", PlayerActivityOverride.None, null);

        long before = GC.GetAllocatedBytesForCurrentThread();
        string translated = PlayerActivityTable.Translate("ACT_MP_RUN", "PRIMARY", PlayerActivityOverride.None, null);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        translated.ShouldBe("ACT_MP_RUN_PRIMARY");
    }
}

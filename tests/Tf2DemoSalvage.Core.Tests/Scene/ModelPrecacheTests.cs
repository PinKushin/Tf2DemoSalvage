using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Turning an entity's <c>m_nModelIndex</c> into a model path.
/// </summary>
/// <remarks>
/// **This is how Valve's client does it, and the entity lump plays no part.** A health pack, a
/// dropped weapon and a door are all networked entities; the client reads
/// <c>m_nModelIndex</c> — <c>RecvPropInt(RECVINFO(m_nModelIndex), 0,
/// RecvProxy_IntToModelIndex16_BackCompatible)</c> in <c>c_baseentity.cpp</c> — and looks the
/// number up in the <c>modelprecache</c> string table. Reading the map's entity lump instead would
/// place them where the map put them, which is only where they are before anyone picks one up.
/// </remarks>
public sealed class ModelPrecacheTests
{
    [Test]
    public void Path_ForAnIndex_IsTheEntryAtThatIndex()
    {
        ModelPrecache precache = new();

        precache.Apply(
        [
            Entry(0, ""),
            Entry(1, "models/items/medkit_small.mdl"),
            Entry(2, "models/items/ammopack_large.mdl"),
        ]);

        precache.Path(1).ShouldBe("models/items/medkit_small.mdl");
        precache.Path(2).ShouldBe("models/items/ammopack_large.mdl");
    }

    [Test]
    public void Path_ForZero_IsNothing()
    {
        // Index zero is the engine's "no model", and the table's first entry is an empty string
        // that exists to occupy it. A viewer that drew entry zero would draw every entity that has
        // no model at all.
        ModelPrecache precache = new();

        precache.Apply([Entry(0, ""), Entry(1, "models/props/crate.mdl")]);

        precache.Path(0).ShouldBeNull();
    }

    [Test]
    public void Path_ForAnIndexTheTableNeverCarried_IsNothing()
    {
        ModelPrecache precache = new();

        precache.Apply([Entry(0, ""), Entry(1, "models/props/crate.mdl")]);

        precache.Path(7).ShouldBeNull();
    }

    [Test]
    public void Apply_AnUpdateAtAnIndex_ReplacesThatEntryOnly()
    {
        // Updates arrive with their own indices and the rest of the table is untouched. Rebuilding
        // from an update alone would leave a table holding one model.
        ModelPrecache precache = new();

        precache.Apply(
            [Entry(0, ""), Entry(1, "models/props/crate.mdl"), Entry(2, "models/props/barrel.mdl")]);
        precache.Apply([Entry(2, "models/props/pallet.mdl")]);

        precache.Path(1).ShouldBe("models/props/crate.mdl", "untouched by the update");
        precache.Path(2).ShouldBe("models/props/pallet.mdl");
    }

    [Test]
    public void ANegativeIndex_IsADynamicModelAndHasNoEntry()
    {
        // Negative indices are models the client precached itself, which a recording of someone
        // else's session cannot resolve. Returning null says so; treating the number as an index
        // would read some unrelated entry and place a wrong model with total confidence.
        ModelPrecache precache = new();

        precache.Apply([Entry(0, ""), Entry(1, "models/props/crate.mdl")]);

        precache.Path(-1).ShouldBeNull();
    }

    [Test]
    public void OnProtocol20AndBelow_AnIndexBelowMinusOne_IsUnpacked()
    {
        // **Valve's own back-compatibility, transcribed from RecvProxy_IntToModelIndex16_Back-
        // Compatible in recvproxy.cpp:** on protocol 20 and earlier the engine wrote these packed,
        // and the client expands them with modelIndex = -2 - ((-2 - modelIndex) << 1).
        //
        // -3 unpacks to -4: -2 - ((-2 - -3) << 1) = -2 - 2 = -4.
        ModelPrecache.Unpack(-3, protocol: 20).ShouldBe(-4);
    }

    [Test]
    public void OnProtocol20AndBelow_MinusOne_IsLeftAlone()
    {
        // The condition is "< -1", so -1 itself is not packed. A reader that unpacked it too would
        // turn "no model" into a plausible negative index.
        ModelPrecache.Unpack(-1, protocol: 20).ShouldBe(-1);
    }

    [Test]
    public void AboveProtocol20_ANegativeIndex_IsLeftAlone()
    {
        // The control for the pair above: the same input on a later protocol must come back
        // unchanged, or the unpacking is being applied everywhere and the protocol test is dead.
        ModelPrecache.Unpack(-3, protocol: 24).ShouldBe(-3);
    }

    private static StringTableEntry Entry(int index, string text) =>
        new(index, text, Array.Empty<byte>());
}

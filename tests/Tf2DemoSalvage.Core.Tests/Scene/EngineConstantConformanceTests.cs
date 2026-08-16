using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Every engine constant the scene decoder acts on, checked against the header that declares it.
/// </summary>
/// <remarks>
/// **These four decide what is drawn and who it belongs to, and every one fails silently.** A wrong
/// <c>EF_NODRAW</c> bit hides entities that should be visible or shows ones that should not; a wrong
/// <c>EF_BONEMERGE</c> parents a weapon to nothing; a wrong <c>MAX_EDICT_BITS</c> masks a handle to a
/// different slot, which resolves to a real, existing, wrong entity. None of them throws.
///
/// **The values come out of the decoder, not out of this file.** <c>EntityState.EngineConstants</c>
/// exposes what the code uses, keyed by the engine's own names, so the assertion is our value
/// against Valve's rather than one literal against another.
/// </remarks>
public sealed class EngineConstantConformanceTests
{
    /// <summary>Where the engine declares them.</summary>
    private const string Const = "src/public/const.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryConstantWeActOn_HasTheEnginesValue()
    {
        IReadOnlyDictionary<string, int> engine = Declared();

        List<string> wrong = [];

        foreach ((string name, int ours) in EntityState.EngineConstants)
        {
            if (!engine.TryGetValue(name, out int theirs))
            {
                wrong.Add($"{name} is not declared in {Const}");
            }
            else if (theirs != ours)
            {
                wrong.Add($"{name}: we use {ours}, the engine declares {theirs}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void TheInvalidHandleIsBuiltFromBothHalves()
    {
        // **The one that is arithmetic rather than a lookup, and the one that was wrong first.** A
        // test written against −1 for the invalid handle would have passed for the wrong reason;
        // the engine's value is all twenty-one bits set. That is
        // (1 << (MAX_EDICT_BITS + NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS)) − 1, and both halves
        // are read from const.h so a change to either moves this.
        IReadOnlyDictionary<string, int> engine = Declared();

        int bits = engine["MAX_EDICT_BITS"] + engine["NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS"];

        EntityState.NoHandle.ShouldBe((1 << bits) - 1);

        // And it is emphatically not −1, which is what a decoder written from habit would test.
        EntityState.NoHandle.ShouldNotBe(-1);
    }

    [Test]
    public void AnInvalidHandleIsRejectedBeforeItIsMasked()
    {
        // **Order of operations, stated as its own claim.** The invalid handle's low MAX_EDICT_BITS
        // bits are 2047, which is a perfectly legal slot — so a decoder that masks first and tests
        // afterwards resolves "no entity" to entity 2047 on every unset handle in the demo.
        // client/recvproxy.cpp:90 tests the whole value first.
        EntityState.Slot(EntityState.NoHandle).ShouldBeNull();

        int slotBits = Declared()["MAX_EDICT_BITS"];

        (EntityState.NoHandle & ((1 << slotBits) - 1))
            .ShouldBe(2047, "which is why the order matters");
    }

    /// <summary>Every constant the header declares.</summary>
    private static IReadOnlyDictionary<string, int> Declared()
    {
        IReadOnlyDictionary<string, int> values = SourceSdk.Constants(Const);

        // The instrument before its answer.
        values.Count.ShouldBeGreaterThan(20, $"nothing was extracted from {Const}");

        return values;
    }
}

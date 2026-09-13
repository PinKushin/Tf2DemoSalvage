using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a model's <c>.phy</c>: its rigid bodies and the joints between them (B58).
/// </summary>
/// <remarks>
/// **The foundation the ragdoll work needs, and the half of a `.phy` that is not closed.** The file
/// is `phyheader_t` — `int size; int id; int solidCount; int32 checkSum;`, `phyfile.h:14-21` — then
/// `solidCount` hulls in Havok's `IVPS` format, then **plain-text KeyValues** carrying masses,
/// inertias, damping, per-axis joint limits, joint friction, surface properties and the
/// solid-to-BONE mapping. Only the hulls are unreadable.
///
/// **The engine dispatches on the same two block names this reader does:**
///
/// <code>
/// if ( !strcmpi( pBlock, "solid" ) )                  { … ParseSolid( … ); … }
/// else if ( !strcmpi( pBlock, "ragdollconstraint" ) ) { … ParseRagdollConstraint( … ); … }
/// </code>
///
/// `ragdoll_shared.cpp:283-293`. Read-from-source.
///
/// **Asserted against the game's own files rather than a fixture**, because the question is whether
/// this reader can read what Valve ships. A hand-built `.phy` would test that the parser can read
/// its author's idea of one — the exact failure `docs/memory/fixtures-are-the-weak-point.md`
/// records — and the binary hull section, which is the part most likely to throw the text scan off,
/// cannot be synthesised faithfully at all.
/// </remarks>
public sealed class PhysicsModelConformanceTests
{
    private const string Heavy = "models/player/heavy.phy";

    /// <remarks>
    /// **The header's count against the text's, which is this reader's own control.** The header
    /// counts HULLS in the closed binary section and the text counts `solid` blocks; they describe
    /// the same bodies from opposite ends of the file. A text scan that landed at the wrong offset
    /// would produce a plausible list of a different length, and nothing else here would notice.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_FindsAsManySolidsAsItsHeaderDeclares()
    {
        PhysicsModel physics = Physics(Heavy);

        physics.Solids.Count.ShouldBe(physics.DeclaredSolidCount);
        physics.DeclaredSolidCount.ShouldBe(16, "measured with the ragdoll-constraints probe");
    }

    /// <remarks>
    /// **One fewer constraint than solids, on every class model measured** — a joint per bone except
    /// the root, which is what a tree is. Demo and pyro are 15/14, heavy 16/15, scout and sniper
    /// 17/16, engineer 18/17, medic 24/23.
    ///
    /// It is a real prediction rather than a restatement: a reader that lost the last block, or that
    /// counted a `collisionrules` section as a constraint, would break it in one direction or the
    /// other.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_HoldsATreeOfJoints()
    {
        PhysicsModel physics = Physics(Heavy);

        physics.Constraints.Count.ShouldBe(physics.Solids.Count - 1);
    }

    /// <remarks>
    /// **`name` is the load-bearing field and this is the assertion that pins it.** The constraints
    /// refer to solids by INDEX; without a bone name per solid the joint graph is a set of numbers
    /// about an unknown ordering, and every mass and limit read correctly would still be unusable.
    ///
    /// The pelvis is asserted by name because it is the root every Valve biped shares, and because
    /// naming one is a prediction where "they are all non-empty" is a presence check.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_NamesTheBoneEachSolidBelongsTo()
    {
        PhysicsModel physics = Physics(Heavy);

        physics.Solids.ShouldAllBe(solid => solid.Name.Length > 0);

        physics.Solids[0].Name.ShouldBe("bip_pelvis");
        physics.Solids[0].Index.ShouldBe(0);
    }

    /// <remarks>
    /// **A mass in kilograms, and the sum is the check that says the numbers are real.** Valve
    /// distributes a player's mass across the solids, so the total is a person rather than an
    /// arbitrary figure — a reader parsing under the wrong culture, or reading the wrong field,
    /// gives zero or something absurd. The bound is wide because it is a fact about Valve's
    /// authoring rather than a constant to pin.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_CarriesMassesThatSumToABody()
    {
        PhysicsModel physics = Physics(Heavy);

        float total = physics.Solids.Sum(solid => solid.Mass);

        TestContext.Out.WriteLine($"heavy.phy: {physics.Solids.Count} solids, {total:0.0} kg total");

        total.ShouldBeGreaterThan(20f);
        total.ShouldBeLessThan(400f);
    }

    /// <remarks>
    /// **Every joint names two solids that exist**, which is what makes the graph walkable. An
    /// unparsed `parent` or `child` reads as -1 here rather than as a plausible 0 — the reader
    /// defaults them to -1 for exactly that reason, so a missing field cannot masquerade as a joint
    /// hanging off the pelvis.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_JoinsSolidsThatExist()
    {
        PhysicsModel physics = Physics(Heavy);

        foreach (RagdollConstraint joint in physics.Constraints)
        {
            joint.Parent.ShouldBeInRange(0, physics.Solids.Count - 1);
            joint.Child.ShouldBeInRange(0, physics.Solids.Count - 1);
            joint.Parent.ShouldNotBe(joint.Child);
        }
    }

    /// <remarks>
    /// **A limit is a RANGE and the minimum is negative**, which is the shape a joint has: it turns
    /// both ways from rest. Asserting only "the numbers parsed" would pass against a reader that
    /// took the absolute value, or that swapped min and max — and a joint whose range runs the wrong
    /// way is a limb that can only bend backwards.
    ///
    /// Not every axis of every joint is free, so this asks that SOME axis of SOME joint has a real
    /// range rather than that all of them do, and then pins the ordering everywhere.
    /// </remarks>
    [Test]
    public void Read_TheHeavysPhysicsModel_CarriesJointLimitsThatRunTheRightWay()
    {
        PhysicsModel physics = Physics(Heavy);

        foreach (RagdollConstraint joint in physics.Constraints)
        {
            foreach (ConstraintAxis axis in (ConstraintAxis[])[joint.X, joint.Y, joint.Z])
            {
                axis.Minimum.ShouldBeLessThanOrEqualTo(axis.Maximum);
            }
        }

        physics.Constraints.ShouldContain(
            joint => joint.X.Minimum < -1f && joint.X.Maximum > 1f,
            "a ragdoll has at least one joint that turns both ways about its first axis");
    }

    /// <remarks>
    /// **Every class, because a reader that works on one file has been right once.** The counts are
    /// Valve's and differ per class, so this is nine independent predictions rather than one
    /// repeated — and the tree property has to hold for all of them.
    /// </remarks>
    [Test]
    public void Read_EveryClassModel_HoldsACompleteJointTree()
    {
        foreach ((string model, int solids) in Classes)
        {
            if (Read($"models/player/{model}.phy") is not { } physics)
            {
                Assert.Ignore("the game is not installed");
                return;
            }

            physics.Solids.Count.ShouldBe(solids, model);
            physics.Constraints.Count.ShouldBe(solids - 1, model);
            physics.Solids.ShouldAllBe(solid => solid.Name.Length > 0);
        }
    }

    /// <remarks>
    /// **The case no shipped file supplies, authored because a sabotage found nothing to redden.**
    /// Removing the reader's final block-close changes no count on any of TF2's `.phy` files, since
    /// every one ends with a trailing `editparams` block whose opening closes the last constraint.
    /// That makes the line look dead and it is not: the format does not require the trailing block,
    /// and a text ending on its last joint would lose it silently.
    ///
    /// A sabotage that reddens nothing names a missing INPUT
    /// (`docs/memory/most-of-a-decoder-is-untested.md#a-sabotage-that-reddens-nothing-names-the-missing-input`), so the input is
    /// written here — a minimal `.phy`, header and all, ending exactly on its final block.
    /// </remarks>
    [Test]
    public void Read_APhysicsFileEndingOnItsLastJoint_KeepsThatJoint()
    {
        byte[] file =
        [
            .. BitConverter.GetBytes(16),          // size, which Valve writes as sizeof(phyheader_t)
            .. BitConverter.GetBytes(0x59485056),  // id
            .. BitConverter.GetBytes(2),           // solidCount
            .. BitConverter.GetBytes(0),           // checkSum
            .. System.Text.Encoding.ASCII.GetBytes("""
                solid {
                  "index" "0"
                  "name" "bip_pelvis"
                  "mass" "7.470685"
                }
                solid {
                  "index" "1"
                  "name" "bip_spine_0"
                  "mass" "5.000000"
                }
                ragdollconstraint {
                  "parent" "0"
                  "child" "1"
                  "xmin" "-35.000000"
                  "xmax" "12.000000"
                  "xfriction" "0.000000"
                }
                """),
        ];

        PhysicsModel physics = PhysicsModel.Read(file);

        physics.Solids.Count.ShouldBe(2);
        physics.Constraints.Count.ShouldBe(1, "the last block is closed at end of text");

        physics.Constraints[0].Parent.ShouldBe(0);
        physics.Constraints[0].Child.ShouldBe(1);
        physics.Constraints[0].X.Minimum.ShouldBe(-35f);
        physics.Constraints[0].X.Maximum.ShouldBe(12f);

        // The control on the assertion above: the second solid must survive too, so a reader that
        // kept only the final block would fail rather than pass on a count of one.
        physics.Solids[1].Name.ShouldBe("bip_spine_0");
    }

    /// <remarks>
    /// **Each solid's mass center and rotation inertia come out beside its hull, indexed like it** (B403).
    /// The engine takes both from the solid's compact surface through the surface manager — see
    /// `PhysicsHullConformanceTests` for the offsets — and a ragdoll element needs them per solid, so they
    /// ride the same blob walk that yields the hulls rather than a second pass over the file.
    /// </remarks>
    [Test]
    public void Read_ASolidWithACompactSurface_CarriesItsMassProperties()
    {
        byte[] file = Phy(Surface(tagged: true, type: 0, new System.Numerics.Vector3(0.1f, -0.2f, 0.3f)));

        PhysicsModel physics = PhysicsModel.Read(file);

        physics.MassProperties.Count.ShouldBe(1);

        PhysicsMassProperties? read = physics.MassProperties[0];

        read.ShouldNotBeNull();
        read.Value.MassCenter.ShouldBe(new System.Numerics.Vector3(0.1f, -0.2f, 0.3f));
        read.Value.RotationInertia.ShouldBe(new System.Numerics.Vector3(0.004f, 0.005f, 0.006f));

        // The control: the text half of the same solid is still read, so the blob walk did not swallow it.
        physics.Solids[0].Name.ShouldBe("bip_pelvis");
    }

    /// <remarks>
    /// **A solid the loader nulls keeps its slot** (B404). `FUN_18000a100` writes each solid's collide, NULL or not,
    /// into `vcollide_t::solids[i]` and moves on (`vcollide.h:16-22`), so the solid after it keeps its index — and
    /// the constraints and the text blocks refer to solids by that index. The middle solid is a `"Null physics
    /// model"`; the last is an untagged surface, so its mass center arriving at index 2 also shows it was read from
    /// its first byte.
    /// </remarks>
    [Test]
    public void Read_ASolidTheLoaderNulls_KeepsItsSlot()
    {
        byte[] file = Phy(
            Surface(tagged: true, type: 0, new System.Numerics.Vector3(1f, 0f, 0f)),
            Surface(tagged: true, type: 1, new System.Numerics.Vector3(2f, 0f, 0f)),
            Surface(tagged: false, type: 0, new System.Numerics.Vector3(3f, 0f, 0f)));

        PhysicsModel physics = PhysicsModel.Read(file);

        physics.Hulls.Count.ShouldBe(3);
        physics.MassProperties.Count.ShouldBe(3);
        physics.MassProperties[0]!.Value.MassCenter.X.ShouldBe(1f);
        physics.MassProperties[1].ShouldBeNull("a Null physics model has no surface");
        physics.MassProperties[2]!.Value.MassCenter.X.ShouldBe(3f);
    }

    /// <remarks>
    /// **A solid with no `VPHY` tag and under `0x30` bytes refuses the whole file** (B404) — `FUN_18000a100` compares
    /// the size prefix unsigned against `0x30` and calls `Error("Corrupt physics model")`, which does not return. The
    /// solid before it is well-formed, so the refusal is the corrupt one's and not an earlier failure's.
    /// </remarks>
    [TestCase(0)]
    [TestCase(0x2F)]
    public void Read_AnUntaggedSolidUnder0x30Bytes_RefusesTheFile(int size)
    {
        byte[] file = Phy(Surface(tagged: true, type: 0, System.Numerics.Vector3.Zero), new byte[size]);

        Should.Throw<InvalidDataException>(() => PhysicsModel.Read(file))
            .Message.ShouldContain("Corrupt physics model");
    }

    /// <remarks>
    /// **A file shorter than `phyheader_t` refuses with `InvalidDataException`** (B405) — the type B404's corrupt
    /// solid throws and the type every Scene reader of a `.phy` catches, so a truncated file costs its model its
    /// physics rather than the load (D32). Valve's tools refuse it too: they read the header and return false
    /// (`vradstaticprops.cpp:520`).
    /// </remarks>
    [Test]
    public void Read_AFileShorterThanItsHeader_RefusesWithInvalidDataException()
    {
        Should.Throw<InvalidDataException>(() => PhysicsModel.Read(new byte[4]));
    }

    /// <remarks>
    /// **A header declaring a size other than `sizeof(phyheader_t)` refuses with `InvalidDataException`** (B405).
    /// `if ( header->size != sizeof(*header) || header->solidCount &lt;= 0 ) return false;` —
    /// `vradstaticprops.cpp:520`, and the same line at `glview.cpp:770`. The file is otherwise a well-formed one-solid
    /// `.phy`, so the declared size is the only thing wrong with it.
    /// </remarks>
    [Test]
    public void Read_AHeaderDeclaringASizeOf20_RefusesWithInvalidDataException()
    {
        byte[] file = Phy(Surface(tagged: true, type: 0, System.Numerics.Vector3.Zero));

        BitConverter.GetBytes(20).CopyTo(file, 0);

        Should.Throw<InvalidDataException>(() => PhysicsModel.Read(file));
    }

    /// <summary>A solid holding a bare compact surface, with no ledges.</summary>
    /// <param name="tagged">Whether it opens with the <c>VPHY</c> header.</param>
    /// <param name="type">The header's type word, at <c>+6</c>.</param>
    /// <param name="massCenter">The surface's first three floats.</param>
    /// <returns>The solid's bytes, without its size prefix.</returns>
    /// <remarks>
    /// **Written as a shipped `.phy` writes a solid**: the header's data size at `+8` is everything after the header.
    /// The rotation inertia is fixed and distinct from the mass center, so a read six floats off is visible.
    /// </remarks>
    private static byte[] Surface(bool tagged, short type, System.Numerics.Vector3 massCenter)
    {
        int at = tagged ? 0x1C : 0;
        byte[] blob = new byte[at + 0x30];

        if (tagged)
        {
            "VPHY"u8.CopyTo(blob);
            BitConverter.GetBytes((short)0x100).CopyTo(blob, 0x04);
            BitConverter.GetBytes(type).CopyTo(blob, 0x06);
            BitConverter.GetBytes(0x30).CopyTo(blob, 0x08);
        }

        BitConverter.GetBytes(massCenter.X).CopyTo(blob, at + 0x00);
        BitConverter.GetBytes(massCenter.Y).CopyTo(blob, at + 0x04);
        BitConverter.GetBytes(massCenter.Z).CopyTo(blob, at + 0x08);
        BitConverter.GetBytes(0.004f).CopyTo(blob, at + 0x0C);
        BitConverter.GetBytes(0.005f).CopyTo(blob, at + 0x10);
        BitConverter.GetBytes(0.006f).CopyTo(blob, at + 0x14);
        "IVPS"u8.CopyTo(blob.AsSpan(at + 0x2C));

        return blob;
    }

    /// <summary>A <c>.phy</c> of the given solids, each with its size prefix, and one text block per solid.</summary>
    /// <param name="solids">The solids' bytes, in order.</param>
    /// <returns>The whole file.</returns>
    private static byte[] Phy(params byte[][] solids)
    {
        List<byte> file =
        [
            .. BitConverter.GetBytes(16),             // size, which Valve writes as sizeof(phyheader_t)
            .. BitConverter.GetBytes(0x59485056),     // id
            .. BitConverter.GetBytes(solids.Length),  // solidCount
            .. BitConverter.GetBytes(0),              // checkSum
        ];

        foreach (byte[] solid in solids)
        {
            file.AddRange(BitConverter.GetBytes(solid.Length));
            file.AddRange(solid);
        }

        string[] names = ["bip_pelvis", "bip_spine_0", "bip_spine_1"];

        for (int index = 0; index < solids.Length; index++)
        {
            file.AddRange(System.Text.Encoding.ASCII.GetBytes(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"solid {{\n  \"index\" \"{index}\"\n  \"name\" \"{names[index]}\"\n  \"mass\" \"7.470685\"\n}}\n")));
        }

        return [.. file];
    }

    /// <summary>Solid counts per class, measured with the `ragdoll-constraints` probe.</summary>
    private static readonly (string Model, int Solids)[] Classes =
    [
        ("demo", 15),
        ("pyro", 15),
        ("heavy", 16),
        ("scout", 17),
        ("sniper", 17),
        ("engineer", 18),
        ("medic", 24),
    ];

    /// <summary>The physics model, or skips the calling test when the game is absent.</summary>
    /// <remarks>
    /// **This threw, and CI is the only machine that could tell.** Six tests here reported as
    /// FAILURES on the machine without Team Fortress 2 rather than as skips, because the missing
    /// install was raised as an `InvalidOperationException` — which NUnit records as a failure like
    /// any other. <see cref="Skip.Unless{T}"/> exists for exactly this and was written before these
    /// tests were; reaching for a throw was the mistake.
    ///
    /// See also `docs/memory/ci-is-the-machine-without-tf2.md`: this is the second family of
    /// absent-game guard that only CI could falsify, found in the same session as B333.
    /// </remarks>
    private static PhysicsModel Physics(string path) =>
        Skip.Unless(Read(path), GameInstall.Missing);

    private static PhysicsModel? Read(string path)
    {
        if (GameInstall.Root is not { } tf)
        {
            return null;
        }

        GameArchives archives = GameArchives.Open(tf);

        return archives.Read(path) is { } bytes ? PhysicsModel.Read(bytes) : null;
    }
}

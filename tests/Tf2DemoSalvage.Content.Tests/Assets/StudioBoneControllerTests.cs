using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The bone controller table: what a networked fraction means.
/// </summary>
/// <remarks>
/// **Almost entirely fixture-driven, and unusually so, because TF2 declares none.** Measured
/// 2026-08-24: the scout, heavy and soldier drive no bone from a controller at all. That makes this
/// the one reader in the family whose real-world exercise is a model nobody here has — so the
/// fixtures are not a convenience, they are the only cases that exist.
///
/// **It is read anyway** because the demo carries <c>m_flEncodedController</c> whether or not this
/// project uses it (<c>baseanimating.cpp:248</c>, eleven bits over 0..1), and because the emptiness
/// is worth being able to assert rather than assume — a model that does use one should surface as a
/// failing expectation, not as a silently wrong pose.
/// </remarks>
public sealed class StudioBoneControllerTests
{
    private const int HeaderSize = 408;

    [Test]
    public void Read_AControllersRange_KeepsBothEndsThatGiveTheWireValueMeaning()
    {
        // **Start and end together, because either alone is useless.** The demo sends a fraction;
        // this is what it spans. A reader that dropped `end` would leave every controlled bone at
        // its start value, which looks like a bone that simply does not move.
        IReadOnlyList<StudioBoneController> controllers = StudioBoneControllers.Read(
            Model(Controller(bone: 7, type: 3, start: -45f, end: 45f)));

        controllers.Count.ShouldBe(1);
        controllers[0].Bone.ShouldBe(7);
        controllers[0].Type.ShouldBe(3);
        controllers[0].Start.ShouldBe(-45f);
        controllers[0].End.ShouldBe(45f);
    }

    [Test]
    public void Read_SeveralControllers_KeepTheirOwnRanges()
    {
        // Distinct values per entry: three copies of one controller cannot show that the stride is
        // right, and the stride is 56 rather than the 24 the field list suggests — the difference is
        // an int unused[8] tail that carries no data and must still be stepped over.
        IReadOnlyList<StudioBoneController> controllers = StudioBoneControllers.Read(
            Model(
                Controller(bone: 1, type: 0, start: 0f, end: 1f),
                Controller(bone: 2, type: 1, start: 10f, end: 20f),
                Controller(bone: 3, type: 2, start: -90f, end: 90f)));

        controllers.Count.ShouldBe(3);
        controllers[2].Bone.ShouldBe(3);
        controllers[2].Start.ShouldBe(-90f);
        controllers[2].End.ShouldBe(90f);
    }

    [Test]
    public void Read_AModelWithNoControllers_ReportsNoneRatherThanThrowing()
    {
        StudioBoneControllers.Read(new byte[HeaderSize]).ShouldBeEmpty();
    }

    [Test]
    public void Read_AHeaderClaimingMoreControllersThanTheFileHolds_IsRefused()
    {
        byte[] file = new byte[HeaderSize];

        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderBoneControllerCountOffset), 9);
        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderBoneControllerIndexOffset), HeaderSize - 8);

        Should.Throw<InvalidDataException>(() => StudioBoneControllers.Read(file));
    }

    [Test]
    public void Read_AHeaderClaimingAnAbsurdCount_IsRefusedBeforeAnythingIsSized()
    {
        // Untrusted input, per D32. The point is that this is refused on the COUNT, before the
        // bounds check that would also catch it — allocating a list sized from a corrupt header is
        // the failure, and a bounds check happening first would hide whether the cap works.
        byte[] file = new byte[HeaderSize];

        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderBoneControllerCountOffset), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderBoneControllerIndexOffset), HeaderSize);

        Should.Throw<InvalidDataException>(() => StudioBoneControllers.Read(file));
    }

    private static byte[] Controller(int bone, int type, float start, float end)
    {
        byte[] controller = new byte[StudioLayout.BoneControllerStride];

        BinaryPrimitives.WriteInt32LittleEndian(
            controller.AsSpan(StudioLayout.BoneControllerBoneOffset), bone);
        BinaryPrimitives.WriteInt32LittleEndian(
            controller.AsSpan(StudioLayout.BoneControllerTypeOffset), type);
        BinaryPrimitives.WriteSingleLittleEndian(
            controller.AsSpan(StudioLayout.BoneControllerStartOffset), start);
        BinaryPrimitives.WriteSingleLittleEndian(
            controller.AsSpan(StudioLayout.BoneControllerEndOffset), end);

        return controller;
    }

    private static byte[] Model(params byte[][] controllers)
    {
        int table = HeaderSize;
        byte[] file = new byte[table + (controllers.Length * StudioLayout.BoneControllerStride)];

        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderBoneControllerCountOffset), controllers.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(StudioLayout.HeaderBoneControllerIndexOffset), table);

        for (int index = 0; index < controllers.Length; index++)
        {
            controllers[index].CopyTo(file, table + (index * StudioLayout.BoneControllerStride));
        }

        return file;
    }
}

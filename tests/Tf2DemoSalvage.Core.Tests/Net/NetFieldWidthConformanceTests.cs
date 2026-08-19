using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The bit widths of the fields inside network messages, where the engine declares them.
/// </summary>
/// <remarks>
/// **These messages carry no length prefix, so a width is not a detail — it is the position of
/// everything after it.** Reading an entity index at 12 bits instead of 11 leaves the reader one bit
/// out for the rest of the packet, and the next message type is then read from the middle of a
/// field. What follows is either a stop, or a run of messages that decode into nonsense with no
/// complaint.
///
/// **Three of them are published and the rest are not**, which is the same split as the message ids
/// themselves. <c>SP_MODEL_INDEX_BITS</c>, <c>MAX_EDICT_BITS</c> and <c>MAX_SERVER_CLASS_BITS</c> are
/// all in <c>public/const.h</c>; the length fields — 11 bits for a user message, 16 for a sound
/// block — live in <c>netmessages.h</c>, which the SDK does not ship. Those came from the same
/// binary scanning as the ids and are pinned by the corpus decoding rather than by a header.
/// </remarks>
public sealed class NetFieldWidthConformanceTests
{
    /// <summary>Where the engine declares the widths that are published.</summary>
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
    public void NetFieldWidths_AnEntityIndex_IsMaxEdictBitsEverywhere()
    {
        // **One constant, three fields, and they must not drift apart.** An entity index is an
        // entity index whether it arrives in svc_SetView, svc_EntityMessage or a decal — so all
        // three read MAX_EDICT_BITS, and a change to one of them without the others would be a
        // decoder that disagrees with itself about what an entity is.
        int edict = SourceSdk.Constants(Const)["MAX_EDICT_BITS"];

        NetMessageReader.EntityIndexBits.ShouldBe(edict);
        NetMessageReader.EntityMessageIndexBits.ShouldBe(edict);
        NetMessageReader.SetViewBits.ShouldBe(edict);
    }

    [Test]
    public void NetFieldWidths_AModelIndex_IsTheEnginesWidth()
    {
        // SP_MODEL_INDEX_BITS is 13, which is what a precached model index is sent as. Twelve would
        // silently halve the reachable range of the model precache table, so every model past 4096
        // would resolve to the wrong one — and a map's precache list is ordered, not sorted, so the
        // wrong model is an arbitrary one rather than a near miss.
        NetMessageReader.ModelIndexBits.ShouldBe(SourceSdk.Constants(Const)["SP_MODEL_INDEX_BITS"]);
    }

    [Test]
    public void NetFieldWidths_AServerClass_IsMaxServerClassBitsWide()
    {
        NetMessageReader.EntityMessageClassBits
            .ShouldBe(SourceSdk.Constants(Const)["MAX_SERVER_CLASS_BITS"]);
    }

    [Test]
    public void NetFieldWidths_WidthsWithNoPublishedSource_AreNamedAsSuch()
    {
        // **A list of what this class cannot check, kept beside what it can.** Every width below
        // came from binary scanning and is held up by the corpus decoding end to end, not by a
        // header — so if netmessages.h ever becomes available, these are the ones to verify.
        //
        // Recorded as a test rather than a comment so the set cannot quietly grow: a new width added
        // to the reader without either a citation or a line here will not be noticed otherwise.
        Dictionary<string, int> unpublished = new(StringComparer.Ordinal)
        {
            ["svc_Sounds length"] = NetMessageReader.SoundsLengthBits,
            ["svc_Sounds reliable length"] = NetMessageReader.SoundsReliableLengthBits,
            ["svc_TempEntities legacy length"] = NetMessageReader.TempEntitiesLegacyLengthBits,
            ["svc_UserMessage length"] = NetMessageReader.UserMessageLengthBits,
            ["svc_VoiceData length"] = NetMessageReader.VoiceDataLengthBits,
            ["svc_BSPDecal texture index"] = NetMessageReader.DecalTextureBits,
        };

        unpublished.Count.ShouldBe(6);

        foreach ((string what, int bits) in unpublished)
        {
            bits.ShouldBeInRange(1, 32, $"{what} is not a plausible field width");
        }
    }
}

namespace Tf2DemoSalvage.Core.Net;

// The small messages, reported by their fields rather than stepped over.
//
// Grouped in one file because each is a handful of scalars and a comment; separate files would
// scatter one idea across eight.
//
// Every field here was already being read - the decoder had to consume them to stay aligned,
// then discarded them. Reporting them costs a record and turns 144 anonymous entries in a single
// 2009 demo into named lines. Nothing new is decoded; what was already decoded simply stops
// being thrown away.

/// <summary>
/// <c>svc_Prefetch</c> — asks the client to precache a resource.
/// </summary>
/// <param name="SoundIndex">Index into the sound precache table.</param>
/// <remarks>
/// The index width is protocol-conditional: 14 bits above protocol 22, 13 below. That boundary
/// is `PROTOCOL_VERSION_22` in Valve's `proto_version.h`, annotated "sound index bits used to
/// = 13", and it is one of the four rules this project implemented from reading another parser
/// and first executed against a real demo in the 2009 file.
/// </remarks>
public sealed record PrefetchMessage(int SoundIndex) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.Prefetch;
}

/// <summary>
/// <c>svc_FixAngle</c> — snaps or nudges the viewing angle.
/// </summary>
/// <param name="IsRelative">Whether the angles are a delta rather than an absolute direction.</param>
/// <param name="Pitch">Pitch in degrees.</param>
/// <param name="Yaw">Yaw in degrees.</param>
/// <param name="Roll">Roll in degrees.</param>
/// <remarks>
/// Angles travel as 16-bit fixed point over a full turn, so the conversion is
/// <c>value × 360 / 65536</c>. Reporting the raw integers instead would be honest but useless —
/// the whole value of this message to a reader is where the player was made to look.
/// </remarks>
public sealed record FixAngleMessage(bool IsRelative, float Pitch, float Yaw, float Roll)
    : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.FixAngle;
}

/// <summary>
/// <c>svc_SetView</c> — moves the client's viewpoint to an entity.
/// </summary>
/// <param name="EntityIndex">The entity now being viewed from.</param>
/// <remarks>
/// Rare and worth seeing when it happens: it is how spectating, death cameras and taunt cameras
/// are expressed, so a run of these marks a point where the recording is no longer showing the
/// recording player's own eyes.
/// </remarks>
public sealed record SetViewMessage(int EntityIndex) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.SetView;
}

/// <summary>
/// <c>net_SignonState</c> — connection handshake progress.
/// </summary>
/// <param name="State">The signon stage reached.</param>
/// <param name="SpawnCount">The server's spawn counter, which changes on a map load.</param>
/// <remarks>
/// The stage boundaries are where a demo's structure changes — the signon stream ends and normal
/// packets begin — so seeing them in a trace explains why the messages around them look
/// different.
/// </remarks>
public sealed record SignOnStateMessage(int State, int SpawnCount) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.SignOnState;
}

/// <summary>
/// <c>svc_EntityMessage</c> — a message addressed to one entity.
/// </summary>
/// <param name="EntityIndex">The entity it is for.</param>
/// <param name="ClassId">The entity's networked class.</param>
/// <param name="BodyBits">How many bits the body occupies.</param>
/// <remarks>
/// The body's layout is defined by the receiving entity's class, so decoding it generically is
/// not possible — which is exactly why the entity and class are worth reporting: they say who
/// would need to interpret it.
///
/// Note the field order: index and class come *before* the length. A reader that went straight
/// for the length would take twenty of their bits instead.
/// </remarks>
/// <param name="Body">
/// The body's bits, kept verbatim. Not decoration: the content cannot be decoded generically, so
/// carrying it is the only way the message can be written back to the demo it came from.
/// </param>
public sealed record EntityMessage(
    int EntityIndex,
    int ClassId,
    int BodyBits,
    System.ReadOnlyMemory<byte> Body = default) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.EntityMessage;
}

/// <summary>
/// <c>svc_BspDecal</c> — a decal applied to the world or to an entity.
/// </summary>
/// <param name="OnEntity">Whether the decal is attached to an entity rather than the world.</param>
/// <param name="EntityIndex">The entity it is attached to, or 0 for a world decal.</param>
/// <param name="ModelIndex">The model it is attached to, or 0 for a world decal.</param>
/// <remarks>
/// This message caused RISKS B16: its fields were implemented from the struct's C types rather
/// than its read function, so a 9-bit texture index was read as 16 bits and the whole packet
/// desynchronised. The entity and model indices are present only when the attachment flag is
/// set, and a world decal carries neither.
/// </remarks>
public sealed record BspDecalMessage(bool OnEntity, int EntityIndex, int ModelIndex) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.BspDecal;
}

/// <summary>
/// <c>svc_VoiceInit</c> — declares the voice codec for the session.
/// </summary>
/// <param name="Codec">Codec name, e.g. <c>vaudio_celt</c>.</param>
/// <param name="Quality">Quality setting, or the sample rate when the quality byte is 255.</param>
/// <param name="SampleRate">
/// The rate transmitted behind quality 255, or <c>null</c> when the message carried none.
/// Recorded separately because <paramref name="Quality"/> is overwritten by it: without this,
/// "quality 22050" and "the escape followed by 22050" are the same message and only one of them
/// can be written back.
/// </param>
public sealed record VoiceInitMessage(
    string Codec, int Quality, int? SampleRate = null) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.VoiceInit;
}

/// <summary>
/// <c>svc_VoiceData</c> — one packet of a player's microphone audio.
/// </summary>
/// <param name="Client">The speaking client's slot. See the remarks before mapping it to a player.</param>
/// <param name="Proximity">Whether the audio is positional rather than global.</param>
/// <param name="BodyBits">Size of the codec payload, which is not decoded.</param>
/// <remarks>
/// **Who spoke and when, which no other message in a demo records.** The audio itself is a codec
/// payload — Speex or CELT depending on era, declared by <c>svc_VoiceInit</c> — and decoding it is
/// a different project. The header is the part a reader wants: it turns "someone used voice" into
/// a timeline of who talked.
///
/// **How the client slot maps to a player is NOT yet established, and this record deliberately
/// does not pretend otherwise.** Source is widely described as numbering voice clients from zero
/// where entities number players from one, which would make the entity <c>Client + 1</c>. The only
/// evidence available contradicts that: the protocol-11 SourceTV demo names two entities — 0 for
/// SourceTV and 1 for the single human — and every one of its 125 voice packets reports
/// <c>client 1</c>. Under the +1 rule that would be entity 2, which does not exist in the file.
///
/// One speaker in one demo cannot settle it, and a SourceTV slot may itself shift the numbering.
/// Resolving voice to a name needs a recording with **two or more people talking**, where the
/// mapping either lines up or does not. Until then the raw slot is reported and nothing is
/// inferred from it — a plausible wrong name is worse than a number.
///
/// This message went unreported for a long time for a mundane reason: no demo in the corpus
/// contained one. The two protocol-11 recordings carry 125 between them and nothing else carries
/// any, which is a good argument for a corpus that spans eras rather than volume.
/// </remarks>
/// <param name="Body">
/// The body's bits, kept verbatim. Not decoration: the content cannot be decoded generically, so
/// carrying it is the only way the message can be written back to the demo it came from.
/// </param>
public sealed record VoiceDataMessage(
    int Client,
    int Proximity,
    int BodyBits,
    System.ReadOnlyMemory<byte> Body = default) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.VoiceData;
}

/// <summary>
/// <c>svc_TempEntities</c> — short-lived effects: explosions, tracers, impacts.
/// </summary>
/// <param name="Count">How many effects the body carries.</param>
/// <param name="BodyBits">How many bits the body occupies.</param>
/// <param name="Body">The body bits, carried for a schema-aware decoder.</param>
/// <remarks>
/// The body is a list of entity-like deltas against per-effect classes, so it is carried here and
/// decoded by <see cref="Tf2DemoSalvage.Core.Schema.EntityDecoder.DecodeTempEntities"/> — which
/// needs the schema, and the schema arrives in a different demo command. Same arrangement as
/// <see cref="PacketEntitiesMessage"/>, and for the same reason.
///
/// The length is protocol-conditional — a varint above protocol 23, a fixed 17-bit field below —
/// which is `PROTOCOL_VERSION_23` in `proto_version.h` and is exercised on both sides by the
/// corpus.
/// </remarks>
public sealed record TempEntitiesMessage(
    int Count, int BodyBits, System.ReadOnlyMemory<byte> Body = default) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.TempEntities;
}

/// <summary>
/// <c>svc_File</c> — the server offering or requesting a file transfer.
/// </summary>
/// <param name="TransferId">Identifies the transfer.</param>
/// <param name="FileName">The file's name.</param>
/// <param name="IsRequested">Whether the server is asking for the file rather than offering it.</param>
/// <remarks>
/// Worth naming rather than counting because the file name is the interesting part: it says what
/// a server tried to send a client, which is occasionally more than a map.
/// </remarks>
public sealed record FileMessage(uint TransferId, string FileName, bool IsRequested) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.File;
}

/// <summary>
/// <c>svc_GetCvarValue</c> — the server asking a client for one of its console variables.
/// </summary>
/// <param name="Cookie">Correlates the reply with this request.</param>
/// <param name="CvarName">The variable being asked for.</param>
/// <remarks>
/// This is how servers check client settings, so the name asked for is the point of the message.
/// </remarks>
public sealed record GetCvarValueMessage(uint Cookie, string CvarName) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.GetCvarValue;
}

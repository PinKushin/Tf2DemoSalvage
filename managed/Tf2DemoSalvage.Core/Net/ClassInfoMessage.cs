using System.Collections.Generic;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One networked server class.</summary>
/// <param name="Id">Class id, as referenced by entity updates.</param>
/// <param name="ClassName">C++ class name, e.g. <c>CTFPlayer</c>.</param>
/// <param name="TableName">Its SendTable, e.g. <c>DT_TFPlayer</c>.</param>
public readonly record struct ServerClass(int Id, string ClassName, string TableName);

/// <summary>
/// <c>svc_ClassInfo</c> — the list of networked classes, linking class ids to SendTables.
/// </summary>
/// <param name="ClassCount">How many classes exist, whether or not they are listed.</param>
/// <param name="CreateOnClient">
/// When true the server sends no entries and expects the client to build the list from its own
/// compiled-in classes. A standalone parser cannot do that, so the list must come from the
/// demo's <c>dem_datatables</c> instead.
/// </param>
/// <param name="Classes">The classes, empty when <paramref name="CreateOnClient"/> is set.</param>
/// <remarks>
/// This message sizes a field used in every later packet: an entity's class id is read with
/// <see cref="ClassIdBits"/> bits, derived from the count rather than transmitted. A wrong
/// count here would not fail here — it would misread every entity in the demo.
/// </remarks>
public sealed record ClassInfoMessage(
    int ClassCount,
    bool CreateOnClient,
    IReadOnlyList<ServerClass> Classes) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.ClassInfo;

    /// <summary>
    /// Bits needed to index the class list. Derived from <see cref="ClassCount"/>, and used by
    /// the entity decoder to read a class id.
    /// </summary>
    public int ClassIdBits => WireWidths.ClassId(ClassCount);
}

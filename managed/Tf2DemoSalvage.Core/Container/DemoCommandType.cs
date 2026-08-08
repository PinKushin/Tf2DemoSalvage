using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// Command types in a demo's command stream. Values are the on-disk encoding.
/// </summary>
/// <remarks>
/// CONFIRMED against three corpus demos. Newer demo protocol versions add
/// <c>dem_customdata</c>; its value is unverified and deliberately absent until a specimen
/// new enough to check exists (<c>docs/FORMAT_NOTES.md</c>).
/// </remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value",
    Justification = "0 is not a valid on-disk command byte. A None = 0 member would make " +
                    "Enum.IsDefined accept it, defeating the check that rejects corrupt input.")]
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte is the on-disk encoding width, not a style preference.")]
public enum DemoCommandType : byte
{
    /// <summary>Signon data. Same payload shape as <see cref="Packet"/>.</summary>
    Signon = 1,

    /// <summary>A network packet. The bulk of any demo — one per frame.</summary>
    Packet = 2,

    /// <summary>Clock synchronisation marker. No payload.</summary>
    SyncTick = 3,

    /// <summary>A console command string.</summary>
    ConsoleCmd = 4,

    /// <summary>User input. Only present in point-of-view demos, never in SourceTV.</summary>
    UserCmd = 5,

    /// <summary>The embedded entity schema (SendTables) — what makes a demo self-describing.</summary>
    DataTables = 6,

    /// <summary>End of the demo. No payload, and its header is short — see the stream reader.</summary>
    Stop = 7,

    /// <summary>String tables, including the player list.</summary>
    StringTables = 8,
}

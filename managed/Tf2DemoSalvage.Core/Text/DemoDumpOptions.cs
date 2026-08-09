namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Controls how much of a demo the text dump reports.
/// </summary>
public sealed record DemoDumpOptions
{
    /// <summary>
    /// Whether to list every command individually. Defaults to <c>true</c>, which is the point
    /// of a dump — but a 75 MB demo produces ~120,000 rows, so callers wanting just the header
    /// and summary can turn it off.
    /// </summary>
    public bool IncludeCommandListing { get; init; } = true;

    /// <summary>Whether to decode and summarise the demo's game events.</summary>
    /// <remarks>
    /// On by default: the events are what the demo is <em>about</em>, and everything above them
    /// in the dump is structure. Costs a full pass over every packet, so it is switchable for
    /// callers that only want the container view.
    /// </remarks>
    public bool IncludeGameEvents { get; init; } = true;

    /// <summary>How many individual events to list before summarising the rest.</summary>
    public int GameEventSampleSize { get; init; } = 40;

    /// <summary>Whether to list the players named by the <c>userinfo</c> string table.</summary>
    public bool IncludePlayers { get; init; } = true;

    /// <summary>Whether to include the match's chat log.</summary>
    public bool IncludeChat { get; init; } = true;

    /// <summary>How many chat lines to print before summarising the rest.</summary>
    public int ChatSampleSize { get; init; } = 60;
}

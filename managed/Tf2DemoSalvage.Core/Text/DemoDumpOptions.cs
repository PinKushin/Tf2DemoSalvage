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
}

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>An appearance that names a player model for every class.</summary>
/// <remarks>
/// **Shared rather than copied, the second time it was needed.** It began private to
/// <c>PlayerCycleAdvanceWiringTests</c>; a second wiring test needing the same six members is the
/// point at which a copy becomes the drift this project has been bitten by
/// (`docs/memory/extraction-without-adoption-is-not-dry.md`), because two stubs answer differently
/// the moment one is adjusted for a test the other never runs.
///
/// **Every member answers something usable, deliberately.** `PlayerProps.Add` drops a player whose
/// model does not resolve, so an appearance returning null for `ModelOf` silently produces an empty
/// draw list — which reads as "the wiring is broken" and is the fixture.
/// </remarks>
internal sealed class StubAppearance : IPlayerAppearance
{
    /// <inheritdoc/>
    public string? ModelOf(int playerClass) => "models/player/scout.mdl";

    /// <inheritdoc/>
    public string? WeaponSuffix(string? weaponClass, int? playerClass) => "PRIMARY";

    /// <inheritdoc/>
    public bool Airwalks(int playerClass) => true;

    /// <inheritdoc/>
    public bool Lands(int playerClass) => true;

    /// <inheritdoc/>
    public string? Hands(int playerClass) => null;

    /// <inheritdoc/>
    /// <remarks>
    /// **Nothing, so a test that does not set out to measure equipment measures none.** The tests
    /// that DO are in <c>PlayerBodygroupWiringTests</c>, which supplies its own wardrobe — a shared
    /// stub answering with a hat would change what every other pose test observes.
    /// </remarks>
    public ItemBodygroups BodygroupsOf(int itemDefinitionIndex) => ItemBodygroups.None;
}

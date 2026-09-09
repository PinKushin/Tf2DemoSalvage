using Tf2DemoSalvage.Core.Scene;

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

    /// <summary>What every scene resolves to, or null for an appearance that resolves none.</summary>
    /// <remarks>
    /// **Settable, because a taunt test must name a sequence without an install.** The compiled scene
    /// archive is 3.6 MB of game data and no test should need it to assert that a resolved scene
    /// becomes a pose layer (B351). Null by default, which is what a machine with no TF2 answers.
    /// </remarks>
    public SceneTaunt? Taunt { get; init; }

    /// <inheritdoc/>
    public SceneTaunt? TauntForScene(string scene) => Taunt;

    /// <inheritdoc/>
    /// <remarks>
    /// **Nothing, so a test that does not set out to measure equipment measures none.** The tests
    /// that DO are in <c>PlayerBodygroupWiringTests</c>, which supplies its own wardrobe — a shared
    /// stub answering with a hat would change what every other pose test observes.
    /// </remarks>
    public ItemBodygroups BodygroupsOf(int itemDefinitionIndex) => ItemBodygroups.None;
}

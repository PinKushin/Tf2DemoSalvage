using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CEconEntity::UpdateSingleParticleSystem</c>'s name lookup (<c>econ_entity.cpp:1659-1693</c>, B69 e): which system an
/// item's entry names once the weapon's suffix and the view model's <c>_vm</c> are applied, and whether the level has it.
/// </summary>
/// <remarks>
/// <code>
/// if ( pSystem->bUseSuffixName &amp;&amp; pEconItemView &amp;&amp; pEconItemView->GetItemDefinition()->GetParticleSuffix() )
///     pszSystemName = name + "_" + suffix;
/// bool bHasUniqueVMEffect = true;
/// if ( pSystem->bDrawInViewModel )
/// {
///     pszTempNameVM = pszSystemName + "_vm";
///     if ( FindParticleSystem( pszTempNameVM ) == NULL ) { pszTempNameVM = pszSystemName; bHasUniqueVMEffect = false; }
///     if ( bIsVM ) pszSystemName = pszTempNameVM;
/// }
/// if ( FindParticleSystem( pszSystemName ) == NULL ) return;
/// </code>
/// </remarks>
public sealed class EconParticleNameConformanceTests
{
    [Test]
    public void Resolve_ASuffixedEntryOnAWeaponWithASuffix_NamesTheSuffixedSystem()
    {
        EconParticleName name = EconParticleSystems.Resolve(Entry("pipe_static", suffix: true), "flamethrower", viewModel: false, Found("pipe_static_flamethrower"))
            .ShouldNotBeNull();

        name.System.ShouldBe("pipe_static_flamethrower");
    }

    [Test]
    public void Resolve_ASuffixedEntryOnAnItemWithNoSuffix_KeepsItsOwnName()
    {
        EconParticleSystems.Resolve(Entry("pipe_static", suffix: true), null, viewModel: false, Found("pipe_static"))
            .ShouldNotBeNull().System.ShouldBe("pipe_static");
    }

    [Test]
    public void Resolve_AnUnsuffixedEntryOnAWeaponWithASuffix_IgnoresTheSuffix()
    {
        EconParticleSystems.Resolve(Entry("pipe_static", suffix: false), "flamethrower", viewModel: false, Found("pipe_static", "pipe_static_flamethrower"))
            .ShouldNotBeNull().System.ShouldBe("pipe_static");
    }

    [Test]
    public void Resolve_ASystemTheLevelDoesNotHave_IsNull()
    {
        EconParticleSystems.Resolve(Entry("superrare_burning1"), null, viewModel: false, Found("superrare_burning2")).ShouldBeNull();
    }

    [Test]
    public void Resolve_AViewModelEntryWithItsOwnVmSystem_NamesItInTheViewModelOnly()
    {
        Func<string, bool> loaded =Found("glow", "glow_vm");

        EconParticleName world = EconParticleSystems.Resolve(Entry("glow", viewModel: true), null, viewModel: false, loaded).ShouldNotBeNull();
        EconParticleName held = EconParticleSystems.Resolve(Entry("glow", viewModel: true), null, viewModel: true, loaded).ShouldNotBeNull();

        (world.System, world.ViewModel, world.UniqueViewModel).ShouldBe(("glow", "glow_vm", true));
        held.System.ShouldBe("glow_vm");
    }

    [Test]
    public void Resolve_AViewModelEntryWithoutAVmSystem_FallsBackToItsOwn()
    {
        EconParticleName held = EconParticleSystems.Resolve(Entry("glow", viewModel: true), null, viewModel: true, Found("glow")).ShouldNotBeNull();

        (held.System, held.ViewModel, held.UniqueViewModel).ShouldBe(("glow", "glow", false));
    }

    private static EconParticle Entry(string system, bool suffix = false, bool viewModel = false) =>
        new(system, 0, false, viewModel, suffix, [null]);

    /// <summary>`g_pParticleSystemMgr->FindParticleSystem( name ) != NULL` over the level's loaded systems.</summary>
    private static Func<string, bool> Found(params string[] names) => new HashSet<string>(names).Contains;
}

using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The friction system's tangential pass — the controller it is filed as at priority <c>600</c>, whose slot 4 is
/// <c>FUN_1800843c0</c> (B369, D172).
/// </summary>
/// <param name="system">The friction system this controller is a face of — its base <c>+0x0</c>.</param>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`, *The friction controller at priority 600*). The same system is filed three
/// times at different priorities; this is the middle one, and it is the pass that spends each pair's friction-cone budget.
///
/// <code>
/// one contact or none:  cp = the list head
///     FUN_180083970(cp, ((step² · cp+0x60) · (cp+0x88 · cp+0x78)));  cp+0x64 → FUN_180085100 else FUN_1800857c0
/// more:  FUN_1800836b0(system, frame);  every pair, last first:  pair+0x20 −= 1;  reaching 0 → FUN_180084680, pair+0x20 = 5
/// </code>
///
/// *Not carried*: the sticking branch `FUN_180085100` behind a contact's <see cref="IvpContactPoint.UsesMaterialAxes"/> (D175),
/// and the every-fifth-PSI `FUN_180084680` with the pair countdown that drives it — that countdown has no other reader, so a
/// field for it would sit unread.
/// </remarks>
public sealed class IvpFrictionController(IvpFrictionSystem system) : IIvpUnitController
{
    /// <summary>The priority its slot 5 returns — <c>0x258</c>.</summary>
    public const int TangentialPriority = 600;

    /// <summary><c>DAT_1800fcfa0</c>: the step below which the reciprocal is answered as <c>1e10</c> instead.</summary>
    private const float VanishingStep = 1e-10f;

    /// <summary>The system this controller drives.</summary>
    public IvpFrictionSystem System { get; } = system ?? throw new ArgumentNullException(nameof(system));

    /// <inheritdoc/>
    public int Priority => TangentialPriority;

    /// <inheritdoc/>
    /// <remarks>**The cores are not read**: this controller's work is the system's, and the entry's cores only say which unit it
    /// belongs to.</remarks>
    public void Advance(IvpSimulationUnit unit, IReadOnlyList<IvpRigidBody> cores, float psiStep)
    {
        double inverseStep = InverseOf(psiStep);

        if (System.ContactCount <= 1)
        {
            if (System.FirstContact is { } lone)
            {
                SolveLone(lone, psiStep, inverseStep);
            }

            return;
        }

        List<IvpFrictionPair> pairs = System.Pairs;

        for (int index = pairs.Count - 1; index >= 0; index--)
        {
            IvpFrictionPair pair = pairs[index];

            if (pair.Contacts.Count == 0)
            {
                continue;
            }

            IvpTangentialSolve.SolveOncePerPair(pair, BudgetFor(pair, psiStep), inverseStep);
        }
    }

    /// <summary>A pair's friction-cone budget for this PSI — <c>FUN_1800836b0</c>'s own sum.</summary>
    /// <param name="pair">The pair.</param>
    /// <param name="psiStep">The PSI's step.</param>
    /// <returns>The budget: the summed product over its contacts, times the step squared.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pair"/> is null.</exception>
    /// <remarks>
    /// **The third factor is <see cref="IvpContactPoint.InverseContactMass"/>** (<c>cp+0x60</c>), whose writer was read on
    /// 2026-09-15; before that this sum could not be formed at all. *The native adds four contacts at a time and then the
    /// remainder, which changes the float summation order and nothing else.*
    /// </remarks>
    public static float BudgetFor(IvpFrictionPair pair, float psiStep)
    {
        ArgumentNullException.ThrowIfNull(pair);

        float sum = 0f;

        for (int index = pair.Contacts.Count - 1; index >= 0; index--)
        {
            IvpContactPoint contact = pair.Contacts[index];

            sum += contact.NormalPush * contact.Friction * contact.InverseContactMass;
        }

        return sum * psiStep * psiStep;
    }

    /// <summary>The step's reciprocal as the PSI builds it — <c>frame+0x4</c>, guarded by <c>DAT_1800fcfa0</c>.</summary>
    /// <param name="psiStep">The PSI's step.</param>
    /// <returns>The reciprocal, or <c>1e10</c> for a vanishing step.</returns>
    internal static double InverseOf(float psiStep) => psiStep <= VanishingStep ? 1e10f : 1f / psiStep;

    private static void SolveLone(IvpContactPoint contact, float psiStep, double inverseStep)
    {
        float budget = psiStep * psiStep * contact.InverseContactMass * (contact.NormalPush * contact.Friction);
        (float Span, float CrossSpan) before = contact.Slide;

        (contact.Slide, contact.SlideExcess) =
            IvpTangentialSolve.ClampSlide(before, budget, contact.Friction, contact.NormalPush, contact.SlideExcess);

        if (contact.Slide != before)
        {
            contact.FirstMeasure = true;
        }

        _ = IvpTangentialSolve.SolveContact(contact, inverseStep);
    }
}

/// <summary>
/// The friction system's normal pass — the controller it is filed as at priority <c>0</c>, whose slot 4 is
/// <c>FUN_180084320</c> (B369, D172).
/// </summary>
/// <param name="system">The friction system this controller is a face of — its base <c>+0x10</c>.</param>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): the last controller a PSI runs, and the one that spends the normal pushes.
///
/// <code>
/// one contact or none → FUN_180084490(that)   else   FUN_1800a9bf0        -- IvpFrictionSystem.SolveNormalPushes
/// no contacts left:  the controller forgets the system and it deletes itself (slot 7)
/// else, +0x80 set:  cleared;  r = FUN_1800877b0(system);  r → FUN_180086e80(system, r), r = its unit
/// either way the unit's dword loses bit 9 and gains bit 8
/// </code>
///
/// **The bits it sets are the stale-entries pair** (<see cref="IvpSimulationUnit.Flags"/>'s <c>0x300</c>), which is why a unit
/// holding a friction system rebuilds its controller entries on every PSI.
///
/// *Not carried*: the system's own deletion when its last contact goes, and the union-find split (<c>FUN_1800877b0</c> and
/// <c>FUN_180086e80</c>) — <see cref="IvpFrictionSystem.SplitCheckDue"/> is cleared here so the flag does not accumulate, and
/// the split it asks for lands with those two functions.
/// </remarks>
public sealed class IvpNormalFrictionController(IvpFrictionSystem system) : IIvpUnitController
{
    /// <summary>The priority its slot 5 returns — zero, so the PSI runs it last.</summary>
    public const int NormalPriority = 0;

    /// <summary>The system this controller drives.</summary>
    public IvpFrictionSystem System { get; } = system ?? throw new ArgumentNullException(nameof(system));

    /// <inheritdoc/>
    public int Priority => NormalPriority;

    /// <inheritdoc/>
    public void Advance(IvpSimulationUnit unit, IReadOnlyList<IvpRigidBody> cores, float psiStep)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (System.ContactCount > 0)
        {
            System.SolveNormalPushes((float)IvpFrictionController.InverseOf(psiStep));

            // `+0x80 set → cleared`, then the union-find the split needs, which is not carried.
            System.SplitCheckDue = false;
        }

        unit.Flags = (unit.Flags & ~0x200) | 0x100;
    }
}

/// <summary>
/// The friction system's record pass — the controller it is filed as at priority <c>2000</c>, whose slot 4 is
/// <c>FUN_180084240</c> (B369, D172).
/// </summary>
/// <param name="system">The friction system this controller is a face of — its base <c>+0x20</c>.</param>
/// <param name="environment">The environment, for its clock.</param>
/// <param name="sides">The contact's two ledge sides now, as the record build needs them.</param>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): the FIRST controller a PSI runs, and for a lone contact all it does is
/// rebuild the record.
///
/// <code>
/// one contact or none → FUN_18008d0c0(list head, env)      -- the record rebuilt, nothing else
/// else  FUN_180088ae0(system);  unit dword &amp; 0x3000 → every pair's +0x30 = 0;  unless &amp; 0xc00 → FUN_180086b40(system)
/// </code>
///
/// *Not carried*: the many-contact branch entirely — `FUN_180088ae0` and `FUN_180086b40` are unread, and the pairs' <c>+0x30</c>
/// this project does not hold (see <see cref="IvpFrictionPair"/>). A system with more than one contact therefore has its records
/// rebuilt by the collision path alone until those are read.
/// </remarks>
public sealed class IvpRecordFrictionController(
    IvpFrictionSystem system,
    IvpImpactEnvironment environment,
    Func<IvpContactPoint, (IvpLedgeSide First, IvpLedgeSide Second)> sides) : IIvpUnitController
{
    /// <summary>The priority its slot 5 returns — <c>0x7d0</c>, the highest of the three.</summary>
    public const int RecordPriority = 2000;

    /// <summary>The system this controller drives.</summary>
    public IvpFrictionSystem System { get; } = system ?? throw new ArgumentNullException(nameof(system));

    /// <inheritdoc/>
    public int Priority => RecordPriority;

    /// <inheritdoc/>
    public void Advance(IvpSimulationUnit unit, IReadOnlyList<IvpRigidBody> cores, float psiStep)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(sides);

        if (System.ContactCount > 1 || System.FirstContact is not { } contact)
        {
            return;
        }

        (IvpLedgeSide first, IvpLedgeSide second) = sides(contact);

        contact.Record = IvpContactRecord.Build(
            contact,
            new IvpContactBody(first, CoreOf(contact.FirstObject), contact.FirstObject.ExtraRadius),
            new IvpContactBody(second, CoreOf(contact.SecondObject), contact.SecondObject.ExtraRadius),
            environment.Now);
    }

    private static IvpRigidBody CoreOf(IvpCollisionObject collisionObject) =>
        collisionObject.Core ?? throw new InvalidOperationException("A friction contact's object has no core.");
}

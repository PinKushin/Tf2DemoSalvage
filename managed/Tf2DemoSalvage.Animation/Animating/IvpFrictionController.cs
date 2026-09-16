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
    public void Advance(IReadOnlyList<IvpRigidBody> cores, float psiStep)
    {
        double inverseStep = psiStep <= VanishingStep ? 1e10f : 1f / psiStep;

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

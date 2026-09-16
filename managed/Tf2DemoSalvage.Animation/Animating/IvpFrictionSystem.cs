using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One core's share of a friction system — the <c>0x18</c> bytes a core holds for each system it is in (B369, D172).</summary>
/// <param name="system">The system, <c>+0x10</c>.</param>
public sealed class IvpFrictionInfo(IvpFrictionSystem system)
{
    /// <summary>The system's contacts this core is in — the vector at <c>+0x0</c>, its count at <c>+0x2</c> and elements at <c>+0x8</c>.</summary>
    internal List<IvpContactPoint> Contacts { get; } = [];

    /// <summary>The system — <c>+0x10</c>.</summary>
    public IvpFrictionSystem System { get; } = system;
}

/// <summary>Two cores touching inside a friction system — the <c>0x50</c> bytes of a pair (B369, D172).</summary>
/// <param name="firstCore">The first core, <c>+0x38</c>.</param>
/// <param name="secondCore">The second core, <c>+0x40</c>.</param>
/// <remarks>*Only the fields the heap solve reads are carried; the rest of the pair is read with the filing routines.*</remarks>
public sealed class IvpFrictionPair(IvpRigidBody firstCore, IvpRigidBody secondCore)
{
    /// <summary>The first core — <c>+0x38</c>.</summary>
    public IvpRigidBody FirstCore { get; } = firstCore;

    /// <summary>The second core — <c>+0x40</c>.</summary>
    public IvpRigidBody SecondCore { get; } = secondCore;

    /// <summary>When the pair last collided — <c>+0x28</c>, written by <c>FUN_18008ef60</c> with <c>env+0x188</c>.</summary>
    public double LastImpact { get; internal set; }

    // NOT carried: `pair+0x30`, which `FUN_1800836b0` grows by the sum of what each tangential solve RETURNS — a delta against
    // a per-contact accumulator at `cp+0x84`, scaled by `DAT_1800ee388`. Neither the accumulator nor the return is ported, and a
    // field this project can only ever leave at zero would read as carried. It arrives with that return value.

    /// <summary>
    /// The contacts touching this pair — the array <c>SolveOncePerPsi</c> walks per pair (its own <c>+8</c>/count
    /// <c>+2</c>), summing each contact's <c>NormalPush × Friction × &lt;an as-yet-unnamed +0x60 factor&gt;</c> into
    /// the pair's own friction-cone budget before clamping and solving each one — see
    /// <see cref="IvpTangentialSolve.SolveOncePerPair"/>.
    /// </summary>
    /// <remarks>
    /// **Filed by <see cref="IvpFrictionLinking.LinkContactByCore"/>**, once per contact, the first time it links
    /// into this pair. **Never removed** — the native's own drop (`FUN_180088090`'s bookkeeping when a mindist
    /// stops colliding) is not ported; see `docs/HANDOFF.md`, item 3.
    /// </remarks>
    public IList<IvpContactPoint> Contacts { get; } = [];
}

/// <summary>
/// A heap of cores held apart by their contacts, solved together every PSI — the <c>0x90</c> bytes of IVP's friction system, and its
/// many-contact normal solve <c>FUN_1800a9bf0</c> (B369, D172).
/// </summary>
/// <param name="environment">The environment, <c>+0x8</c>.</param>
/// <remarks>
/// **Read from the disassembly instruction by instruction** (`docs/findings/51`, *`FUN_1800a9bf0`* and *The many-contact normal
/// solve itself*), **and pinned to the shipped `vphysics.dll` called in process** — `IvpHeapSolveConformanceTests` replays the
/// systems the `vphysics-heap-solve` probe gave the binary.
///
/// **What a solve is**: every contact's response to a unit push at every other contact sharing a moving core, the stiffness and
/// closing-speed target each must meet, the contacts pushed last time solved as an equation first, and the constraint solver when
/// that answer pulls or leaves a contact pulling. The pushes go into the cores' staged changes, which are committed unless they
/// add more energy than the heap's allowance.
///
/// *Not carried yet: the filing pass `FUN_1800a9bf0` makes between its sort and its solve — a contact dropped once its gap reaches
/// `block[0x47]` or its record is outside (`FUN_180083e40`), and one moved to the head once its gap passes
/// `block[0x46] + block[0x43]` with both friction cores flagged — which lands with the filing routines it calls.*
/// </remarks>
public sealed class IvpFrictionSystem(IvpImpactEnvironment environment)
{
    /// <summary><c>CMP [+0x7a], 0x96</c>: past this many contacts the anomaly manager is asked whether to freeze the heap.</summary>
    private const int MostContacts = 150;

    /// <summary><c>DAT_1800fcfb0</c>: <c>20.0</c>, the stiffness a record whose gap is inside the contact gap is pushed out with.</summary>
    private const double Penetrating = 20d;

    /// <summary><c>DAT_1800ea94c</c>: <c>0.01f</c>, the share of gravity below which a push is too soft to trust the sub-system.</summary>
    private const float Firmness = 0.01f;

    /// <summary><c>DAT_1800fd578</c>: <c>0.1f</c> widened, the share of each mover's friction weight the energy may grow by.</summary>
    private const double Allowance = 0.1f;

    /// <summary><c>CMP EAX, 0x90000</c>: past this many pulls in a row a streak starts again.</summary>
    private const short LongestPull = 9;

    /// <summary>The environment — <c>+0x8</c>.</summary>
    public IvpImpactEnvironment Environment { get; } = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>The head of the contact list — <c>+0x40</c>.</summary>
    public IvpContactPoint? FirstContact { get; private set; }

    /// <summary>Every core in the system — the vector at <c>+0x48</c>, count <c>+0x4a</c>, elements <c>+0x50</c>.</summary>
    internal List<IvpRigidBody> Cores { get; } = [];

    /// <summary>The movable ones — the vector at <c>+0x58</c>.</summary>
    internal List<IvpRigidBody> MovableCores { get; } = [];

    /// <summary>The pairs — the vector at <c>+0x68</c>.</summary>
    internal List<IvpFrictionPair> Pairs { get; } = [];

    /// <summary>How many contacts the list holds — the signed word at <c>+0x7a</c>.</summary>
    public short ContactCount { get; private set; }

    /// <summary>How many contacts at the head the solve leaves out — the signed word at <c>+0x7c</c>, which the solve zeroes first.</summary>
    public short LeftOut { get; private set; }

    /// <summary>Whether a pair has been deleted since the last solve, so the system may have split — the byte at <c>+0x80</c>.</summary>
    /// <remarks>
    /// **Set by <see cref="RemoveContact"/> when a pair empties** (<c>FUN_180083e40</c>: `if FUN_180088130(system, cp) == 1:
    /// system+0x80 = 1`), and read by the priority-0 controller, which clears it and runs the union-find
    /// (<c>FUN_1800877b0</c>) to see whether the system now falls into two — *that split is not carried yet*, so nothing
    /// clears this yet either. Named for what it means rather than for the offset: losing a pair is the only thing that can
    /// disconnect a system.
    /// </remarks>
    public bool SplitCheckDue { get; internal set; }

    /// <summary>
    /// Gives a core its share of the system, and adds it to <see cref="MovableCores"/> when it is not immovable —
    /// <c>FUN_180087bf0</c> plus the share it links in, <c>FUN_180076690</c>.
    /// </summary>
    /// <param name="core">The core; must not already have a share of this system.</param>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    /// <remarks>
    /// **The native's gravity-list registration is not carried**: this project's <see cref="IvpDamping"/>,
    /// <see cref="IvpPush"/> and <see cref="IvpGravity"/> already walk every body directly rather than a dynamically
    /// registered subset, so a core joining a friction system needs no separate registration for them to keep reaching it.
    /// **A movable core's share replaces <see cref="IvpRigidBody.FrictionInfo"/> outright** — the native writes
    /// <c>core+0x60</c> directly rather than merging — because a movable core belongs to exactly one system at a time.
    /// </remarks>
    internal void AddCore(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        IvpFrictionInfo share = new(this);

        Cores.Add(core);

        if (core.Immovable)
        {
            core.FrictionInfos[this] = share;
        }
        else
        {
            core.FrictionInfo = share;
            MovableCores.Add(core);
        }
    }

    /// <summary>Adds a pair to the system — <c>FUN_1800833d0</c>'s call site inside <c>FUN_180088090</c>.</summary>
    /// <param name="pair">The pair.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pair"/> is null.</exception>
    internal void AddPair(IvpFrictionPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        Pairs.Add(pair);
    }

    /// <summary>The system's existing pair for two cores, in either order, or null — <c>FUN_1800850b0</c>.</summary>
    /// <param name="first">A core.</param>
    /// <param name="second">The other core.</param>
    /// <returns>The pair, or null when the two have none yet.</returns>
    internal IvpFrictionPair? PairFor(IvpRigidBody first, IvpRigidBody second)
    {
        for (int index = Pairs.Count - 1; index >= 0; index--)
        {
            IvpFrictionPair candidate = Pairs[index];

            if ((ReferenceEquals(candidate.FirstCore, first) && ReferenceEquals(candidate.SecondCore, second)) ||
                (ReferenceEquals(candidate.FirstCore, second) && ReferenceEquals(candidate.SecondCore, first)))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Removes a contact from this system entirely — <c>FUN_180083e40(system, cp)</c>.</summary>
    /// <param name="contact">The contact to remove.</param>
    /// <param name="firstCore">The contact's first physical core.</param>
    /// <param name="secondCore">Its second.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>, which both cores' anchors are reset to.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The cores have no pair, or a core no share of this system.</exception>
    /// <remarks>
    /// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point*), in this order and no other:
    ///
    /// <code>
    /// FUN_180078820(core0); FUN_180078820(core1)   -- core+0x200 = core+0x208 = env+0x188
    /// FUN_180088ce0(system, cp)                    -- off the system's list
    /// if FUN_180088130(system, cp) == 1:  system+0x80 = 1   -- the pair emptied and was deleted
    /// info0, info1 = FUN_180077f00(core, system);  each loses cp, and an emptied share takes its core out
    /// FUN_180083210(cp);  free(cp, 0xd0)
    /// </code>
    ///
    /// **The anchor reset comes first and it is not incidental**: both cores are told they moved now, so the rest test
    /// (<see cref="IvpRigidBody.TestRest"/>) cannot call a core settled on the strength of an anchor older than the
    /// contact that has just gone.
    ///
    /// **The destructor is not carried.** `FUN_180083210` releases each synapse's ledge through its surface manager and
    /// tells the environment's listeners; this port holds neither, and the contact is simply dropped for collection.
    /// </remarks>
    internal void RemoveContact(
        IvpContactPoint contact, IvpRigidBody firstCore, IvpRigidBody secondCore, double now)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(firstCore);
        ArgumentNullException.ThrowIfNull(secondCore);

        // `FUN_180078820(core)` on each, before anything is unlinked.
        firstCore.RestAnchorTime = now;
        firstCore.SettleAnchorTime = now;
        secondCore.RestAnchorTime = now;
        secondCore.SettleAnchorTime = now;

        Unlink(contact);

        if (RemoveFromPair(contact, firstCore, secondCore))
        {
            SplitCheckDue = true;
        }

        RemoveCoreContact(contact, firstCore);
        RemoveCoreContact(contact, secondCore);
    }

    /// <summary>
    /// Measures a pair's contacts again and removes the ones now outside their features — <c>FUN_180083b30(pair, system)</c>.
    /// </summary>
    /// <param name="pair">The pair, in this system.</param>
    /// <param name="sides">Each contact's two ledge sides where their objects are now — the cache objects the builder refreshes.</param>
    /// <param name="materials">The material manager <see cref="IvpContactPoint.SetMaterials"/> reads.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <param name="skip">A contact left alone — the collided one, when the island build runs this inline.</param>
    /// <returns>How many contacts the pair has left.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// for i = pair+0x2 − 1 down to 0:  cp = pair+0x8[i]
    ///     FUN_18008d0c0(cp, pair+0x38 core+0x10);  FUN_1800908d0(cp)
    ///     if record+0x76 == 1:  FUN_180083e40 inline
    /// return pair+0x2
    /// </code>
    /// **Last to first**, so a removal never shifts a contact not yet visited. *The side lookup is a parameter because the
    /// cache objects (`core+0x10`) are not carried yet*; the collision path hands the builder its sides the same way.
    /// </remarks>
    internal short RevalidatePair(
        IvpFrictionPair pair,
        Func<IvpContactPoint, (IvpLedgeSide First, IvpLedgeSide Second)> sides,
        IIvpMaterialManager materials,
        double now,
        IvpContactPoint? skip = null)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(sides);
        ArgumentNullException.ThrowIfNull(materials);

        for (int index = pair.Contacts.Count - 1; index >= 0; index--)
        {
            IvpContactPoint contact = pair.Contacts[index];

            // The island build's inline copy (`FUN_180090700`) walks every contact of the pair but the one that collided.
            if (ReferenceEquals(contact, skip))
            {
                continue;
            }

            (IvpLedgeSide first, IvpLedgeSide second) = sides(contact);

            IvpContactRecord record = IvpContactRecord.Build(
                contact,
                new IvpContactBody(first, CoreOf(contact.FirstObject), contact.FirstObject.ExtraRadius),
                new IvpContactBody(second, CoreOf(contact.SecondObject), contact.SecondObject.ExtraRadius),
                now);
            contact.SetMaterials(materials);

            if (record.Outside)
            {
                RemoveContact(contact, pair.FirstCore, pair.SecondCore, now);
            }
        }

        return (short)pair.Contacts.Count;

        static IvpRigidBody CoreOf(IvpCollisionObject collisionObject) =>
            collisionObject.Core ?? throw new InvalidOperationException("A friction contact's object has no core.");
    }

    /// <summary>
    /// Takes a contact off its pair and deletes the pair when it empties — <c>FUN_180088130(system, cp)</c>.
    /// </summary>
    /// <param name="contact">The contact being removed.</param>
    /// <param name="firstCore">One of the contact's two physical cores.</param>
    /// <param name="secondCore">The other.</param>
    /// <returns><c>true</c> when the pair emptied and was removed, <c>false</c> when contacts remain on it.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The two cores have no pair — the native asserts here (line <c>0x299</c>).</exception>
    /// <remarks>
    /// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point*): the pair of the two cores is
    /// found in either order (<see cref="PairFor"/>, <c>FUN_1800863f0</c>), the contact is taken off its contact vector
    /// (<c>FUN_180083da0</c>, an ordered removal), and when none remain — <c>FUN_180086b30</c> is that vector's count —
    /// the pair leaves the system (<c>FUN_180083db0</c>) and is freed.
    ///
    /// **The environment listeners `FUN_180083db0` tells (`FUN_180081f70`) are not carried**, for the same reason none of
    /// this port's listeners are: nothing in a corpse's own simulation subscribes. Cores are passed in rather than read
    /// off the contact's objects, matching <see cref="IvpFrictionLinking.LinkContactByCore"/> — this project models the
    /// object-to-core link nowhere else.
    /// </remarks>
    internal bool RemoveFromPair(IvpContactPoint contact, IvpRigidBody firstCore, IvpRigidBody secondCore)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(firstCore);
        ArgumentNullException.ThrowIfNull(secondCore);

        IvpFrictionPair pair = PairFor(firstCore, secondCore)
            ?? throw new InvalidOperationException(
                "The two cores have no pair in this system; the native asserts (FUN_1800863f0, line 0x299).");

        pair.Contacts.Remove(contact);

        if (pair.Contacts.Count == 0)
        {
            Pairs.Remove(pair);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Takes a contact out of one core's share of this system, dropping the share and the core when it empties —
    /// <c>FUN_180075130</c> then <c>FUN_180077c10</c>/<c>FUN_180088c80</c> inside <c>FUN_180083e40</c>.
    /// </summary>
    /// <param name="contact">The contact being removed.</param>
    /// <param name="core">One of the contact's two physical cores.</param>
    /// <returns><c>true</c> when the core's share emptied and the core left the system, <c>false</c> when contacts remain.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The core has no share of this system — <c>FUN_180077f00</c> returned null.</exception>
    /// <remarks>
    /// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point* and *What a core keeps per system*): the
    /// core's share is found (<see cref="IvpRigidBody.FrictionInfoIn"/>, <c>FUN_180077f00</c>), the contact leaves its ordered
    /// contact vector (<c>FUN_180075130</c>), and an emptied share is detached from the core (<c>FUN_180077c10</c>) as the core
    /// leaves the system (<c>FUN_180088c80</c>).
    ///
    /// **The inverse of <see cref="AddCore"/>, and no more.** `FUN_180088c80` also unlinks the system's three controller bases
    /// from the core's simulation unit and clears bit 9 / sets bit 8 of that unit's dword; <see cref="AddCore"/> models none of
    /// that — this project reaches gravity, damping and push by walking every body, not through a registered unit — so the
    /// removal has nothing to undo there either.
    /// </remarks>
    internal bool RemoveCoreContact(IvpContactPoint contact, IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(core);

        IvpFrictionInfo info = core.FrictionInfoIn(this)
            ?? throw new InvalidOperationException(
                "The core has no share of this system; FUN_180077f00 returned null where the removal dereferences it.");

        info.Contacts.Remove(contact);

        if (info.Contacts.Count != 0)
        {
            return false;
        }

        Cores.Remove(core);

        if (core.Immovable)
        {
            core.FrictionInfos.Remove(this);
        }
        else
        {
            core.FrictionInfo = null;
            MovableCores.Remove(core);
        }

        return true;
    }

    /// <summary>A contact filed at the head of the list — <c>FUN_180087c90(system, cp)</c>.</summary>
    /// <param name="point">The contact.</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is null.</exception>
    internal void Link(IvpContactPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        point.FrictionSystem = this;
        point.Next = FirstContact;
        point.Previous = null;

        if (FirstContact is not null)
        {
            FirstContact.Previous = point;
        }

        FirstContact = point;
        ContactCount++;
    }

    /// <summary>A contact taken out of the list — <c>FUN_180088ce0(system, cp)</c>, the inverse of <see cref="Link"/>.</summary>
    /// <param name="point">The contact; must be filed in this system's list.</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is null.</exception>
    /// <remarks>
    /// **Read from the disassembly** (`docs/findings/51`, *Removing a contact point*): `cp+0x0` is the next link,
    /// `cp+0x8` the previous, the head is `system+0x40`, and `system+0x7a` (the contact count) drops by one. The
    /// removed contact's own links are cleared so a freed record cannot be walked back into.
    /// </remarks>
    internal void Unlink(IvpContactPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (point.Previous is { } before)
        {
            before.Next = point.Next;
        }
        else
        {
            FirstContact = point.Next;
        }

        if (point.Next is { } after)
        {
            after.Previous = point.Previous;
        }

        point.Next = null;
        point.Previous = null;
        point.FrictionSystem = null;
        ContactCount--;
    }

    /// <summary>The list insertion-sorted by push streak, most pushed first — <c>FUN_1800a9bf0</c>'s first loop.</summary>
    /// <remarks>
    /// **Keyed on `cp+0x90 &amp; 0xffff0000` compared signed, which is the streak word alone**, and a contact moves back only past
    /// a strictly greater key, so equal streaks keep their order. The links are exchanged in place, the head moved with them.
    /// </remarks>
    internal void SortContacts()
    {
        IvpContactPoint? previous = FirstContact;
        IvpContactPoint? current = previous?.Next;

        while (previous is not null && current is not null)
        {
            if (previous.PushStreak > current.PushStreak)
            {
                MoveBefore(previous, current);

                while (current.Previous is { } before && before.PushStreak > current.PushStreak)
                {
                    MoveBefore(before, current);
                }

                current = previous;
            }

            previous = current;
            current = current.Next;
        }
    }

    /// <summary>The normal pushes, every PSI at priority 0 — <c>FUN_180084320</c>, slot 4 of the controller base at <c>+0x10</c>.</summary>
    /// <param name="inverseStep">The PSI event's float at <c>+0x4</c>; see <see cref="SolveHeap"/>.</param>
    /// <exception cref="InvalidOperationException">The list is empty, or a contact has no record, where the binary dereferences the null.</exception>
    /// <remarks>
    /// <code>
    /// +0x7a ≤ 1 → FUN_180084490 (the lone contact)   else FUN_1800a9bf0 (the heap)
    /// +0x7a == 0 → the controller forgets the system, which deletes itself (slot 7);  the event's unit: bit 9 cleared, bit 8 set
    /// else +0x80 set → cleared;  r = FUN_1800877b0;  r → FUN_180086e80 (the split), and r's unit: bit 9 cleared, bit 8 set
    /// </code>
    /// *Not carried yet: the deletion and the split, which land with the filing routines and the simulation units.*
    /// </remarks>
    internal void SolveNormalPushes(float inverseStep)
    {
        if (ContactCount <= 1)
        {
            SolveOne(inverseStep);
            return;
        }

        SolveHeap(inverseStep);
    }

    /// <summary>The heap solved — <c>FUN_1800a9bf0</c>: the sort, then everything from its zeroing of <c>+0x7c</c> on.</summary>
    /// <param name="inverseStep">
    /// The PSI event's float at <c>+0x4</c>, which each contact's push is multiplied by: the environment's inverse step narrowed,
    /// <c>(float)env+0x110</c>, as the unit manager's PSI <c>FUN_180075a90</c> builds the event.
    /// </param>
    /// <exception cref="InvalidOperationException">A contact has no record, or an object no core, where the binary dereferences the null.</exception>
    /// <remarks>
    /// <code>
    /// +0x7c = 0
    /// more than 150 contacts and anomaly slot 5 answers for the cores → every movable core, last first: bit 0 set, and when it is in
    ///     more than one pair with no immovable core, velocity and spin zeroed; return
    /// every contact in list order: record+0x70 = its row (−1 inside +0x7c);  +0x78, +0x80 = the objects' cores' shares;
    ///     +0x8c = the gap;  +0x88 = (int)streak;  the record appended to the solver's rows
    /// FUN_1800a9520 builds the system and names the active rows;  FUN_1800aa5c0 solves it
    /// </code>
    /// **The shares are looked up on the objects' PHYSICAL cores (`+0xe8`)**, where the filing pass reads their friction cores.
    /// </remarks>
    internal void SolveHeap(float inverseStep)
    {
        SortContacts();
        LeftOut = 0;

        if (ContactCount > MostContacts && Environment.Anomalies.MaximumContactsExceeded(Cores))
        {
            Freeze();
            return;
        }

        int size = ContactCount - LeftOut;
        IvpLinearSystem system = new(size, size);
        List<IvpContactRecord> records = [];
        int counted = 0;

        for (IvpContactPoint? point = FirstContact; point is not null; point = point.Next)
        {
            IvpContactRecord record = point.Record ?? throw new InvalidOperationException("A filed contact has no record.");

            if (counted < LeftOut)
            {
                record.Index = -1;
                continue;
            }

            counted++;
            record.FirstFrictionInfo = CoreOf(point.FirstObject).FrictionInfoIn(this);
            record.SecondFrictionInfo = CoreOf(point.SecondObject).FrictionInfoIn(this);
            record.SolveGap = point.Gap;
            record.SolvePushStreak = point.PushStreak;
            records.Add(record);
            record.Index = (short)(records.Count - 1);
        }

        int[] active = new int[ContactCount];
        int activeCount = Build(system, records, active);

        SolveNormals(system, records, active, activeCount, inverseStep);
    }

    /// <summary>The lone contact's push — <c>FUN_180084490(system+0x10, event)</c>, the writer of <c>cp+0x88</c> when a system has one.</summary>
    /// <remarks>
    /// <code>
    /// s = (d)((n.y·v.y + n.x·v.x) + n.z·v.z) + (d)((t.y·ω.y + t.x·ω.x) + t.z·ω.z)             first core, the normal term first
    /// s = s + ((d)−((n.y·v.y + n.x·v.x) + n.z·v.z) − (d)((t′.y·ω.y + t′.x·ω.x) + t′.z·ω.z))    second core
    /// g = (d)(block[0x43] − cp+0x8c);  f = (((g ≥ 0 ? 1.0 : 20.0)·g) + s)·(d)record+0x90
    /// f > 0 → cp+0x88 = (f)((d)event+0x4 · f);  FUN_180083420(record, f)      else cp+0x88 = 0
    /// !(block[0x47] > cp+0x8c) or record+0x76 == 1 → FUN_180083e40(system, cp)                  COMISS/JBE: a NaN gap drops
    /// </code>
    /// **Its closing speed adds the first core's normal term to its turn term, where the heap's matrix build adds the turn term
    /// to the normal term**, and it reads the contact's own gap, not a record copy. *Not carried yet: the drop, which lands with
    /// the filing routines.*
    /// </remarks>
    private void SolveOne(float inverseStep)
    {
        IvpContactPoint point = FirstContact ?? throw new InvalidOperationException("A friction system with no contacts was solved.");
        IvpContactRecord record = point.Record ?? throw new InvalidOperationException("A filed contact has no record.");
        (float X, float Y, float Z) n = record.Normal;
        double closing = 0d;

        if (record.FirstCore is { } first)
        {
            float moving = Dot(n, first.Velocity);
            float turning = Dot(record.FirstTurn, first.AngularVelocity);

            closing = IvpMath.Addsd(moving, turning);
        }

        if (record.SecondCore is { } second)
        {
            float moving = Dot(n, second.Velocity);
            float turning = Dot(record.SecondTurn, second.AngularVelocity);

            closing = IvpMath.Addsd(closing, (double)(-moving) - turning);
        }

        double gap = IvpCollisionTolerance.ContactGap - point.Gap;
        double stiffness = gap >= 0d ? 1d : Penetrating;
        double push = IvpMath.Mulsd(IvpMath.Addsd(IvpMath.Mulsd(stiffness, gap), closing), record.VirtualMass);

        if (push > 0d)
        {
            point.NormalPush = (float)IvpMath.Mulsd(inverseStep, push);
            record.Apply(push);
            return;
        }

        point.NormalPush = 0f;
    }

    /// <summary>Two neighbours exchanged, <paramref name="after"/> moving ahead of <paramref name="before"/>.</summary>
    private void MoveBefore(IvpContactPoint before, IvpContactPoint after)
    {
        if (ReferenceEquals(FirstContact, before))
        {
            FirstContact = after;
        }

        before.Previous?.Next = after;
        after.Next?.Previous = before;

        before.Next = after.Next;
        after.Previous = before.Previous;
        before.Previous = after;
        after.Next = before;
    }

    private static IvpRigidBody CoreOf(IvpCollisionObject collisionObject) =>
        collisionObject.Core ?? throw new InvalidOperationException("A contact's object has no core.");

    /// <summary>The heap frozen instead of solved — <c>FUN_1800a9bf0</c>'s branch past 150 contacts.</summary>
    private void Freeze()
    {
        for (int index = MovableCores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = MovableCores[index];
            int pairs = 0;

            core.FlagBit0 = true;

            for (int pair = Pairs.Count - 1; pair >= 0; pair--)
            {
                IvpFrictionPair candidate = Pairs[pair];

                if ((ReferenceEquals(candidate.FirstCore, core) || ReferenceEquals(candidate.SecondCore, core)) &&
                    !candidate.SecondCore.Immovable && !candidate.FirstCore.Immovable)
                {
                    pairs++;
                }
            }

            if (pairs > 1)
            {
                core.Velocity = (0f, 0f, 0f);
                core.AngularVelocity = (0f, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// Each record's response to a unit push at every other record sharing a moving core, and what each must meet — <c>FUN_1800a9520</c>.
    /// </summary>
    /// <returns>How many rows are active: those whose contact was pushed or pulled last time.</returns>
    /// <remarks>
    /// <code>
    /// s = (d)((t.y·ω.y + t.x·ω.x) + t.z·ω.z) + (d)((n.y·v.y + n.x·v.x) + n.z·v.z)           first core, record lanes first
    /// s = s + ((d)−((n.y·v.y + n.x·v.x) + n.z·v.z) − (d)((t′.y·ω.y + t′.x·ω.x) + t′.z·ω.z))    second core
    /// g = (d)(block[0x43] − gap);  rhs = (g ≥ 0 ? 1.0 : 20.0)·g + s
    /// first core:   n′ = (f)((d)n·(d)−m⁻¹),  u = t ⊙ I⁻¹;   second: n′ = (f)((d)n·(d)m⁻¹),  u = t′ ⊙ I⁻¹
    ///     each contact R of the core's share with a row:  σ = R's first core is this one ? −1 : 1;  that side of R named
    ///     M[R, i] = (((d)((n′.y·Rn.y + n′.x·Rn.x) + n′.z·Rn.z) ∓ (d)((u.y·τ.y + u.x·τ.x) + u.z·τ.z))·σ) + M[R, i]
    /// </code>
    /// The first core takes the turn term away, the second adds it. *The binary then asserts that every row matches its contact's
    /// place in the list (line `0x3a6`), which the loop in <see cref="SolveHeap"/> guarantees; it is not carried.*
    /// </remarks>
    private static int Build(IvpLinearSystem system, List<IvpContactRecord> records, int[] active)
    {
        Array.Clear(system.Values, 0, system.Rows * system.Columns);

        int count = 0;

        for (int row = 0; row < records.Count; row++)
        {
            IvpContactRecord record = records[row];
            (float X, float Y, float Z) n = record.Normal;
            double closing = 0d;

            if (record.FirstCore is { } first)
            {
                float turning = Dot(record.FirstTurn, first.AngularVelocity);
                float moving = Dot(n, first.Velocity);

                closing = IvpMath.Addsd(turning, moving);
            }

            if (record.SecondCore is { } second)
            {
                float moving = Dot(n, second.Velocity);
                float turning = Dot(record.SecondTurn, second.AngularVelocity);

                closing = IvpMath.Addsd(closing, (double)(-moving) - turning);
            }

            double gap = IvpCollisionTolerance.ContactGap - record.SolveGap;
            double stiffness = gap >= 0d ? 1d : Penetrating;

            system.RightHandSide[row] = IvpMath.Addsd(IvpMath.Mulsd(stiffness, gap), closing);

            if (record.SolvePushStreak != 0)
            {
                active[count++] = row;
            }

            if (record.FirstCore is { } pushedFirst)
            {
                Couple(system, records, record.FirstFrictionInfo, pushedFirst, row, Response(n, record.FirstTurn, pushedFirst, -pushedFirst.InverseMass), subtractTurn: true);
            }

            if (record.SecondCore is { } pushedSecond)
            {
                Couple(system, records, record.SecondFrictionInfo, pushedSecond, row, Response(n, record.SecondTurn, pushedSecond, pushedSecond.InverseMass), subtractTurn: false);
            }
        }

        return count;
    }

    /// <summary>A core's response to a unit push along a normal: the normal times the widened share, narrowed, and the turn through the inverse inertia.</summary>
    private static ((float X, float Y, float Z) Push, (float X, float Y, float Z) Turn) Response(
        (float X, float Y, float Z) normal, (float X, float Y, float Z) turn, IvpRigidBody core, double share) =>
        (((float)IvpMath.Mulsd(normal.X, share), (float)IvpMath.Mulsd(normal.Y, share), (float)IvpMath.Mulsd(normal.Z, share)),
         (IvpMath.Mulss(turn.X, core.InverseInertia.X), IvpMath.Mulss(turn.Y, core.InverseInertia.Y), IvpMath.Mulss(turn.Z, core.InverseInertia.Z)));

    /// <summary>One core's response written into the column of the record pushing it, at the row of every record that shares the core.</summary>
    private static void Couple(
        IvpLinearSystem system,
        List<IvpContactRecord> records,
        IvpFrictionInfo? share,
        IvpRigidBody core,
        int column,
        ((float X, float Y, float Z) Push, (float X, float Y, float Z) Turn) response,
        bool subtractTurn)
    {
        List<IvpContactPoint> contacts = (share ?? throw new InvalidOperationException("A pushed core has no share of the system.")).Contacts;

        for (int index = 0; index < contacts.Count; index++)
        {
            IvpContactRecord touching = contacts[index].Record ?? throw new InvalidOperationException("A filed contact has no record.");
            int row = touching.Index;

            if (row < 0)
            {
                continue;
            }

            IvpContactRecord other = records[row];
            bool firstSide = ReferenceEquals(other.FirstCore, core);
            double sign = firstSide ? -1d : 1d;

            if ((firstSide ? other.FirstCore : other.SecondCore) is null)
            {
                continue;
            }

            float along = Dot(response.Push, other.Normal);
            float twist = Dot(response.Turn, firstSide ? other.FirstTurn : other.SecondTurn);
            double coupling = subtractTurn ? (double)along - twist : IvpMath.Addsd(along, twist);
            int at = (row * system.Columns) + column;

            system.Values[at] = IvpMath.Addsd(IvpMath.Mulsd(coupling, sign), system.Values[at]);
        }
    }

    /// <summary><c>(a.y·b.y + a.x·b.x) + a.z·b.z</c> in float, <paramref name="a"/>'s lanes each product's destination.</summary>
    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        IvpMath.Addss(IvpMath.Addss(IvpMath.Mulss(a.Y, b.Y), IvpMath.Mulss(a.X, b.X)), IvpMath.Mulss(a.Z, b.Z));

    /// <summary>The system solved and the pushes applied — <c>FUN_1800aa5c0(solver, system, active, m, arena)</c>.</summary>
    /// <remarks>
    /// <code>
    /// scale = equilibrate;  result zeroed;  the active rows gathered and eliminated (FUN_1800a80a0)
    /// solved and FUN_1800aa9f0 holds → the result
    /// otherwise k = how many contacts lead the list with a negative streak;  the constraint solver warm on k, or return
    /// result[i] = scale·result[i];  before = FUN_1800aa1a0
    /// each contact, row i:  x > 0 → streak = streak ≥ 0 ? −1 : streak − 1
    ///                       x == 0 or NaN → streak = 0, x = 0, no push
    ///                       x &lt; 0 → streak = max(streak, 0) + 1, past 9 → 0
    ///                       x ≠ 0 → record push (FUN_1800a9280), x = MAXSD(x, 0)
    ///     cp+0x88 = (f)((d)event+0x4 · x)
    /// after = FUN_1800aa1a0;  after > FUN_1800aa010 + before → every movable core's staged changes dropped, else committed
    /// </code>
    /// </remarks>
    private void SolveNormals(IvpLinearSystem system, List<IvpContactRecord> records, int[] active, int activeCount, float inverseStep)
    {
        double scale = system.Equilibrate();
        IvpLinearSystem sub = new(activeCount, activeCount);

        Array.Clear(system.Result, 0, system.Columns);
        sub.Gather(system, active, activeCount);

        if (!(sub.Solve() && Holds(system, sub.Result, active, activeCount, scale, records)))
        {
            int warm = 0;

            for (IvpContactPoint? point = FirstContact; point is not null && point.PushStreak < 0; point = point.Next)
            {
                warm++;
            }

            if (!new IvpComplementaritySolver(system.Values, system.RightHandSide, system.Result, system.Rows).Solve(warm))
            {
                return;
            }
        }

        for (int row = system.Rows - 1; row >= 0; row--)
        {
            system.Result[row] = IvpMath.Mulsd(scale, system.Result[row]);
        }

        double before = KineticEnergy();
        int index = 0;

        for (IvpContactPoint? point = FirstContact; point is not null; point = point.Next, index++)
        {
            double push = index < LeftOut ? 0d : Streak(point, system.Result[index]);

            point.NormalPush = (float)IvpMath.Mulsd(inverseStep, push);
        }

        double after = KineticEnergy();

        if (after > IvpMath.Addsd(EnergyAllowance(), before))
        {
            for (int core = MovableCores.Count - 1; core >= 0; core--)
            {
                IvpPush.Drop(MovableCores[core]);
            }

            return;
        }

        for (int core = MovableCores.Count - 1; core >= 0; core--)
        {
            IvpPush.Flush(MovableCores[core]);
        }
    }

    /// <summary>One contact's streak moved by its push, and the push applied — the body of <c>FUN_1800aa5c0</c>'s last loop.</summary>
    /// <returns>The push as the contact records it: zero for none or a pull.</returns>
    private double Streak(IvpContactPoint point, double push)
    {
        if (push > 0d)
        {
            point.PushStreak = point.PushStreak >= 0 ? (short)-1 : unchecked((short)(point.PushStreak - 1));
        }
        else if (!(push < 0d))
        {
            point.PushStreak = 0;

            return 0d;
        }
        else
        {
            if (point.PushStreak < 0)
            {
                point.PushStreak = 0;
            }

            point.PushStreak++;

            if (point.PushStreak > LongestPull)
            {
                point.PushStreak = 0;
            }
        }

        IvpContactRecord record = point.Record ?? throw new InvalidOperationException("A filed contact has no record.");

        record.Push(push, Environment.Limits, Environment.InverseStep);

        return IvpLinearSystem.Maxsd(push, 0d);
    }

    /// <summary>Whether the active rows' answer holds for the whole system — <c>FUN_1800aa9f0(solver, x, active, m, arena)</c>.</summary>
    /// <remarks>
    /// <code>
    /// every active row j, in order:  result[j] = x;  (d)(f)(env+0x138 · 0.01f) > (x·scale)·(d)record+0x94 → too soft
    /// every inactive row:  FUN_1800a7270 fails → 0
    /// return not too soft
    /// </code>
    /// **A NaN is never too soft** (`COMISD`/`CMOVA`).
    /// </remarks>
    private bool Holds(IvpLinearSystem system, double[] pushes, int[] active, int activeCount, double scale, List<IvpContactRecord> records)
    {
        bool[] taken = new bool[system.Rows];
        bool soft = false;

        for (int index = 0; index < activeCount; index++)
        {
            int row = active[index];
            double push = pushes[index];
            double firm = IvpMath.Mulss(Environment.GravityLength, Firmness);

            taken[row] = true;
            system.Result[row] = push;

            if (firm > IvpMath.Mulsd(IvpMath.Mulsd(push, scale), records[row].InverseMass))
            {
                soft = true;
            }
        }

        for (int row = 0; row < system.Rows; row++)
        {
            if (!taken[row] && !system.Holds(row))
            {
                return false;
            }
        }

        return !soft;
    }

    /// <summary>
    /// The heap's kinetic energy with every staged change applied — <c>FUN_1800aa1a0(system)</c>: each core, last first, through
    /// <see cref="IvpRigidBody.KineticEnergy"/>.
    /// </summary>
    /// <remarks>The velocity's <c>x</c> lane adds the real to the staged, its <c>y</c> and <c>z</c> the staged to the real; every spin lane the staged to the real.</remarks>
    private double KineticEnergy()
    {
        double sum = 0d;

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = Cores[index];
            (float X, float Y, float Z) velocity = (
                IvpMath.Addss(core.PendingVelocity.X, core.Velocity.X),
                IvpMath.Addss(core.Velocity.Y, core.PendingVelocity.Y),
                IvpMath.Addss(core.Velocity.Z, core.PendingVelocity.Z));
            (float X, float Y, float Z) spin = (
                IvpMath.Addss(core.AngularVelocity.X, core.PendingAngularVelocity.X),
                IvpMath.Addss(core.AngularVelocity.Y, core.PendingAngularVelocity.Y),
                IvpMath.Addss(core.AngularVelocity.Z, core.PendingAngularVelocity.Z));

            sum = IvpMath.Addsd(sum, core.KineticEnergy(velocity, spin));
        }

        return sum;
    }

    /// <summary>How much energy a solve may add — <c>FUN_1800aa010(system)</c>.</summary>
    /// <remarks>
    /// <code>
    /// Σ over the cores not flagged 2, last first:  (d)(c · env+0x138) · (d)0.1f,   c = MINSS(MAXSS(mass, least), least)
    /// </code>
    /// **Clamped from both sides against the same value**, the minimum friction mass `limits+0x1c`, so `c` is that value whatever
    /// the mass — the two instructions are carried as written.
    /// </remarks>
    private double EnergyAllowance()
    {
        float least = Environment.Limits.MinimumFrictionMass;
        double sum = 0d;

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = Cores[index];

            if (core.Immovable)
            {
                continue;
            }

            float floored = core.Mass > least ? core.Mass : least;
            float weight = floored < least ? floored : least;

            sum = IvpMath.Addsd(sum, IvpMath.Mulsd(IvpMath.Mulss(weight, Environment.GravityLength), Allowance));
        }

        return sum;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// A deep copy of an object graph that keeps its shape: an object reached twice is copied once, so two references that
/// named one object name one copy (D217).
/// </summary>
/// <remarks>
/// **Why one generic copier rather than a <c>Clone</c> on every type.** The timeline builder's in-place state is some
/// twenty types — the entity table and every <c>EntityState</c>, the decoder's baselines, the feeds, every
/// <c>ScenePropTrack</c> and its histories — and they reference each other: a track sits in a dictionary by slot AND in
/// the list of every track. Hand-written clones would each have to agree on that sharing, and a field added later would
/// be silently left out of one. Copying by reflection copies every field there is, and the identity map keeps the
/// sharing. What it must NOT copy is said by the caller (the <c>share</c> set) and by <see cref="Immutable"/>.
///
/// **Held by a test of the whole thing, not by inspection:** a builder snapshotted mid-demo and resumed must produce
/// the timeline a straight build produces (<c>TimelineBuilderSnapshotTests</c>).
/// </remarks>
internal static class StateCopy
{
    /// <summary>Copies a graph.</summary>
    /// <typeparam name="T">The root's type.</typeparam>
    /// <param name="source">The root.</param>
    /// <param name="share">Instances handed over as they are rather than copied.</param>
    /// <returns>The copy.</returns>
    public static T Of<T>(T source, IReadOnlySet<object> share)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(share);

        return Of(source, share, prior: null, out _);
    }

    /// <summary>
    /// Copies a graph, reusing any object of an EARLIER copy whose original has not changed since (D217 step 3).
    /// </summary>
    /// <typeparam name="T">The root's type.</typeparam>
    /// <param name="source">The root.</param>
    /// <param name="share">Instances handed over as they are rather than copied.</param>
    /// <param name="prior">The earlier copy's map from original to copy, or null for a full copy.</param>
    /// <param name="copies">This copy's map, to pass as <paramref name="prior"/> next time.</param>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// **What makes checkpoints affordable**: consecutive checkpoints of one pass differ in a small part of their state —
    /// the decoder's baselines and string tables barely move — so each shares everything unchanged with the one before.
    /// An earlier copy is reused only when its original is still equal to it, field for field, floats bit for bit, and
    /// every reference in it still names the copy of the same original — so the shape of the graph is kept too. Copies
    /// are never written after they are made (a resume copies its checkpoint again), which is what makes sharing them
    /// between checkpoints safe.
    /// </remarks>
    /// <param name="created">When given, filled with what the copy CREATED rather than reused, by type — the diagnostic for
    /// what a checkpoint costs beyond the one before.</param>
    /// <param name="freeze">Whether to freeze every <see cref="PropertyBag"/> in the source first, so this copy and every
    /// copy of it share them (D217 step 4); a checkpoint does, nothing else.</param>
    public static T Of<T>(
        T source,
        IReadOnlySet<object> share,
        Dictionary<object, object>? prior,
        out Dictionary<object, object> copies,
        Dictionary<Type, (long Objects, long Elements)>? created = null,
        bool freeze = false)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(share);

        copies = new(ReferenceEqualityComparer.Instance);
        Reuse reuse = new(share, prior, created) { Freeze = freeze };

        T copy = (T)Copy(source, share, copies, reuse)!;
        reuse.MapReused(copies);
        return copy;
    }

    /// <summary>Whether an original still equals its earlier copy, memoised over one copy.</summary>
    private sealed class Reuse(IReadOnlySet<object> share, Dictionary<object, object>? prior, Dictionary<Type, (long Objects, long Elements)>? created)
    {
        private readonly Dictionary<object, bool> _same = new(ReferenceEqualityComparer.Instance);

        /// <summary>The originals being compared now, each with its depth in the comparison.</summary>
        private readonly Dictionary<object, int> _comparing = new(ReferenceEqualityComparer.Instance);

        /// <summary>Originals found equal only while an assumption above them is still open.</summary>
        private readonly List<object> _pending = [];

        /// <summary>The shallowest open assumption the comparison in progress has leaned on.</summary>
        private int _leanedOn = int.MaxValue;

        /// <summary>Whether the copy freezes every <see cref="PropertyBag"/> it reaches, in the ORIGINAL, before copying it.</summary>
        public bool Freeze { get; init; }

        /// <summary>Counts a NEW object (an array counts its elements), when the caller asked what a copy creates.</summary>
        public void Created(Type type, long elements)
        {
            if (created is not null)
            {
                (long objects, long count) = created.GetValueOrDefault(type);
                created[type] = (objects + 1, count + elements);
            }
        }

        /// <summary>The earlier copy of <paramref name="original"/>, when it can stand for a copy made now.</summary>
        public object? Unchanged(object original) =>
            prior is not null && prior.TryGetValue(original, out object? earlier) && Same(original, earlier) ? earlier : null;

        /// <summary>
        /// Adds every original found unchanged to this copy's map — the descendants of a reused object were never visited
        /// by the copy, and without them the NEXT copy could not recognise them and would copy them again.
        /// </summary>
        public void MapReused(Dictionary<object, object> copies)
        {
            foreach ((object original, bool same) in _same)
            {
                if (same && !copies.ContainsKey(original))
                {
                    copies[original] = prior![original];
                }
            }
        }

        /// <remarks>
        /// **A cycle is judged as one** (Tarjan's lowlink, applied to equality). An object being compared is assumed equal
        /// when a cycle leads back to it, so the recursion ends — but an object judged equal only BECAUSE of that
        /// assumption is not known equal until the assumption is settled: if the object the cycle leads back to turns out
        /// changed, reusing the other's earlier copy would close the copy's cycle on its stale copy. So such a judgement is
        /// held pending, and memoised only when the outermost assumption it leaned on holds; a "changed" is always final,
        /// since an assumption only ever adds equality. An acyclic graph leans on nothing and is memoised as it goes.
        /// </remarks>
        private bool Same(object original, object earlier)
        {
            if (_comparing.TryGetValue(original, out int at))
            {
                _leanedOn = Math.Min(_leanedOn, at);
                return true;
            }

            if (_same.TryGetValue(original, out bool known))
            {
                return known;
            }

            int depth = _comparing.Count;
            int pendingFrom = _pending.Count;
            int outer = _leanedOn;
            _comparing.Add(original, depth);
            _leanedOn = int.MaxValue;
            bool same = Compare(original, earlier);
            _comparing.Remove(original);
            int leanedOn = _leanedOn;
            _leanedOn = Math.Min(outer, leanedOn >= depth ? int.MaxValue : leanedOn);

            if (!same)
            {
                _same[original] = false;
            }
            else if (leanedOn < depth)
            {
                _pending.Add(original);
                return true;
            }
            else
            {
                _same[original] = true;
            }

            // Settled here: what was pending on this one holds with it, or is unknown again without it.
            for (int index = pendingFrom; same && index < _pending.Count; index++)
            {
                _same[_pending[index]] = true;
            }

            _pending.RemoveRange(pendingFrom, _pending.Count - pendingFrom);
            return same;
        }

        /// <summary>A one-dimensional array of primitives as its bytes.</summary>
        private static ReadOnlySpan<byte> Bytes(Array array) =>
            MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(array), Buffer.ByteLength(array));

        private bool Compare(object original, object earlier)
        {
            Type type = original.GetType();

            if (type != earlier.GetType())
            {
                return false;
            }

            if (original is Array array)
            {
                Array other = (Array)earlier;

                if (array.Length != other.Length || array.Rank != 1)
                {
                    return false;
                }

                Type element = type.GetElementType()!;

                // Primitives and enums byte for byte, unboxed: bit for bit is what Value asks of a float too, and these
                // have no padding to differ in. A struct may, so it stays element by element.
                if (element.IsPrimitive || element.IsEnum)
                {
                    return Bytes(array).SequenceEqual(Bytes(other));
                }

                for (int index = 0; index < array.Length; index++)
                {
                    if (!Value(array.GetValue(index), other.GetValue(index)))
                    {
                        return false;
                    }
                }

                return true;
            }

            return SameFields(original, earlier, type);
        }

        private bool SameFields(object original, object earlier, Type type)
        {
            for (Type? level = type; level is not null; level = level.BaseType)
            {
                foreach (FieldInfo field in StateCopy.Fields(level))
                {
                    if (!Value(field.GetValue(original), field.GetValue(earlier)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool Value(object? original, object? earlier)
        {
            if (original is null || earlier is null)
            {
                return original is null && earlier is null;
            }

            Type type = original.GetType();

            if (type != earlier.GetType())
            {
                return false;
            }

            // Bit for bit: `0f.Equals(-0f)` is true and the engine's comparisons can tell them apart.
            if (original is float single)
            {
                return BitConverter.SingleToInt32Bits(single) == BitConverter.SingleToInt32Bits((float)earlier);
            }

            if (original is double number)
            {
                return BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits((double)earlier);
            }

            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return original.Equals(earlier);
            }

            if (Immutable(type) || share.Contains(original))
            {
                return ReferenceEquals(original, earlier) || (type.IsValueType && original.Equals(earlier));
            }

            if (type.IsValueType)
            {
                return SameFields(original, earlier, type);
            }

            // A reference must still name the copy of the same original, and that original must be unchanged too.
            return prior!.TryGetValue(original, out object? copied) && ReferenceEquals(copied, earlier) && Same(original, earlier);
        }
    }

    /// <summary>Every object reachable from a root, the root included — what a copy may share when the root is immutable.</summary>
    /// <param name="root">The root, such as the demo's schema.</param>
    /// <returns>The objects, compared by identity.</returns>
    /// <remarks>
    /// **For the schema, which never changes once parsed**: a decoded property carries its <c>SendProperty</c> by value
    /// inside a struct, so sharing the schema's ROOT left every property definition an entity referred to copied again
    /// per snapshot (measured 4.2 MB on movement-test). Sharing everything under it stops that.
    /// </remarks>
    public static HashSet<object> Reachable(object root)
    {
        ArgumentNullException.ThrowIfNull(root);

        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
        Stack<object> pending = new([root]);

        while (pending.TryPop(out object? next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            if (next is Array array)
            {
                if (HasReferences(array.GetType().GetElementType()!))
                {
                    foreach (object? item in array)
                    {
                        Visit(item, pending);
                    }
                }

                continue;
            }

            for (Type? type = next.GetType(); type is not null; type = type.BaseType)
            {
                foreach (FieldInfo field in Fields(type))
                {
                    Visit(field.GetValue(next), pending);
                }
            }
        }

        return seen;
    }

    private static void Visit(object? value, Stack<object> pending)
    {
        if (value is null || Immutable(value.GetType()))
        {
            return;
        }

        if (!value.GetType().IsValueType)
        {
            pending.Push(value);
            return;
        }

        if (HasReferences(value.GetType()))
        {
            // A struct's references are followed in place; the boxed struct itself is not an object to share.
            for (Type? type = value.GetType(); type is not null && type.IsValueType; type = type.BaseType)
            {
                foreach (FieldInfo field in Fields(type))
                {
                    Visit(field.GetValue(value), pending);
                }
            }
        }
    }

    private static object? Copy(object? value, IReadOnlySet<object> share, Dictionary<object, object> copies, Reuse reuse)
    {
        if (value is null)
        {
            return null;
        }

        Type type = value.GetType();

        if (Immutable(type) || share.Contains(value))
        {
            return value;
        }

        if (type.IsValueType)
        {
            // A struct is copied by value already; only references inside it need following.
            return HasReferences(type) ? CopyFields(value, RuntimeHelpers.GetUninitializedObject(type), share, copies, reuse) : value;
        }

        if (copies.TryGetValue(value, out object? done))
        {
            return done;
        }

        // A checkpoint freezes each entity's properties first, so the copy and every resume of it share them (D217 step 4).
        // A bag under a parent reused unchanged above never reaches here, and needs not: it is unchanged only if its
        // shell still names the same Frozen as the prior copy's, which means it is still frozen (StateCopyTests holds it).
        if (reuse.Freeze && value is PropertyBag bag)
        {
            bag.Freeze();
        }

        if (reuse.Unchanged(value) is { } earlier)
        {
            // Its children are the earlier copy's too; mapping them keeps a second reference to one of them on the same copy.
            copies[value] = earlier;
            return earlier;
        }

        if (value is Array array)
        {
            Array copy = Array.CreateInstance(type.GetElementType()!, LengthsOf(array));
            reuse.Created(type, array.Length);
            copies[value] = copy;

            if (HasReferences(type.GetElementType()!) && array.Rank > 1)
            {
                // Copied element by element only in one dimension; a shallow copy here would share what looks copied.
                throw new NotSupportedException($"{type} has more than one dimension and holds references; StateCopy cannot copy it.");
            }

            if (HasReferences(type.GetElementType()!))
            {
                for (int index = 0; index < array.Length; index++)
                {
                    copy.SetValue(Copy(array.GetValue(index), share, copies, reuse), index);
                }
            }
            else
            {
                Array.Copy(array, copy, array.Length);
            }

            return copy;
        }

        object instance = RuntimeHelpers.GetUninitializedObject(type);
        reuse.Created(type, 1);
        copies[value] = instance;

        return CopyFields(value, instance, share, copies, reuse);
    }

    private static object CopyFields(object from, object into, IReadOnlySet<object> share, Dictionary<object, object> copies, Reuse reuse)
    {
        for (Type? type = from.GetType(); type is not null; type = type.BaseType)
        {
            foreach (FieldInfo field in Fields(type))
            {
                object? value = field.GetValue(from);
                field.SetValue(into, field.FieldType.IsValueType && !HasReferences(field.FieldType) ? value : Copy(value, share, copies, reuse));
            }
        }

        return into;
    }

    /// <summary>
    /// A field a copy leaves at its default and a comparison ignores: an instance's claim on what it alone may write,
    /// which a copy must not inherit (<see cref="PropertyBag"/>'s ownership, D217 step 4).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class NotCopiedAttribute : Attribute;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsByType = new();

    // Reaching private fields is the whole of a deep copy: it copies what each type keeps to itself.
#pragma warning disable S3011
    private static FieldInfo[] Fields(Type type) => FieldsByType.GetOrAdd(type, static type =>
        [.. type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => !field.IsDefined(typeof(NotCopiedAttribute), inherit: false))]);
#pragma warning restore S3011

    /// <summary>
    /// The approximate bytes of every object reachable from a root that <paramref name="seen"/> has not met yet — what
    /// this root keeps beyond what was already counted (D217 step 4, part 3).
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="share">Shared inputs, counted as nothing: every checkpoint holds the same ones.</param>
    /// <param name="seen">Objects already counted, by identity; filled as this walks.</param>
    /// <returns>Bytes: a 16-byte header plus each field's size for an object, 24 plus the elements for an array.</returns>
    /// <remarks>
    /// **An estimate with a control**: the probe prints the sum over every owner beside the measured retained heap, and
    /// the two must agree to within the runtime's own overheads. It ranks owners; it is not an allocator's count.
    /// </remarks>
    public static long Footprint(object root, IReadOnlySet<object> share, HashSet<object> seen)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(seen);

        long bytes = 0;
        Stack<object> pending = new([root]);

        while (pending.TryPop(out object? next))
        {
            Type type = next.GetType();

            // Strings and the shared write-once forms are state a checkpoint keeps, so they count; the other immutable
            // types (delegates, reflection, the demo's own bytes) are inputs.
            bool input = share.Contains(next)
                || (Immutable(type) && type != typeof(string) && type != typeof(PropertyBag.Frozen) && type != typeof(Schema.DecodedProperties));

            if (input || !seen.Add(next))
            {
                continue;
            }

            if (next is string text)
            {
                bytes += 22 + (2L * text.Length);
                continue;
            }

            if (next is Array array)
            {
                Type element = type.GetElementType()!;
                bytes += 24 + ((long)array.Length * SlotSize(element));

                if (HasReferences(element))
                {
                    foreach (object? item in array)
                    {
                        Follow(item, pending);
                    }
                }

                continue;
            }

            bytes += 16;

            for (Type? level = type; level is not null; level = level.BaseType)
            {
                foreach (FieldInfo field in Fields(level))
                {
                    bytes += SlotSize(field.FieldType);

                    if (HasReferences(field.FieldType))
                    {
                        Follow(field.GetValue(next), pending);
                    }
                }
            }
        }

        return bytes;
    }

    private static int SlotSize(Type type) =>
        type.IsValueType ? RuntimeHelpers.SizeOf(type.TypeHandle) : IntPtr.Size;

    /// <summary>Pushes the objects a value names: itself if a reference, or the references inside it if a struct.</summary>
    private static void Follow(object? value, Stack<object> pending)
    {
        if (value is null)
        {
            return;
        }

        Type type = value.GetType();

        if (!type.IsValueType)
        {
            pending.Push(value);
            return;
        }

        for (Type? level = type; level is not null && level.IsValueType; level = level.BaseType)
        {
            foreach (FieldInfo field in Fields(level))
            {
                if (HasReferences(field.FieldType))
                {
                    Follow(field.GetValue(value), pending);
                }
            }
        }
    }

    /// <summary>A hash of every VALUE reachable from a root, identity ignored except for the graph's shape.</summary>
    /// <param name="root">The root.</param>
    /// <param name="share">Instances hashed as a marker rather than followed, as a copy shares them.</param>
    /// <returns>The hash, as hex.</returns>
    /// <remarks>
    /// **The control for "nothing wrote the checkpoint"** (D217 step 4). Comparing a checkpoint with a copy of it cannot
    /// see a write into something they SHARE — a frozen bag's chunks are both of theirs — so this follows shared frozen
    /// state too, and two hashes of one checkpoint, before and after a window, must agree.
    /// </remarks>
    public static string Digest(object root, IReadOnlySet<object> share)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(share);

        using System.Security.Cryptography.IncrementalHash hash =
            System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        Hash(root, share, new Dictionary<object, int>(ReferenceEqualityComparer.Instance), hash);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Hash(object? value, IReadOnlySet<object> share, Dictionary<object, int> seen, System.Security.Cryptography.IncrementalHash hash)
    {
        if (value is null)
        {
            hash.AppendData("n"u8);
            return;
        }

        Type type = value.GetType();
        hash.AppendData(BitConverter.GetBytes(type.TypeHandle.Value));

        switch (value)
        {
            case float single:
                hash.AppendData(BitConverter.GetBytes(BitConverter.SingleToInt32Bits(single)));
                return;
            case double number:
                hash.AppendData(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(number)));
                return;
            case string text:
                hash.AppendData(BitConverter.GetBytes(text.Length));
                hash.AppendData(MemoryMarshal.AsBytes(text.AsSpan()));
                return;
            default:
                break;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!));
            return;
        }

        // Shared inputs and the truly immutable are markers; a frozen bag is followed, being the state under test.
        if ((Immutable(type) && type != typeof(PropertyBag.Frozen) && type != typeof(Schema.DecodedProperties)) || share.Contains(value))
        {
            hash.AppendData("s"u8);
            return;
        }

        if (!type.IsValueType)
        {
            if (seen.TryGetValue(value, out int at))
            {
                hash.AppendData(BitConverter.GetBytes(at));
                return;
            }

            seen[value] = seen.Count;
        }

        if (value is Array array)
        {
            hash.AppendData(BitConverter.GetBytes(array.Length));
            Type element = type.GetElementType()!;

            if ((element.IsPrimitive || element.IsEnum) && array.Rank == 1)
            {
                hash.AppendData(MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(array), Buffer.ByteLength(array)));
                return;
            }

            foreach (object? item in array)
            {
                Hash(item, share, seen, hash);
            }

            return;
        }

        for (Type? level = type; level is not null; level = level.BaseType)
        {
            foreach (FieldInfo field in Fields(level))
            {
                Hash(field.GetValue(value), share, seen, hash);
            }
        }
    }

    private static int[] LengthsOf(Array array)
    {
        int[] lengths = new int[array.Rank];

        for (int dimension = 0; dimension < lengths.Length; dimension++)
        {
            lengths[dimension] = array.GetLength(dimension);
        }

        return lengths;
    }

    private static bool HasReferences(Type type) =>
        !type.IsPrimitive && !type.IsEnum && (!type.IsValueType || ContainsReferences.GetOrAdd(type, Ask));

    private static readonly ConcurrentDictionary<Type, bool> ContainsReferences = new();

    private static bool Ask(Type type) =>
        (bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!
            .MakeGenericMethod(type).Invoke(null, null)!;

    /// <summary>Types whose instances never change, so a copy may share them.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it is shared rather than copied.</returns>
    /// <remarks>
    /// **Only the ones that cannot change at all.** A record is NOT here: `init` stops reassignment of its members but
    /// a member can be a list someone appends to.
    ///
    /// **A <see cref="ReadOnlyMemory{T}"/> is here, and that is a decision about the input, not about the type.**
    /// `DemoCommandReader` slices the demo's own bytes, and nothing writes through a read-only view; following one would
    /// copy the backing array — the whole FILE if it is a slice of it — and would clone a `MemoryManager`'s handle (a
    /// mapped view). So the view and what it views are shared. *Measured* (`timeline-resume … cost`, 2026-10-15): the
    /// builder's state reaches no slice of the file at the half-way snapshot — following or sharing gave the same bytes
    /// on movement-test (103.6 vs 103.7 MB) and f12 (780.4 both) — so this guards a later field, not a present cost.
    /// </remarks>
    internal static bool Immutable(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || typeof(Delegate).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type)
        || type.IsPointer
        || type == typeof(PropertyBag.Frozen)
        || type == typeof(Schema.DecodedProperties)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>));
}

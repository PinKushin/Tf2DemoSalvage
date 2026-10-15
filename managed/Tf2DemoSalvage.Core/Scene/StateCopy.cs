using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

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
    public static T Of<T>(
        T source,
        IReadOnlySet<object> share,
        Dictionary<object, object>? prior,
        out Dictionary<object, object> copies,
        Dictionary<Type, (long Objects, long Elements)>? created = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(share);

        copies = new(ReferenceEqualityComparer.Instance);
        Reuse reuse = new(share, prior, created);

        T copy = (T)Copy(source, share, copies, reuse)!;
        reuse.MapReused(copies);
        return copy;
    }

    /// <summary>Whether an original still equals its earlier copy, memoised over one copy.</summary>
    private sealed class Reuse(IReadOnlySet<object> share, Dictionary<object, object>? prior, Dictionary<Type, (long Objects, long Elements)>? created)
    {
        private readonly Dictionary<object, bool> _same = new(ReferenceEqualityComparer.Instance);

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

        private bool Same(object original, object earlier)
        {
            if (_same.TryGetValue(original, out bool known))
            {
                return known;
            }

            // Assumed equal while it is being compared, so a cycle back to it does not recurse for ever.
            _same.Add(original, true);
            bool same = Compare(original, earlier);

            if (!same)
            {
                _same[original] = false;
            }

            return same;
        }

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

    // Reaching private fields is the whole of a deep copy: it copies what each type keeps to itself.
#pragma warning disable S3011
    private static FieldInfo[] Fields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
#pragma warning restore S3011

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
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>));
}

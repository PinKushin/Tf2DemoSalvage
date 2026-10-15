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

        Dictionary<object, object> copies = new(ReferenceEqualityComparer.Instance);

        return (T)Copy(source, share, copies)!;
    }

    private static object? Copy(object? value, IReadOnlySet<object> share, Dictionary<object, object> copies)
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
            return HasReferences(type) ? CopyFields(value, RuntimeHelpers.GetUninitializedObject(type), share, copies) : value;
        }

        if (copies.TryGetValue(value, out object? done))
        {
            return done;
        }

        if (value is Array array)
        {
            Array copy = Array.CreateInstance(type.GetElementType()!, LengthsOf(array));
            copies[value] = copy;

            if (HasReferences(type.GetElementType()!) && array.Rank == 1)
            {
                for (int index = 0; index < array.Length; index++)
                {
                    copy.SetValue(Copy(array.GetValue(index), share, copies), index);
                }
            }
            else
            {
                Array.Copy(array, copy, array.Length);
            }

            return copy;
        }

        object instance = RuntimeHelpers.GetUninitializedObject(type);
        copies[value] = instance;

        return CopyFields(value, instance, share, copies);
    }

    private static object CopyFields(object from, object into, IReadOnlySet<object> share, Dictionary<object, object> copies)
    {
        for (Type? type = from.GetType(); type is not null; type = type.BaseType)
        {
            foreach (FieldInfo field in Fields(type))
            {
                object? value = field.GetValue(from);
                field.SetValue(into, field.FieldType.IsValueType && !HasReferences(field.FieldType) ? value : Copy(value, share, copies));
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
    /// </remarks>
    internal static bool Immutable(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || typeof(Delegate).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type)
        || type.IsPointer;
}

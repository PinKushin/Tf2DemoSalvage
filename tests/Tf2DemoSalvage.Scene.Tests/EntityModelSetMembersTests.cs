using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Holds <see cref="EntityModelSet"/>'s member list fixed while it is split into partial files.</summary>
/// <remarks>
/// **A partial split is a pure move, and this is the instrument that says so.** It reflects over every
/// member the compiler emitted for the type (public, internal, private, static, instance, nested, with
/// parameter and return types) and compares the sorted list with a checked-in snapshot taken from the
/// unsplit class. A member that is dropped, renamed, retyped or has its visibility changed in a move
/// changes the list.
///
/// **Compiler-generated members are excluded** (closures, lambdas, local functions, backing fields):
/// their names carry an ordinal that follows source order, which a move legitimately changes, and they
/// are not members anyone wrote.
/// </remarks>
public sealed class EntityModelSetMembersTests
{
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Test]
    public void EntityModelSet_Members_AreTheSameAfterTheSplit()
    {
        string snapshotPath = SnapshotPath();
        string actual = string.Join('\n', Describe(typeof(EntityModelSet)).Order(StringComparer.Ordinal)) + "\n";

        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, actual);
            Assert.Fail($"No snapshot existed; wrote {snapshotPath}. Review it and commit it.");
        }

        actual.ShouldBe(File.ReadAllText(snapshotPath));
    }

    private static IEnumerable<string> Describe(Type type)
    {
        string owner = type.FullName ?? type.Name;
        foreach (MemberInfo member in type.GetMembers(All))
        {
            if (member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                continue;
            }

            switch (member)
            {
                case FieldInfo f:
                    yield return $"{owner}|field|{f.Name}|{Access(f)}{(f.IsStatic ? " static" : "")}{(f.IsInitOnly ? " readonly" : "")}|{f.FieldType}";
                    break;
                case MethodBase m:
                    string returns = m is MethodInfo mi ? mi.ReturnType.ToString() : "ctor";
                    string parameters = string.Join(",", m.GetParameters().Select(p => p.ParameterType.ToString()));
                    yield return $"{owner}|{(m is ConstructorInfo ? "ctor" : "method")}|{m.Name}|{Access(m)}{(m.IsStatic ? " static" : "")}|({parameters})|{returns}";
                    break;
                case PropertyInfo p:
                    yield return $"{owner}|property|{p.Name}|({string.Join(",", p.GetIndexParameters().Select(i => i.ParameterType.ToString()))})|{p.PropertyType}|get={p.GetMethod != null}|set={p.SetMethod != null}";
                    break;
                case EventInfo e:
                    yield return $"{owner}|event|{e.Name}|{e.EventHandlerType}";
                    break;
                case Type nested:
                    yield return $"{owner}|nested|{nested.Name}|{(nested.IsNestedPublic ? "public" : "nonpublic")}";
                    foreach (string line in Describe(nested))
                    {
                        yield return line;
                    }

                    break;
            }
        }
    }

    private static string Access(FieldInfo f) => (f.Attributes & FieldAttributes.FieldAccessMask).ToString();

    private static string Access(MethodBase m) => (m.Attributes & MethodAttributes.MemberAccessMask).ToString();

    private static string SnapshotPath([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, "Snapshots", "EntityModelSet.members.txt");
}

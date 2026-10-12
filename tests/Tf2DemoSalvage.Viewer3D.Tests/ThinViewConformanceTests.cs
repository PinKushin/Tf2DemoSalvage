using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The view's DOMAIN REACH: every type and member the built viewer assembly uses from the domain assemblies, held
/// equal to a committed list that may only shrink (D90, D214).
/// </summary>
/// <remarks>
/// **The measure is reach, not size.** A thin view knows nothing a presenter or a model would; whether it does is what
/// its assembly REFERENCES, which the compiler writes down. The list is `thin-view-domain-reach.txt` beside this file,
/// one `Assembly!Namespace.Type` or `Assembly!Namespace.Type::Member(arity)` a line, sorted ordinally. The end state is
/// the list empty and the project references gone, at which point the compiler enforces what this test does.
///
/// **Domain is Core, Content, Scene, Audio, Animation and GameSystems.** Presentation and Render are where behaviour
/// goes (D90's table: presenter and renderer), and Fonts and Logging are services; none of the four is counted.
///
/// **What the instrument reads:** the assembly's `TypeRef` and `MemberRef` tables. That covers calls, field and
/// property access, constructors, `typeof`, casts, attributes, locals, generic arguments, and a member reached on a
/// generic instantiation of a domain type; lambdas and local functions are in the same tables. A method is recorded
/// with its parameter count, so a second overload of the same arity is not a new entry.
///
/// **What it cannot see:** a `const` or an enum member, which the compiler copies in as a number; `nameof`; a domain
/// value handed from one Render or Presentation call straight to another without a local, which needs no reference at
/// all; and anything reached by name at run time. A Render or Presentation member whose SIGNATURE mentions a domain
/// type leaves that type here only if the view names it — a local, a field, a parameter of its own.
/// </remarks>
public sealed class ThinViewConformanceTests
{
    /// <summary>The assemblies a view may not reach into.</summary>
    private static readonly string[] Domain =
    [
        "Tf2DemoSalvage.Core",
        "Tf2DemoSalvage.Content",
        "Tf2DemoSalvage.Scene",
        "Tf2DemoSalvage.Audio",
        "Tf2DemoSalvage.Animation",
        "Tf2DemoSalvage.GameSystems",
    ];

    private const string ListName = "thin-view-domain-reach.txt";

    private const string ViewAssembly = "tf2demoview.dll";

    [Test]
    public void DomainReach_OfTheBuiltViewAssembly_IsExactlyTheCommittedList()
    {
        Reach reach = Reach.Of(Path.Combine(AppContext.BaseDirectory, ViewAssembly));

        // What the assembly uses today, beside the test's binaries: the lines to delete are a diff against this.
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "thin-view-domain-reach.actual.txt"), string.Join('\n', reach.Entries) + "\n");
        TestContext.Out.WriteLine(reach.Report());

        string[] listed = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, ListName))
            .Where(static line => line.Length > 0)
            .ToArray();

        string[] added = reach.Entries.Except(listed, StringComparer.Ordinal).ToArray();
        string[] gone = listed.Except(reach.Entries, StringComparer.Ordinal).ToArray();

        added.ShouldBeEmpty(
            $"the view assembly uses {added.Length} domain type(s) or member(s) that are not in {ListName}. " +
            "Behaviour goes in Presentation/Scene/Render with tests; the view gets one call. " +
            "The list only shrinks: do not add these lines.\n  " + string.Join("\n  ", added.Select(reach.WithUsers)));

        gone.ShouldBeEmpty(
            $"{ListName} holds {gone.Length} entr(ies) the view assembly no longer uses. Delete these lines:\n  " +
            string.Join("\n  ", gone));

        listed.ShouldBe(listed.Order(StringComparer.Ordinal).ToArray(), $"{ListName} is kept sorted, ordinally");
        listed.Distinct(StringComparer.Ordinal).Count().ShouldBe(listed.Length, $"{ListName} holds no line twice");
    }

    /// <summary>One assembly's references into the domain, and which of its own methods hold them.</summary>
    private sealed class Reach
    {
        /// <summary>Every entry, sorted ordinally.</summary>
        public required string[] Entries { get; init; }

        /// <summary>The view's own methods that hold each MEMBER entry, by `Type.Method`.</summary>
        public required Dictionary<string, SortedSet<string>> Users { get; init; }

        public string WithUsers(string entry)
        {
            if (!Users.TryGetValue(entry, out SortedSet<string>? users))
            {
                return entry;
            }

            string more = users.Count > 4 ? $" and {users.Count - 4} more" : string.Empty;

            return $"{entry}    used by {string.Join(", ", users.Take(4))}{more}";
        }

        public static Reach Of(string path)
        {
            using PEReader file = new(File.OpenRead(path));
            MetadataReader reader = file.GetMetadataReader();
            Names names = new(reader);
            SortedSet<string> entries = new(StringComparer.Ordinal);
            Dictionary<EntityHandle, string> members = [];

            foreach (TypeReferenceHandle handle in reader.TypeReferences)
            {
                if (names.Type(handle) is { } type)
                {
                    entries.Add(type);
                }
            }

            foreach (MemberReferenceHandle handle in reader.MemberReferences)
            {
                MemberReference member = reader.GetMemberReference(handle);

                if (names.Parent(member.Parent) is not { } parent)
                {
                    continue;
                }

                string name = reader.GetString(member.Name);
                string entry = member.GetKind() == MemberReferenceKind.Method
                    ? $"{parent}::{name}({member.DecodeMethodSignature(names, null).ParameterTypes.Length})"
                    : $"{parent}::{name}";

                entries.Add(entry);
                members[handle] = entry;
            }

            return new Reach { Entries = [.. entries], Users = UsersOf(file, reader, members) };
        }

        /// <summary>Walks every method body's IL for the member references it holds.</summary>
        private static Dictionary<string, SortedSet<string>> UsersOf(
            PEReader file, MetadataReader reader, Dictionary<EntityHandle, string> members)
        {
            Dictionary<string, SortedSet<string>> users = new(StringComparer.Ordinal);

            foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);

                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                string owner = Owner(reader, method);

                foreach (EntityHandle token in Tokens(file.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? []))
                {
                    EntityHandle named = token.Kind == HandleKind.MethodSpecification
                        ? reader.GetMethodSpecification((MethodSpecificationHandle)token).Method
                        : token;

                    if (members.TryGetValue(named, out string? entry))
                    {
                        if (!users.TryGetValue(entry, out SortedSet<string>? held))
                        {
                            held = new SortedSet<string>(StringComparer.Ordinal);
                            users[entry] = held;
                        }

                        held.Add(owner);
                    }
                }
            }

            return users;
        }

        /// <summary>
        /// `Type.Method` as the source names it: a lambda's or a local function's body belongs to the method it was
        /// written in, which the compiler keeps between angle brackets, and its closure class to the type around it.
        /// </summary>
        private static string Owner(MetadataReader reader, MethodDefinition method)
        {
            TypeDefinition type = reader.GetTypeDefinition(method.GetDeclaringType());
            string name = reader.GetString(method.Name);

            if (name.StartsWith('<') && name.IndexOf('>', StringComparison.Ordinal) is > 1 and int close)
            {
                name = name[1..close];
            }

            while (reader.GetString(type.Name).StartsWith('<') && !type.GetDeclaringType().IsNil)
            {
                type = reader.GetTypeDefinition(type.GetDeclaringType());
            }

            return $"{reader.GetString(type.Name)}.{name}";
        }

        /// <summary>Every metadata token an IL stream's instructions carry.</summary>
        private static IEnumerable<EntityHandle> Tokens(byte[] il)
        {
            int at = 0;

            while (at < il.Length)
            {
                int value = il[at++];

                if (value == 0xFE)
                {
                    value = 0xFE00 | il[at++];
                }

                OpCode code = OpCodes[unchecked((short)value)];

                switch (code.OperandType)
                {
                    case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                        yield return MetadataTokens.EntityHandle(BitConverter.ToInt32(il, at));
                        at += 4;
                        break;

                    case OperandType.InlineSwitch:
                        at += 4 + (4 * BitConverter.ToInt32(il, at));
                        break;

                    case OperandType.InlineI8 or OperandType.InlineR:
                        at += 8;
                        break;

                    case OperandType.InlineBrTarget or OperandType.InlineI or OperandType.InlineSig
                        or OperandType.InlineString or OperandType.ShortInlineR:
                        at += 4;
                        break;

                    case OperandType.InlineVar:
                        at += 2;
                        break;

                    case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                        at += 1;
                        break;

                    default:
                        break;
                }
            }
        }

        /// <summary>Every IL instruction by its encoded value, for its operand's size.</summary>
        private static readonly Dictionary<short, OpCode> OpCodes = typeof(OpCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(static field => (OpCode)field.GetValue(null)!)
            .ToDictionary(static code => code.Value);

        /// <summary>The numbers the refactor is sized by: totals, by assembly, by namespace, and the fattest methods.</summary>
        public string Report()
        {
            StringBuilder report = new();
            string[] types = Entries.Where(static entry => !entry.Contains("::", StringComparison.Ordinal)).ToArray();

            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"THIN VIEW domain reach: {Entries.Length} entries ({types.Length} types, {Entries.Length - types.Length} members)");

            foreach (IGrouping<string, string> assembly in Entries.GroupBy(static entry => entry[..entry.IndexOf('!', StringComparison.Ordinal)]))
            {
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {assembly.Key}: {assembly.Count()}");
            }

            report.AppendLine("by namespace:");

            foreach (IGrouping<string, string> space in Entries.GroupBy(NamespaceOf).OrderByDescending(static g => g.Count()))
            {
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {space.Count(),5}  {space.Key}");
            }

            report.AppendLine("view methods by distinct domain members used:");

            foreach ((string method, int count) in Users
                .SelectMany(static pair => pair.Value.Select(user => (user, pair.Key)))
                .GroupBy(static held => held.user)
                .Select(static g => (g.Key, g.Count()))
                .OrderByDescending(static row => row.Item2)
                .ThenBy(static row => row.Key, StringComparer.Ordinal)
                .Take(40))
            {
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {count,5}  {method}");
            }

            report.AppendLine("view types by distinct domain members used:");

            foreach ((string type, int count) in Users
                .SelectMany(static pair => pair.Value.Select(user => (type: user[..user.IndexOf('.', StringComparison.Ordinal)], pair.Key)))
                .Distinct()
                .GroupBy(static held => held.type)
                .Select(static g => (g.Key, g.Count()))
                .OrderByDescending(static row => row.Item2))
            {
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  {count,5}  {type}");
            }

            return report.ToString();
        }

        /// <summary>`Assembly!Namespace` of an entry; a nested type's is its outermost type's.</summary>
        private static string NamespaceOf(string entry)
        {
            string type = entry.Split("::", 2)[0].Split('+', 2)[0];

            return type[..type.LastIndexOf('.')];
        }
    }

    /// <summary>Names a reference `Assembly!Namespace.Type`, or null when it is not into the domain.</summary>
    private sealed class Names(MetadataReader reader) : ISignatureTypeProvider<string?, object?>
    {
        /// <summary>A type reference's entry; a nested type is `Outer+Inner`, in its outermost type's assembly.</summary>
        public string? Type(TypeReferenceHandle handle)
        {
            TypeReference type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);

            switch (type.ResolutionScope.Kind)
            {
                case HandleKind.TypeReference:
                    return Type((TypeReferenceHandle)type.ResolutionScope) is { } outer ? $"{outer}+{name}" : null;

                case HandleKind.AssemblyReference:
                    string assembly = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name);

                    return Array.IndexOf(Domain, assembly) >= 0 ? $"{assembly}!{reader.GetString(type.Namespace)}.{name}" : null;

                default:
                    return null;
            }
        }

        /// <summary>A member's declaring type: a reference, or the generic type an instantiation is of.</summary>
        public string? Parent(EntityHandle parent) => parent.Kind switch
        {
            HandleKind.TypeReference => Type((TypeReferenceHandle)parent),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(this, null),
            _ => null,
        };

        public string? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Type(handle);

        public string? GetGenericInstantiation(string? genericType, ImmutableArray<string?> typeArguments) => genericType;

        public string? GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        // Everything below is not a domain type's own member: an array, a pointer or a reference of one has none the
        // view could name, and the element's type reference is in the table already.
        public string? GetArrayType(string? elementType, ArrayShape shape) => null;

        public string? GetByReferenceType(string? elementType) => null;

        public string? GetFunctionPointerType(MethodSignature<string?> signature) => null;

        public string? GetGenericMethodParameter(object? genericContext, int index) => null;

        public string? GetGenericTypeParameter(object? genericContext, int index) => null;

        public string? GetModifiedType(string? modifier, string? unmodifiedType, bool isRequired) => unmodifiedType;

        public string? GetPinnedType(string? elementType) => null;

        public string? GetPointerType(string? elementType) => null;

        public string? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;

        public string? GetSZArrayType(string? elementType) => null;

        public string? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;
    }
}

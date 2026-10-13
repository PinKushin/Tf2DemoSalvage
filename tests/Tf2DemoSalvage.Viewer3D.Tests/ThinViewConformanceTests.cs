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

using Tf2DemoSalvage.Presentation;

#pragma warning disable CA1812 // HoldsWhatAViewMayNot is never instantiated: it exists to be reflected over by the field rule's control.

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The view's DOMAIN REACH: every type and member the built viewer assembly uses from the domain assemblies. There
/// are none, and this holds it there (D90, D214).
/// </summary>
/// <remarks>
/// **The measure is reach, not size.** A thin view knows nothing a presenter or a model would; whether it does is what
/// its assembly REFERENCES, which the compiler writes down. A committed list of what it reached,
/// `thin-view-domain-reach.txt`, shrank over eight slices from 1,025 entries to none (2026-10-13). The three tests
/// here are the end state: the assembly reaches nothing, the project references no domain project, and no source
/// file names a domain namespace.
///
/// **Why three, and why the compiler alone does not hold it:** the domain assemblies still arrive through Presentation
/// and Render, and the view cannot shut them out — `Device3D` implements Scene's interfaces, so using it at all needs
/// Scene to be resolvable. So a `using` would compile. The source test refuses it, and it also sees what the assembly
/// test cannot: a `const` the compiler copied in.
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

    private const string ViewAssembly = "tf2demoview.dll";

    /// <summary>The view's project folder, found from the test's binaries.</summary>
    private static string ViewProject => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "managed", "Tf2DemoSalvage.Viewer3D"));

    [Test]
    public void DomainReach_OfTheBuiltViewAssembly_IsNothing()
    {
        Reach reach = Reach.Of(Path.Combine(AppContext.BaseDirectory, ViewAssembly));

        TestContext.Out.WriteLine(reach.Report());

        reach.Entries.ShouldBeEmpty(
            $"the view assembly uses {reach.Entries.Length} domain type(s) or member(s), and a thin view uses none (D214). " +
            "Behaviour goes in Presentation/Scene/Render with tests; the view gets one call.\n  " +
            string.Join("\n  ", reach.Entries.Select(reach.WithUsers)));
    }

    [Test]
    public void ProjectReferences_OfTheViewsProject_NameNoDomainAssembly()
    {
        string project = File.ReadAllText(Path.Combine(ViewProject, "Tf2DemoSalvage.Viewer3D.csproj"));

        string[] referenced = Domain
            .Where(assembly => project.Contains($"{assembly}.csproj", StringComparison.Ordinal))
            .ToArray();

        referenced.ShouldBeEmpty("the view's project references Presentation, Render, Fonts and Logging, and no domain project (D214)");
    }

    [Test]
    public void Sources_OfTheView_NameNoDomainNamespace()
    {
        List<string> named = [];

        foreach (string file in Directory.EnumerateFiles(ViewProject, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string[] lines = File.ReadAllLines(file);

            for (int at = 0; at < lines.Length; at++)
            {
                string code = lines[at].TrimStart();

                // Comments may name what moved and where it went; code may not name the namespace at all.
                if (code.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Domain.Any(assembly => code.Contains(assembly + ".", StringComparison.Ordinal) || code.Contains(assembly + ";", StringComparison.Ordinal)))
                {
                    named.Add($"{Path.GetFileName(file)}({at + 1}): {code}");
                }
            }
        }

        named.ShouldBeEmpty(
            "a view source file names a domain namespace — a `using`, or a fully qualified type or constant, which the " +
            "assembly test cannot see when the compiler copies the value in (D214):\n  " + string.Join("\n  ", named));
    }

    [Test]
    public void Fields_OfEveryViewType_HoldNoObjectOfTheLayersBelowButTheSession()
    {
        // **The guard slice 9 found a sound rule for** (D214). Reach at zero did not make the view thin: it still held
        // the playback presenter, the camera presenter, the frame clock, the opening sequence, the console, the launch
        // options and the device, and sequenced them itself — in plain values and Presentation's own types, which the
        // reach count is not about. Every one of those needed a FIELD of a class from below. A view holds its controls,
        // its own window state, and the one session.
        string[] held = HeldFromBelow(typeof(MainForm).Assembly.GetTypes());

        held.ShouldBeEmpty(
            "a view type holds an object of a layer below it. Behaviour and its state go in the session " +
            "(`ViewerSession`, Presentation) with tests; the view holds controls, window state and the session (D214):\n  " +
            string.Join("\n  ", held));
    }

    [Test]
    public void Fields_OfATypeThatHoldsAPresenterAndADevice_AreReportedAndItsValuesAndSessionAreNot()
    {
        // The control: the same question asked of a type that does what the rule forbids.
        HeldFromBelow([typeof(HoldsWhatAViewMayNot)]).ShouldBe(
        [
            "HoldsWhatAViewMayNot._devices: Tf2DemoSalvage.Render.Device3D",
            "HoldsWhatAViewMayNot._library: Tf2DemoSalvage.Presentation.PlaylistPresenter",
            "HoldsWhatAViewMayNot._window: Tf2DemoSalvage.Presentation.IViewerWindow",
        ]);
    }

    /// <summary>The assemblies whose objects a view may not hold: everything of this program's but the view, its fonts and its log.</summary>
    private static bool IsBelow(System.Reflection.Assembly assembly)
    {
        string name = assembly.GetName().Name ?? string.Empty;

        return name.StartsWith("Tf2DemoSalvage.", StringComparison.Ordinal)
            && name is not ("Tf2DemoSalvage.Fonts" or "Tf2DemoSalvage.Logging")
            && !name.EndsWith(".Tests", StringComparison.Ordinal);
    }

    /// <summary>Every field of the given types — compiler-made closures and backing fields included — that holds a class or interface from below.</summary>
    /// <remarks>
    /// **Classes and interfaces, not values.** An enum or a struct from Presentation (`ViewerAction` in the menu's
    /// shortcut table, a `DebugModes` in a delegate's signature) is a value handed across; an object is something to
    /// call, and calling is where the behaviour was. The type is taken apart first, so a `List&lt;Device3D&gt;` or a
    /// `Func&lt;PlaybackPresenter&gt;` is the same finding as the object itself.
    /// </remarks>
    private static string[] HeldFromBelow(IEnumerable<Type> types)
    {
        const System.Reflection.BindingFlags Declared =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly;

        SortedSet<string> held = new(StringComparer.Ordinal);

        foreach (Type type in types)
        {
            foreach (System.Reflection.FieldInfo field in type.GetFields(Declared))
            {
                foreach (Type part in Parts(field.FieldType))
                {
                    if ((part.IsClass || part.IsInterface)
                        && !typeof(Delegate).IsAssignableFrom(part)

                        // An event's argument is a value handed across, as a struct is: the transport's three events
                        // are `EventHandler<TickEventArgs>` and the like, which `IPlaybackView` requires of it.
                        && !typeof(EventArgs).IsAssignableFrom(part)
                        && IsBelow(part.Assembly)
                        && part != typeof(ViewerSession))
                    {
                        held.Add($"{type.Name}.{field.Name}: {part.FullName}");
                    }
                }
            }
        }

        return [.. held];
    }

    /// <summary>A type and everything it is built from: array elements and generic arguments, all the way down.</summary>
    private static IEnumerable<Type> Parts(Type type)
    {
        if (type.HasElementType)
        {
            foreach (Type part in Parts(type.GetElementType()!))
            {
                yield return part;
            }

            yield break;
        }

        yield return type.IsGenericType ? type.GetGenericTypeDefinition() : type;

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type part in Parts(argument))
            {
                yield return part;
            }
        }
    }

    /// <summary>What the field rule forbids, and two things it allows, in one type.</summary>
#pragma warning disable CS0649, CS0169, CA1823, S1144, S3459, IDE0051 // Never assigned or read: the fields exist to be found.
    private sealed class HoldsWhatAViewMayNot
    {
        private readonly PlaylistPresenter? _library;

        private readonly List<Device3D>? _devices;

        private readonly Func<IViewerWindow?[]>? _window;

        private readonly ViewerSession? _session;

        private readonly (ViewerAction Action, DebugModes Modes) _values;
    }
#pragma warning restore CS0649, CS0169, CA1823, S1144, S3459, IDE0051

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

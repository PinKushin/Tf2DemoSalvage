using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>No call in the renderer hands native code the unpinned address of a <see cref="ComPtr{T}"/> on the managed heap (B706).</summary>
/// <remarks>
/// **Read from Silk.NET 2.23.0** (`ComPtr`1.cs`): `GetAddressOf() => (T**)Unsafe.AsPointer(ref Unsafe.AsRef(in this))`, and
/// every generated `ref ComPtr&lt;T&gt;` overload passes that pointer on. `ref _camera` is a field of a heap object, so a
/// compacting collection on another thread leaves native code reading or writing where the object used to be. CI's
/// `0xC0000005` under `PSSetConstantBuffers` has this shape, reproduced by experiment: B706 holds the table.
///
/// **Read from the compiled IL, not the source**, so `ref _x`, `ref a._b`, `ref _array[i]` and an overload nobody thought of
/// are one rule. Refused outright: `GetAddressOf` and `Unsafe.AsPointer`, which nothing here needs. A `ref` to a `ComPtr` or
/// a raw pointer reaching Silk.NET must be rooted in a local or a parameter, on the stack, which never moves;
/// `PinnedComCalls` pins the rest. **It fails closed**: an argument it cannot walk back through simple pushes, or with a branch
/// or a branch target between the root and the call, is reported.
///
/// **What it does not see:** a call that launders the address through a helper of ours taking `ref` and forwarding it as a
/// pointer (the forward itself would be reported unless it pins), and anything outside this assembly, the only one using `ComPtr`.
/// `&amp;_field` with no `fixed` is not scanned for because the compiler refuses it (CS0212).
/// </remarks>
public sealed class UnpinnedComPtrConformanceTests
{
    [Test]
    public void Renderer_EveryMethod_PassesSilkNoHeapComPtrAddress()
    {
        string[] calls = [.. UnpinnedCalls(typeof(Device3D).Assembly.GetTypes())];

        calls.ShouldBeEmpty(
            "each takes a ComPtr's address on the heap with Unsafe.AsPointer and pins nothing; give it a PinnedComCalls overload");
    }

    /// <summary>The control: the scan names each heap form in this class's own <see cref="UnpinnedOnPurpose"/> and passes each stack form.</summary>
    [Test]
    public void UnpinnedCalls_OverEachForm_NamesTheHeapOnesOnly()
    {
        string[] calls = [.. UnpinnedCalls([typeof(UnpinnedOnPurpose)])];

        calls.ShouldBe(
        [
            Named(nameof(UnpinnedOnPurpose.ByFieldReference), "PSSetConstantBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByElementReference), "PSSetConstantBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByForwardedReference), "PSSetConstantBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByFieldWithTrailingArguments), "IASetVertexBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByLocalWithAComputedTrailingArgument), "IASetVertexBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByConditionalEndingInTheLocal), "PSSetConstantBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByConditionalEndingInTheField), "PSSetConstantBuffers"),
            Named(nameof(UnpinnedOnPurpose.ByAddressOfAField), "GetAddressOf"),
            Named(nameof(UnpinnedOnPurpose.ByAddressOfALocal), "GetAddressOf"),
            Named(nameof(UnpinnedOnPurpose.ByAsPointer), "AsPointer"),
        ],
        ignoreOrder: true);

        static string Named(string method, string callee) => $"{nameof(UnpinnedOnPurpose)}.{method} -> {callee}";
    }

#pragma warning disable S6640, S3459, S1144, S2933, S3358, CS0649, CA1823 // The control holds the very calls the scan refuses; it is never run, only read.
    /// <summary>The control's subject. Never run: the test reads its IL.</summary>
    private static unsafe class UnpinnedOnPurpose
    {
        private static ComPtr<ID3D11Buffer> s_camera;
        private static readonly ComPtr<ID3D11Buffer>[] s_cameras = new ComPtr<ID3D11Buffer>[1];

        public static void ByFieldReference(ComPtr<ID3D11DeviceContext> context) =>
            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref s_camera);

        public static void ByElementReference(ComPtr<ID3D11DeviceContext> context) =>
            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref s_cameras[0]);

        public static void ByForwardedReference(ComPtr<ID3D11DeviceContext> context, ref ComPtr<ID3D11Buffer> camera) =>
            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref camera);

        public static void ByFieldWithTrailingArguments(ComPtr<ID3D11DeviceContext> context, uint stride, uint offset) =>
            D3D11DeviceContextVtblExtensions.IASetVertexBuffers(context, 0, 1, ref s_camera, in stride, in offset);

        public static void ByLocalWithAComputedTrailingArgument(ComPtr<ID3D11DeviceContext> context, uint offset)
        {
            ComPtr<ID3D11Buffer> camera = s_camera;

            D3D11DeviceContextVtblExtensions.IASetVertexBuffers(context, 0, 1, ref camera, in Computed(), in offset);
        }

        public static void ByConditionalEndingInTheLocal(ComPtr<ID3D11DeviceContext> context, bool which)
        {
            ComPtr<ID3D11Buffer> camera = default;

            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref which ? ref s_camera : ref camera);
        }

        public static void ByConditionalEndingInTheField(ComPtr<ID3D11DeviceContext> context, bool which)
        {
            ComPtr<ID3D11Buffer> camera = default;

            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref which ? ref camera : ref s_camera);
        }

        public static nint ByAddressOfAField() => (nint)s_camera.GetAddressOf();

        public static nint ByAddressOfALocal(ComPtr<ID3D11Buffer> camera) => (nint)camera.GetAddressOf();

        public static nint ByAsPointer() => (nint)Unsafe.AsPointer(ref s_camera);

        public static void ByLocalReference(ComPtr<ID3D11DeviceContext> context)
        {
            ComPtr<ID3D11Buffer> camera = s_camera;

            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref camera);
        }

        public static void ByParameterReference(ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Buffer> camera) =>
            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref camera);

        public static void ByLocalWithTrailingArguments(ComPtr<ID3D11DeviceContext> context, uint stride, uint offset)
        {
            ComPtr<ID3D11Buffer> camera = s_camera;

            D3D11DeviceContextVtblExtensions.IASetVertexBuffers(context, 0, 1, ref camera, in stride, in offset);
        }

        public static void ByFieldOfALocalStruct(ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Buffer> camera)
        {
            (ComPtr<ID3D11Buffer> Camera, int Other) both = (camera, 0);

            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref both.Camera);
        }

        private static ref readonly uint Computed() => ref Unsafe.NullRef<uint>();
    }
#pragma warning restore S6640, S3459, S1144, S2933, S3358, CS0649, CA1823

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToDictionary(static code => code.Value);

    private static IEnumerable<string> UnpinnedCalls(IEnumerable<Type> types)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type type in types)
        {
            foreach (MethodBase method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                List<Instruction> code = Decode(method);

                for (int at = 0; at < code.Count; at++)
                {
                    if (code[at].Callee is { } callee && !PassesOnlyTheStack(callee, code, at))
                    {
                        yield return $"{type.Name}.{method.Name} -> {callee.Name}";
                    }
                }
            }
        }
    }

    private static bool PassesOnlyTheStack(MethodBase callee, List<Instruction> code, int call)
    {
        if (callee.DeclaringType == typeof(Unsafe) && callee.Name == nameof(Unsafe.AsPointer))
        {
            return false;
        }

        if (callee.DeclaringType?.Assembly.GetName().Name?.StartsWith("Silk.", StringComparison.Ordinal) != true)
        {
            return true;
        }

        if (callee.Name == nameof(ComPtr<ID3D11Buffer>.GetAddressOf) && IsComPtr(callee.DeclaringType))
        {
            return false;
        }

        ParameterInfo[] parameters = callee.GetParameters();

        for (int index = 0; index < parameters.Length; index++)
        {
            Type type = parameters[index].ParameterType;

            if (type.IsByRef && (IsComPtr(type.GetElementType()) || type.GetElementType() is { IsPointer: true })
                && !(PusherOfTrailing(code, call - 1, parameters.Length - 1 - index) is { } pusher && RootedOnTheStack(code, pusher, call)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The instruction that pushed the argument <paramref name="trailing"/> arguments below the top, when each above it is one simple push.</summary>
    private static int? PusherOfTrailing(List<Instruction> code, int at, int trailing)
    {
        for (; trailing > 0; trailing--)
        {
            at = SkipNeutral(code, at);

            if (at < 0 || !IsSinglePush(code[at].Code))
            {
                return null;
            }

            at--;
        }

        return at;
    }

    private static int SkipNeutral(List<Instruction> code, int at)
    {
        while (at >= 0 && (code[at].Code == OpCodes.Nop || code[at].Code == OpCodes.Conv_U || code[at].Code == OpCodes.Conv_I))
        {
            at--;
        }

        return at;
    }

    private static bool IsSinglePush(OpCode code) =>
        code.StackBehaviourPop == StackBehaviour.Pop0
        && code.OperandType != OperandType.InlineMethod
        && code.StackBehaviourPush is StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref;

    /// <summary>
    /// Whether the address at <paramref name="at"/> is a local's or a parameter's (or a field of one that is a struct), or the null
    /// reference, with one path from it to <paramref name="call"/>: a backward walk over a branch reads only the path laid out last.
    /// </summary>
    private static bool RootedOnTheStack(List<Instruction> code, int at, int call)
    {
        at = SkipNeutral(code, at);

        while (at >= 0 && code[at].Code == OpCodes.Ldflda)
        {
            at = SkipNeutral(code, at - 1);
        }

        if (at < 0)
        {
            return false;
        }

        for (int between = at + 1; between <= call; between++)
        {
            if (code[between].IsBranchTarget || code[between].Code.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
            {
                return false;
            }
        }

        OpCode root = code[at].Code;

        return root == OpCodes.Ldloca || root == OpCodes.Ldloca_S || root == OpCodes.Ldarga || root == OpCodes.Ldarga_S
            || code[at].Callee is { Name: nameof(Unsafe.NullRef) };
    }

    private static bool IsComPtr(Type? type) =>
        type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(ComPtr<>);

    /// <summary>One IL instruction: the method it calls when it calls one, and whether anything branches to it.</summary>
    private readonly record struct Instruction(OpCode Code, MethodBase? Callee, bool IsBranchTarget);

    private static List<Instruction> Decode(MethodBase method)
    {
        List<(int Offset, Instruction Instruction)> decoded = [];
        HashSet<int> targets = [];
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            return [];
        }

        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (int at = 0; at < il.Length;)
        {
            int offset = at;
            short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
            OpCode code = OpCodesByValue[value];

            at += code.Size;

            decoded.Add((offset, new Instruction(
                code,
                code.OperandType == OperandType.InlineMethod
                    ? method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments)
                    : null,
                IsBranchTarget: false)));

            switch (code.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                    targets.Add(at + 1 + (sbyte)il[at]);
                    break;
                case OperandType.InlineBrTarget:
                    targets.Add(at + 4 + BitConverter.ToInt32(il, at));
                    break;
                case OperandType.InlineSwitch:
                    int cases = BitConverter.ToInt32(il, at);
                    int after = at + 4 + (4 * cases);

                    for (int index = 0; index < cases; index++)
                    {
                        targets.Add(after + BitConverter.ToInt32(il, at + 4 + (4 * index)));
                    }

                    break;
                default:
                    break;
            }

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };
        }

        return [.. decoded.Select(entry => entry.Instruction with { IsBranchTarget = targets.Contains(entry.Offset) })];
    }
}

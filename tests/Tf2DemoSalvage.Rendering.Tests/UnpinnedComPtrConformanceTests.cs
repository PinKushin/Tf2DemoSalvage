using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>No call in the renderer hands Silk.NET the unpinned address of a <see cref="ComPtr{T}"/> on the managed heap (B706).</summary>
/// <remarks>
/// **Read from Silk.NET 2.23.0** (`ComPtr`1.cs`): `GetAddressOf() => (T**)Unsafe.AsPointer(ref Unsafe.AsRef(in this))`, and
/// every generated `ref ComPtr&lt;T&gt;` overload passes that pointer on. `ref _camera` is a field of a heap object, so a
/// compacting collection on another thread leaves native code reading or writing where the object used to be. CI's
/// `0xC0000005` under `PSSetConstantBuffers` has this shape, reproduced by experiment: B706 holds the table.
///
/// **Read from the compiled IL, not the source**, so `ref _x`, `ref a._b`, `ref _array[i]`, `_x.GetAddressOf()` and an overload
/// nobody thought of are one rule. An address rooted in a local or a parameter is on the stack, which never moves, and
/// `PinnedComCalls` pins the rest. Anything the walk cannot prove is reported, so it fails closed.
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

    /// <summary>The control: the scan names the heap forms in this class's own <see cref="UnpinnedOnPurpose"/> and passes the stack forms.</summary>
    [Test]
    public void UnpinnedCalls_OverEachForm_NamesTheHeapOnesOnly()
    {
        string[] calls = [.. UnpinnedCalls([typeof(UnpinnedOnPurpose)])];

        calls.ShouldBe(
        [
            $"{nameof(UnpinnedOnPurpose)}.{nameof(UnpinnedOnPurpose.ByFieldReference)} -> PSSetConstantBuffers",
            $"{nameof(UnpinnedOnPurpose)}.{nameof(UnpinnedOnPurpose.ByElementReference)} -> PSSetConstantBuffers",
            $"{nameof(UnpinnedOnPurpose)}.{nameof(UnpinnedOnPurpose.ByForwardedReference)} -> PSSetConstantBuffers",
            $"{nameof(UnpinnedOnPurpose)}.{nameof(UnpinnedOnPurpose.ByFieldAddress)} -> GetAddressOf",
        ],
        ignoreOrder: true);
    }

#pragma warning disable S6640, S3459, S1144, S2933, CS0649, CA1823 // The control holds the very calls the scan refuses; it is never run, only read.
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

        public static nint ByFieldAddress() => (nint)s_camera.GetAddressOf();

        public static void ByLocalReference(ComPtr<ID3D11DeviceContext> context)
        {
            ComPtr<ID3D11Buffer> camera = s_camera;

            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref camera);
        }

        public static void ByParameterReference(ComPtr<ID3D11DeviceContext> context, ComPtr<ID3D11Buffer> camera) =>
            D3D11DeviceContextVtblExtensions.PSSetConstantBuffers(context, 0, 1, ref camera);

        public static nint ByLocalStructFieldAddress(ComPtr<ID3D11Buffer> camera)
        {
            (ComPtr<ID3D11Buffer> Camera, int Other) both = (camera, 0);

            return (nint)both.Camera.GetAddressOf();
        }
    }
#pragma warning restore S6640, S3459, S1144, S2933, CS0649, CA1823

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
        if (callee.DeclaringType?.Assembly.GetName().Name?.StartsWith("Silk.", StringComparison.Ordinal) != true)
        {
            return true;
        }

        if (callee.Name == nameof(ComPtr<ID3D11Buffer>.GetAddressOf) && IsComPtr(callee.DeclaringType))
        {
            return RootedOnTheStack(code, call - 1);
        }

        ParameterInfo[] parameters = callee.GetParameters();

        for (int index = 0; index < parameters.Length; index++)
        {
            Type type = parameters[index].ParameterType;

            if (type.IsByRef && IsComPtr(type.GetElementType())
                && !(PusherOfTrailing(code, call - 1, parameters.Length - 1 - index) is { } pusher && RootedOnTheStack(code, pusher)))
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

    /// <summary>Whether the address at <paramref name="at"/> is a local's or a parameter's (or a field of one that is a struct), or the null reference.</summary>
    private static bool RootedOnTheStack(List<Instruction> code, int at)
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

        OpCode root = code[at].Code;

        return root == OpCodes.Ldloca || root == OpCodes.Ldloca_S || root == OpCodes.Ldarga || root == OpCodes.Ldarga_S
            || code[at].Callee is { Name: nameof(System.Runtime.CompilerServices.Unsafe.NullRef) };
    }

    private static bool IsComPtr(Type? type) =>
        type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(ComPtr<>);

    /// <summary>One IL instruction, with the method it calls when it calls one.</summary>
    private readonly record struct Instruction(OpCode Code, MethodBase? Callee);

    private static List<Instruction> Decode(MethodBase method)
    {
        List<Instruction> decoded = [];
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            return decoded;
        }

        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (int at = 0; at < il.Length;)
        {
            short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
            OpCode code = OpCodesByValue[value];

            at += code.Size;

            decoded.Add(new Instruction(
                code,
                code.OperandType == OperandType.InlineMethod
                    ? method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments)
                    : null));

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

        return decoded;
    }
}

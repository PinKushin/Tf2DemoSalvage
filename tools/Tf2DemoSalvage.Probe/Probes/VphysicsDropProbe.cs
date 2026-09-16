using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Drives the shipped <c>vphysics.dll</c> through its own public interfaces — <c>CreateInterface</c>, <c>IPhysics</c>,
/// <c>IPhysicsCollision</c>, <c>IPhysicsSurfaceProps</c>, <c>IPhysicsEnvironment</c>, <c>IPhysicsObject</c> — exactly as the
/// engine calls them, to drop an 8×8×8 box onto a static slab and print its trajectory (milestone 1).
/// </summary>
/// <remarks>
/// <para>
/// **Every address below is the vtable slot's contents, read from the binary at runtime — never a hardcoded function
/// address.** The constants here are only the SLOT INDEX, counted from the SDK's virtual declaration order
/// (<c>public/vphysics_interface.h</c>, <c>public/appframework/IAppSystem.h</c>; a virtual destructor takes slot 0 when a
/// class declares one). Each was verified once against this binary via GhidraMCP before being trusted:
/// </para>
/// <code>
/// CreateInterface (export, 1800b8770): walks a linked list of {factory, name, next} nodes (InterfaceReg) starting at
///   DAT_180135b00, string-compares, calls the factory - matches tier1/interface.h's CreateInterfaceFn exactly.
/// "VPhysics031"/"VPhysicsCollision007"/"VPhysicsSurfaceProps001" (1800ea670/1800eabe0/1800ec448): each has one code xref,
///   a lea/lea/lea+jmp stub that constructs an InterfaceReg(this, factory, name); the factory in turn is `lea rax,[singleton]; ret`
///   - singletons at 18011f020 (IPhysics), 18011f0d8 (IPhysicsCollision), 180120b38 (IPhysicsSurfaceProps). The last one is the
///   exact address docs/findings/51 already names as "the props object", cross-confirming the method.
/// IPhysics (vtable read from the singleton at runtime, static image address 1800ea608):
///   slot 5  CreateEnvironment  - FUN_180004ec0 allocates via FUN_180012cd0 (which news a 0xd8-byte CPhysicsEnvironment) and
///                                appends it to a growable list, then returns it - matches "creates and tracks an environment".
/// IPhysicsCollision (static image address 1800eaa40):
///   slot 29 BBoxToCollide      - FUN_180009ec0(this, mins*, maxs*) checks a 0x20-byte-per-entry mins/maxs/pointer cache before
///                                building a new one - the exact shape of the documented "bbox cache" in vphysics_interface.h.
/// IPhysicsSurfaceProps (static image address 1800ec598, singleton 180120b38):
///   slot 1  ParseSurfaceData   - FUN_180018740; docs/findings/51 already names this exact address independently (D172 port).
///   slot 3  GetSurfaceIndex    - FUN_180018500; also independently named in docs/findings/51.
/// IPhysicsEnvironment (vtable is per-instance; the constructor CPhysicsEnvironment::CPhysicsEnvironment at 1800114f0 sets
///   `*this = &amp;PTR_FUN_1800ebbd8`, so 1800ebbd8 is this build's one and only environment vtable):
///   slot 3  SetGravity              - CPhysicsEnvironment::SetGravity, named by Ghidra already (1800150f0); logs
///                                     "Set Gravity %.1f" and scales/swaps axes into IVP's frame (HL Z becomes IVP -Y).
///   slot 4  GetGravity              - 1800136d0; the exact inverse of slot 3's scale-and-swap, writing through the 2nd arg.
///   slot 7  CreatePolyObject        - FUN_180012d40(env, collide, materialIndex, position*, angles*, params*) tail-calls the
///                                     same builder as slot 8 with a literal 0 ("not static").
///   slot 8  CreatePolyObjectStatic  - FUN_180012da0; identical to slot 7 but passes 1 ("static") to the shared builder
///                                     FUN_18001b340 - the one bit the header says is the whole difference between the two.
///   slot 34 Simulate                - CPhysicsEnvironment::Simulate, named by Ghidra already (180015310); `void Simulate(this,
///                                     float deltaTime)` - only runs its body `if (param_1[1] != 0)` (the internal IVP world,
///                                     set unconditionally in the constructor), and the one external-callback use inside it
///                                     (a friction/scrape report) is itself null-checked before the call - so an environment
///                                     with no collision solver, no event handler and no debug overlay set is not a null
///                                     dereference here.
///   slot 36 GetSimulationTimestep   - 1800139d0: reads a stored double, narrows to float, returns.
///   slot 37 SetSimulationTimestep   - 1800152f0: converts the float arg to double and tail-calls a shared setter.
/// IPhysicsObject (vtable is per-instance; FUN_18001b340 sets a fresh 0x60-byte object's `*this = &amp;PTR_FUN_1800ecbf0`, so
///   1800ecbf0 is this build's one and only polygonal-object vtable):
///   slot 15 EnableMotion  - 18001ba90(this, bool): no-ops when IsStatic() (slot 1) is true, else compares against
///                           IsMotionEnabled() (slot 9) and flips IvpCollisionObject::SetMovable when it changed.
///   slot 24 Wake          - 18001e3d0: loads the inner IVP object at this+0x10 and tail-jumps to the shared waker - a bare
///                           no-argument call, matching `virtual void Wake(void) = 0`.
///   slot 47 GetPosition   - 18001c030(this, Vector* out, QAngle* out): scales/swaps the stored position by the same
///                           HL-per-IVP-unit constant slot 4 uses, the inverse of slot 3's SetGravity transform.
///   slot 51 GetVelocity   - 18001c1f0(this, Vector* out, AngularImpulse* out): both output pointers null-checked before use,
///                           matching the header's "pass NULL for either to avoid computations".
/// </code>
/// <para>
/// **No collision solver, event handler or debug overlay is installed.** <c>Simulate</c>'s one call into an external callback
/// is null-guarded (see the slot 34 note above), and nothing else in this scene needs one for milestone 1 - a static slab and
/// one falling box never touch a constraint, a fluid, or a vehicle.
/// </para>
/// </remarks>
public sealed class VphysicsDropProbe : IProbe
{
    private const string PhysicsVersion = "VPhysics031";
    private const string CollisionVersion = "VPhysicsCollision007";
    private const string SurfacePropsVersion = "VPhysicsSurfaceProps001";

    private const int PhysicsCreateEnvironmentSlot = 5;
    private const int CollisionBBoxToCollideSlot = 29;
    private const int SurfacePropsParseSurfaceDataSlot = 1;
    private const int SurfacePropsGetSurfaceIndexSlot = 3;
    private const int EnvironmentSetGravitySlot = 3;
    private const int EnvironmentGetGravitySlot = 4;
    private const int EnvironmentCreatePolyObjectSlot = 7;
    private const int EnvironmentCreatePolyObjectStaticSlot = 8;
    private const int EnvironmentSimulateSlot = 34;
    private const int EnvironmentGetSimulationTimestepSlot = 36;
    private const int EnvironmentSetSimulationTimestepSlot = 37;
    private const int ObjectEnableMotionSlot = 15;
    private const int ObjectWakeSlot = 24;
    private const int ObjectGetPositionSlot = 47;
    private const int ObjectGetVelocitySlot = 51;

    private const int TotalTicks = 660;
    private const int PrintEveryTicks = 33;
    private const float Timestep = 1f / 66f;

    /// <summary>A minimal <c>surfaceproperties.txt</c>-shaped text defining exactly one surface, <c>default</c>.</summary>
    /// <remarks>
    /// The tokenizer behind <c>ParseSurfaceData</c> (read from the disassembly for the ported
    /// <see cref="global::Tf2DemoSalvage.Animation.Animating.VphysicsSurfaceData"/>) treats quotes as ordinary delimiters, not a
    /// requirement, but real shipped files quote every key and value and this does the same.
    /// </remarks>
    private const string SurfaceText = """
        "default"
        {
        "friction"      "0.8"
        "elasticity"    "0.25"
        "density"       "2700"
        "thickness"     "-1"
        "dampening"     "0"
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Vec3(float x, float y, float z)
    {
        public readonly float X = x;
        public readonly float Y = y;
        public readonly float Z = z;

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture, $"({X:F2}, {Y:F2}, {Z:F2})");
    }

    /// <summary><c>objectparams_t</c>, <c>public/vphysics_interface.h:1062-1075</c>; x64 layout, pointer-aligned.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectParams
    {
        public nint MassCenterOverride;
        public float Mass;
        public float Inertia;
        public float Damping;
        public float RotDamping;
        public float RotInertiaLimit;
        public nint Name;
        public nint GameData;
        public float Volume;
        public float DragCoefficient;

        [MarshalAs(UnmanagedType.I1)]
        public bool EnableCollisions;

        /// <summary><c>g_PhysDefaultObjectParams</c>, <c>game/shared/physics_shared.cpp:43-56</c>, with a caller-chosen mass and name.</summary>
        public static ObjectParams Default(float mass, nint name) => new()
        {
            MassCenterOverride = 0,
            Mass = mass,
            Inertia = 1.0f,
            Damping = 0.1f,
            RotDamping = 0.1f,
            RotInertiaLimit = 0.05f,
            Name = name,
            GameData = 0,
            Volume = 0f,
            DragCoefficient = 1.0f,
            EnableCollisions = true,
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateInterfaceDelegate([MarshalAs(UnmanagedType.LPStr)] string name, out int returnCode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateEnvironmentDelegate(nint physics);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint BBoxToCollideDelegate(nint collision, in Vec3 mins, in Vec3 maxs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ParseSurfaceDataDelegate(
        nint surfaceProps, [MarshalAs(UnmanagedType.LPStr)] string filename, [MarshalAs(UnmanagedType.LPStr)] string text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetSurfaceIndexDelegate(nint surfaceProps, [MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetGravityDelegate(nint environment, in Vec3 gravity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetGravityDelegate(nint environment, out Vec3 gravity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetSimulationTimestepDelegate(nint environment, float timestep);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float GetSimulationTimestepDelegate(nint environment);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreatePolyObjectDelegate(
        nint environment, nint collide, int materialIndex, in Vec3 position, in Vec3 angles, ref ObjectParams objectParams);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SimulateDelegate(nint environment, float deltaTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnableMotionDelegate(nint physicsObject, [MarshalAs(UnmanagedType.I1)] bool enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WakeDelegate(nint physicsObject);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetPositionDelegate(nint physicsObject, out Vec3 position, out Vec3 angles);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetVelocityDelegate(nint physicsObject, out Vec3 velocity, out Vec3 angularVelocity);

    /// <inheritdoc />
    public string Name => "vphysics-drop";

    /// <inheritdoc />
    public string Summary =>
        "drives the shipped vphysics.dll through CreateInterface/IPhysics/IPhysicsCollision/IPhysicsSurfaceProps as the " +
        "engine does, drops an 8x8x8 box on a static slab and prints its trajectory: vphysics-drop";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        nint createInterfaceExport = NativeLibrary.GetExport(module, "CreateInterface");
        CreateInterfaceDelegate createInterface =
            Marshal.GetDelegateForFunctionPointer<CreateInterfaceDelegate>(createInterfaceExport);

        if (!TryCreate(output, createInterface, PhysicsVersion, out nint physics) ||
            !TryCreate(output, createInterface, CollisionVersion, out nint collision) ||
            !TryCreate(output, createInterface, SurfacePropsVersion, out nint surfaceProps))
        {
            return;
        }

        output.WriteLine(
            $"IPhysics={physics:x} IPhysicsCollision={collision:x} IPhysicsSurfaceProps={surfaceProps:x}");
        output.Flush();

        int parsed = VCall<ParseSurfaceDataDelegate>(surfaceProps, SurfacePropsParseSurfaceDataSlot)(
            surfaceProps, "probe.txt", SurfaceText);
        int materialIndex = VCall<GetSurfaceIndexDelegate>(surfaceProps, SurfacePropsGetSurfaceIndexSlot)(
            surfaceProps, "default");
        output.WriteLine($"ParseSurfaceData -> {parsed} entries; GetSurfaceIndex(\"default\") -> {materialIndex}");
        output.Flush();

        if (materialIndex < 0)
        {
            output.WriteLine("No 'default' surface parsed; aborting.");
            return;
        }

        nint environment = VCall<CreateEnvironmentDelegate>(physics, PhysicsCreateEnvironmentSlot)(physics);
        output.WriteLine($"IPhysicsEnvironment={environment:x}");
        output.Flush();

        Vec3 gravity = new(0f, 0f, -800f);
        VCall<SetGravityDelegate>(environment, EnvironmentSetGravitySlot)(environment, gravity);
        VCall<GetGravityDelegate>(environment, EnvironmentGetGravitySlot)(environment, out Vec3 readGravity);
        output.WriteLine($"control: GetGravity -> {readGravity} (set {gravity})");

        VCall<SetSimulationTimestepDelegate>(environment, EnvironmentSetSimulationTimestepSlot)(environment, Timestep);
        float readTimestep = VCall<GetSimulationTimestepDelegate>(environment, EnvironmentGetSimulationTimestepSlot)(environment);
        output.WriteLine($"control: GetSimulationTimestep -> {readTimestep:F6} (set {Timestep:F6})");
        output.Flush();

        nint staticCollide = VCall<BBoxToCollideDelegate>(collision, CollisionBBoxToCollideSlot)(
            collision, new Vec3(-500f, -500f, -10f), new Vec3(500f, 500f, 0f));
        nint dynamicCollide = VCall<BBoxToCollideDelegate>(collision, CollisionBBoxToCollideSlot)(
            collision, new Vec3(-4f, -4f, -4f), new Vec3(4f, 4f, 4f));

        nint staticName = Marshal.StringToHGlobalAnsi("static-slab");
        nint dynamicName = Marshal.StringToHGlobalAnsi("dropped-box");

        try
        {
            ObjectParams staticParams = ObjectParams.Default(mass: 1.0f, staticName);
            nint staticObject = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectStaticSlot)(
                environment, staticCollide, materialIndex, new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f), ref staticParams);

            VCall<GetPositionDelegate>(staticObject, ObjectGetPositionSlot)(staticObject, out Vec3 staticPosition, out _);
            output.WriteLine($"control: static slab GetPosition -> {staticPosition} (expected (0.00, 0.00, 0.00))");

            ObjectParams dynamicParams = ObjectParams.Default(mass: 10.0f, dynamicName);
            nint dynamicObject = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectSlot)(
                environment, dynamicCollide, materialIndex, new Vec3(0f, 0f, 64f), new Vec3(0f, 0f, 0f), ref dynamicParams);

            VCall<GetPositionDelegate>(dynamicObject, ObjectGetPositionSlot)(dynamicObject, out Vec3 dynamicPosition, out _);
            output.WriteLine($"control: dropped box GetPosition -> {dynamicPosition} (expected (0.00, 0.00, 64.00))");
            output.Flush();

            VCall<EnableMotionDelegate>(dynamicObject, ObjectEnableMotionSlot)(dynamicObject, true);
            VCall<WakeDelegate>(dynamicObject, ObjectWakeSlot)(dynamicObject);

            SimulateDelegate simulate = VCall<SimulateDelegate>(environment, EnvironmentSimulateSlot);
            GetPositionDelegate getPosition = VCall<GetPositionDelegate>(dynamicObject, ObjectGetPositionSlot);
            GetVelocityDelegate getVelocity = VCall<GetVelocityDelegate>(dynamicObject, ObjectGetVelocitySlot);

            for (int tick = 1; tick <= TotalTicks; tick++)
            {
                simulate(environment, Timestep);

                if (tick % PrintEveryTicks != 0)
                {
                    continue;
                }

                getPosition(dynamicObject, out Vec3 position, out Vec3 angles);
                getVelocity(dynamicObject, out Vec3 velocity, out Vec3 angularVelocity);
                output.WriteLine(
                    $"tick {tick,4} t={tick * Timestep,6:F3}  pos={position}  angles={angles}  vel={velocity}  spin={angularVelocity}");
                output.Flush();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(staticName);
            Marshal.FreeHGlobal(dynamicName);
        }
    }

    private static bool TryCreate(TextWriter output, CreateInterfaceDelegate createInterface, string version, out nint result)
    {
        result = createInterface(version, out int returnCode);

        if (result != 0 && returnCode == 0)
        {
            return true;
        }

        output.WriteLine($"CreateInterface(\"{version}\") failed: returnCode={returnCode}, pointer={result:x}");
        return false;
    }

    /// <summary>Reads an object's vtable pointer and one slot from it, and binds a delegate to that function pointer.</summary>
    private static T VCall<T>(nint instance, int slot)
        where T : Delegate
    {
        nint vtable = Marshal.ReadIntPtr(instance);
        nint function = Marshal.ReadIntPtr(vtable, slot * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(function);
    }
}

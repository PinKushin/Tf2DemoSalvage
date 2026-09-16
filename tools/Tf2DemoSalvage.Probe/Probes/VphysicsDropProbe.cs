using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Drives the shipped <c>vphysics.dll</c> through its own public interfaces — <c>CreateInterface</c>, <c>IPhysics</c>,
/// <c>IPhysicsCollision</c>, <c>IPhysicsSurfaceProps</c>, <c>IPhysicsEnvironment</c>, <c>IPhysicsObject</c> — exactly as the
/// engine calls them, to drop an 8×8×8 box onto a static slab and print its trajectory (milestone 1), or — <c>phy</c> mode —
/// a real model's own <c>.phy</c> collide on a real static model's, for the B369 differential against <see
/// cref="global::Tf2DemoSalvage.Animation.Animating.IvpSimulation"/>.
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
///                                **Read at runtime from this build and matched against 1800eaa40+29*8 as a cross-check on the
///                                slot-counting method itself** (B369): the vtable word at that offset IS 180009ec0.
///   slot 36 VCollideLoad       - FUN_18000a100, read at 1800eaa40+36*8=1800eab20 (=0x18000a100 in this build): zeroes a
///                                24-byte <c>vcollide_t</c> (<c>public/vcollide.h</c>: <c>ushort solidCount:15/isPacked:1</c>,
///                                <c>ushort descSize</c>, <c>CPhysCollide **solids</c> at +8, <c>char *pKeyValues</c> at +16),
///                                then for <c>solidCount</c> length-prefixed blobs: a blob under 0x30 bytes with no <c>VPHY</c>
///                                tag calls <c>Error("Corrupt physics model")</c> (the exact string `docs/findings` already
///                                cites for <see cref="global::Tf2DemoSalvage.Content.Assets.PhysicsModel"/>'s own B404 note on
///                                this same function address); otherwise <c>FUN_18000bcf0</c> allocates its OWN buffer and
///                                <c>memcpy</c>s the solid's bytes in (so the caller's buffer need not outlive the call), builds
///                                the <c>CPhysCollide</c> vtable (<c>PTR_FUN_1800eaf70</c>) into it, and files the pointer into
///                                <c>solids[i]</c>; the bytes left over after all solids are likewise copied into a fresh
///                                allocation as <c>pKeyValues</c>. **Matches the header's declared signature exactly**
///                                (<c>VCollideLoad( vcollide_t *pOutput, int solidCount, const char *pBuffer, int size, bool
///                                swap = false )</c>) and every published caller (<c>studiobyteswap.cpp:514</c>,
///                                <c>bsplib.cpp:1681</c>) passes <c>pBuffer</c> starting AFTER the file's own header and
///                                <c>size</c> as the remaining byte count — exactly <see cref="global::Tf2DemoSalvage.Content.Assets.PhysicsModel"/>'s
///                                own <c>HeaderSize</c> (16, <c>phyheader_t</c>) skip.
///   slot 37 VCollideUnload     - FUN_18000a330, read at 1800eab28 (=0x18000a330): walks <c>solidCount</c> solids, calls each
///                                non-null one's own vtable slot 0 (<c>(*collide->vtbl)(collide, 1)</c> - a virtual destructor
///                                taking the "free memory" flag `sub_matter` conventions give it slot 0 for the same reason
///                                <c>IPhysicsCollision</c>'s own destructor is slot 0), frees the solids array and the
///                                <c>pKeyValues</c> buffer, and zeroes the struct - the exact inverse of <c>VCollideLoad</c>.
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
/// one falling box never touch a constraint, a fluid, or a vehicle. The same is true of <c>phy</c> mode's two poly objects.
/// </para>
/// <para>
/// **`phy` mode's surfaces come from the game's own manifest, not the synthetic single-entry text below.** A real `.phy`
/// names a real surface (`wood`, `default`, …) that the synthetic text below does not define, so <c>GetSurfaceIndex</c> would
/// answer −1 for it; this mode instead replays <c>scripts/surfaceproperties_manifest.txt</c> through the shipped
/// <c>ParseSurfaceData</c> exactly as <see cref="global::Tf2DemoSalvage.Scene.GameContent"/>'s own <c>ReadSurfaces</c> replays it
/// through the ported one, so both sides of the B369 differential resolve the same name against the same numbers.
/// </para>
/// </remarks>
public sealed class VphysicsDropProbe : IProbe
{
    private const string PhysicsVersion = "VPhysics031";
    private const string CollisionVersion = "VPhysicsCollision007";
    private const string SurfacePropsVersion = "VPhysicsSurfaceProps001";

    private const int PhysicsCreateEnvironmentSlot = 5;
    private const int CollisionBBoxToCollideSlot = 29;
    private const int CollisionVCollideLoadSlot = 36;
    private const int CollisionVCollideUnloadSlot = 37;
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

    /// <summary>Where the box scene drops from, and <c>phy</c> mode's own default when no <c>z</c> is given.</summary>
    private const float DefaultDropHeight = 64f;

    /// <summary><c>phyheader_t</c>'s size (<c>phyfile.h:14-21</c>) — <see cref="global::Tf2DemoSalvage.Content.Assets.PhysicsModel"/>'s own skip, and every published <c>VCollideLoad</c> caller's.</summary>
    private const int PhyHeaderSize = 16;

    /// <summary>The mass a solid's own <c>.phy</c> did not state — matched to <see cref="global::Tf2DemoSalvage.Content.Assets.PhysicsModel"/>'s "no mass key" reading of zero.</summary>
    private const float FallbackMass = 10f;

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

    /// <summary><c>vcollide_t</c>, <c>public/vcollide.h</c> — 24 bytes, the pointer fields 8-byte aligned exactly as the disassembly reads them.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VCollide
    {
        /// <summary>Bits 0-14 are <c>solidCount</c>, bit 15 is <c>isPacked</c> — read together as the disassembly's <c>&amp; 0x7fff</c> does.</summary>
        public ushort SolidCountAndPacked;
        public ushort DescSize;
        public nint Solids;
        public nint KeyValues;

        public readonly int SolidCount => SolidCountAndPacked & 0x7fff;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateInterfaceDelegate([MarshalAs(UnmanagedType.LPStr)] string name, out int returnCode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateEnvironmentDelegate(nint physics);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint BBoxToCollideDelegate(nint collision, in Vec3 mins, in Vec3 maxs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VCollideLoadDelegate(
        nint collision, nint output, int solidCount, nint buffer, int size, [MarshalAs(UnmanagedType.I1)] bool swap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VCollideUnloadDelegate(nint collision, nint vcollide);

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
        "engine does, drops an 8x8x8 box on a static slab and prints its trajectory: vphysics-drop " +
        "| vphysics-drop phy <dynamicModel.mdl> <staticModel.mdl> [z] [every]  -- the same rig, using each model's own .phy solid 0";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count > 0 && string.Equals(arguments[0], "phy", StringComparison.OrdinalIgnoreCase))
        {
            RunPhy(output, arguments);
        }
        else
        {
            RunBox(output);
        }
    }

    private static void RunBox(TextWriter output)
    {
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

        SetGravityAndTimestep(output, environment);

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

            ObjectParams dynamicParams = ObjectParams.Default(mass: 10.0f, dynamicName);
            nint dynamicObject = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectSlot)(
                environment, dynamicCollide, materialIndex, new Vec3(0f, 0f, DefaultDropHeight), new Vec3(0f, 0f, 0f), ref dynamicParams);

            Drop(
                output, environment, staticObject, dynamicObject,
                "static slab", new Vec3(0f, 0f, 0f), "dropped box", new Vec3(0f, 0f, DefaultDropHeight), PrintEveryTicks);
        }
        finally
        {
            Marshal.FreeHGlobal(staticName);
            Marshal.FreeHGlobal(dynamicName);
        }
    }

    /// <summary>
    /// The same rig as <see cref="RunBox"/>, but each object's collide is solid 0 of a real model's own <c>.phy</c>, read
    /// through the game's own content path and loaded by the shipped <c>VCollideLoad</c> — never through this project's
    /// own <see cref="global::Tf2DemoSalvage.Content.Assets.PhysicsModel"/> reader, so the two readings cannot agree by
    /// sharing a bug.
    /// </summary>
    private static void RunPhy(TextWriter output, IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 3)
        {
            output.WriteLine("vphysics-drop phy <dynamicModel.mdl> <staticModel.mdl> [z] [every]");
            return;
        }

        string dynamicModel = arguments[1];
        string staticModel = arguments[2];
        float z = arguments.Count > 3
            ? float.Parse(arguments[3], NumberStyles.Float, CultureInfo.InvariantCulture)
            : DefaultDropHeight;
        int every = arguments.Count > 4 ? int.Parse(arguments[4], CultureInfo.InvariantCulture) : PrintEveryTicks;

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
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

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        int surfaceFiles = ParseRealSurfaces(game, surfaceProps);
        output.WriteLine($"ParseSurfaceData -> {surfaceFiles} manifest file(s) parsed from scripts/surfaceproperties_manifest.txt");
        output.Flush();

        nint environment = VCall<CreateEnvironmentDelegate>(physics, PhysicsCreateEnvironmentSlot)(physics);
        output.WriteLine($"IPhysicsEnvironment={environment:x}");
        output.Flush();

        SetGravityAndTimestep(output, environment);

        LoadedPhy? staticPhy = LoadCollide(output, game, collision, surfaceProps, staticModel);
        LoadedPhy? dynamicPhy = staticPhy is null ? null : LoadCollide(output, game, collision, surfaceProps, dynamicModel);

        if (staticPhy is not { } staticLoaded || dynamicPhy is not { } dynamicLoaded)
        {
            if (staticPhy is { } toFree)
            {
                FreeCollide(collision, toFree);
            }

            return;
        }

        nint staticName = Marshal.StringToHGlobalAnsi(staticModel);
        nint dynamicName = Marshal.StringToHGlobalAnsi(dynamicModel);

        try
        {
            output.WriteLine(
                $"static  '{staticModel}': solid 0 surfaceprop '{staticLoaded.SurfaceProp}' -> material {staticLoaded.MaterialIndex}, " +
                $"mass {staticLoaded.Mass:F2}{(staticLoaded.MassFromFile ? string.Empty : " (fallback, .phy had none)")}");
            output.WriteLine(
                $"dynamic '{dynamicModel}': solid 0 surfaceprop '{dynamicLoaded.SurfaceProp}' -> material {dynamicLoaded.MaterialIndex}, " +
                $"mass {dynamicLoaded.Mass:F2}{(dynamicLoaded.MassFromFile ? string.Empty : " (fallback, .phy had none)")}");
            output.Flush();

            ObjectParams staticParams = staticLoaded.Parameters(staticName);
            nint staticObject = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectStaticSlot)(
                environment, staticLoaded.Collide, staticLoaded.MaterialIndex, new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f),
                ref staticParams);

            ObjectParams dynamicParams = dynamicLoaded.Parameters(dynamicName);
            output.WriteLine(
                $"params: inertia {dynamicParams.Inertia:F2} damping {dynamicParams.Damping:F2} rotdamping {dynamicParams.RotDamping:F2} " +
                $"volume {dynamicParams.Volume:F2} drag {dynamicParams.DragCoefficient:F2}");
            nint dynamicObject = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectSlot)(
                environment, dynamicLoaded.Collide, dynamicLoaded.MaterialIndex, new Vec3(0f, 0f, z), new Vec3(0f, 0f, 0f),
                ref dynamicParams);

            Drop(
                output, environment, staticObject, dynamicObject,
                staticModel, new Vec3(0f, 0f, 0f), dynamicModel, new Vec3(0f, 0f, z), every);
        }
        finally
        {
            Marshal.FreeHGlobal(staticName);
            Marshal.FreeHGlobal(dynamicName);
            FreeCollide(collision, staticLoaded);
            FreeCollide(collision, dynamicLoaded);
        }
    }

    /// <summary>Sets gravity and the timestep, printing the control read-back of each — shared by both modes.</summary>
    private static void SetGravityAndTimestep(TextWriter output, nint environment)
    {
        Vec3 gravity = new(0f, 0f, -800f);
        VCall<SetGravityDelegate>(environment, EnvironmentSetGravitySlot)(environment, gravity);
        VCall<GetGravityDelegate>(environment, EnvironmentGetGravitySlot)(environment, out Vec3 readGravity);
        output.WriteLine($"control: GetGravity -> {readGravity} (set {gravity})");

        VCall<SetSimulationTimestepDelegate>(environment, EnvironmentSetSimulationTimestepSlot)(environment, Timestep);
        float readTimestep = VCall<GetSimulationTimestepDelegate>(environment, EnvironmentGetSimulationTimestepSlot)(environment);
        output.WriteLine($"control: GetSimulationTimestep -> {readTimestep:F6} (set {Timestep:F6})");
        output.Flush();
    }

    /// <summary>The creation-frame control read-backs, the wake, and the stepped trajectory — shared by both modes.</summary>
    private static void Drop(
        TextWriter output,
        nint environment,
        nint staticObject,
        nint dynamicObject,
        string staticLabel,
        Vec3 staticExpected,
        string dynamicLabel,
        Vec3 dynamicExpected,
        int every)
    {
        VCall<GetPositionDelegate>(staticObject, ObjectGetPositionSlot)(staticObject, out Vec3 staticPosition, out _);
        output.WriteLine($"control: {staticLabel} GetPosition -> {staticPosition} (expected {staticExpected})");

        VCall<GetPositionDelegate>(dynamicObject, ObjectGetPositionSlot)(dynamicObject, out Vec3 dynamicPosition, out _);
        output.WriteLine($"control: {dynamicLabel} GetPosition -> {dynamicPosition} (expected {dynamicExpected})");
        output.Flush();

        // The IVP core behind the object (`object+0x10` → `+0xe8`): inertia `+0x20..0x28`, its reciprocals `+0x40..0x48`,
        // inverse mass `+0x4c`, mass `+0x2c`? — printed as raw floats so the port's own core can be set beside them.
        nint core = Marshal.ReadIntPtr(Marshal.ReadIntPtr(dynamicObject + 0x10) + 0xe8);
        output.WriteLine(
            string.Create(CultureInfo.InvariantCulture,
                $"core: inertia ({CoreFloat(core, 0x20):g9}, {CoreFloat(core, 0x24):g9}, {CoreFloat(core, 0x28):g9})  " +
                $"inverse ({CoreFloat(core, 0x40):g9}, {CoreFloat(core, 0x44):g9}, {CoreFloat(core, 0x48):g9})  " +
                $"inverse mass {CoreFloat(core, 0x4c):g9}"));

        VCall<EnableMotionDelegate>(dynamicObject, ObjectEnableMotionSlot)(dynamicObject, true);
        VCall<WakeDelegate>(dynamicObject, ObjectWakeSlot)(dynamicObject);

        SimulateDelegate simulate = VCall<SimulateDelegate>(environment, EnvironmentSimulateSlot);
        GetPositionDelegate getPosition = VCall<GetPositionDelegate>(dynamicObject, ObjectGetPositionSlot);
        GetVelocityDelegate getVelocity = VCall<GetVelocityDelegate>(dynamicObject, ObjectGetVelocitySlot);

        for (int tick = 1; tick <= TotalTicks; tick++)
        {
            simulate(environment, Timestep);

            if (tick % every != 0)
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

    /// <summary>Replays the game's own surface-properties manifest through the shipped <c>ParseSurfaceData</c> — <c>GameContent.ReadSurfaces</c>'s route, for the DLL instead of the port.</summary>
    /// <returns>How many manifest <c>file</c> entries were parsed.</returns>
    private static int ParseRealSurfaces(GameContent game, nint surfaceProps)
    {
        const string Manifest = "scripts/surfaceproperties_manifest.txt";

        if (game.Archives.Read(Manifest) is not { Length: > 0 } manifest)
        {
            return 0;
        }

        int files = 0;

        KeyValuesReader.Read(manifest, (key, value, depth) =>
        {
            if (depth != 1 || value is null || !string.Equals(key, "file", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (game.Archives.Read(value) is { } text)
            {
                VCall<ParseSurfaceDataDelegate>(surfaceProps, SurfacePropsParseSurfaceDataSlot)(
                    surfaceProps, value, System.Text.Encoding.Latin1.GetString(text));
                files++;
            }

            return true;
        });

        return files;
    }

    /// <summary>One model's solid 0, loaded by the shipped <c>VCollideLoad</c> and resolved to a material — <c>phy</c> mode's per-model state.</summary>
    /// <param name="Collide">Solid 0's <c>CPhysCollide*</c>, ready for <c>CreatePolyObject(Static)</c>.</param>
    /// <param name="VCollide">The unmanaged <c>vcollide_t</c> this solid's collide lives in — kept to free with <see cref="FreeCollide"/>.</param>
    /// <param name="MaterialIndex">The resolved surface index — the solid's own name, else <c>default</c>.</param>
    /// <param name="Mass">The solid's own mass, or <see cref="FallbackMass"/> when the <c>.phy</c> named none.</param>
    /// <param name="MassFromFile">Whether <see cref="Mass"/> came from the file rather than the fallback.</param>
    /// <param name="SurfaceProp">The name actually resolved against, for the printed line.</param>
    /// <param name="Solid">The solid's own text block, whose inertia and damping the object is created with.</param>
    private sealed record LoadedPhy(
        nint Collide, nint VCollide, int MaterialIndex, float Mass, bool MassFromFile, string SurfaceProp, PhysicsSolid Solid)
    {
        /// <summary>The solid's own parameters over <c>g_PhysDefaultObjectParams</c>, as the prop path's key parser fills them.</summary>
        public ObjectParams Parameters(nint name) =>
            ObjectParams.Default(Mass, name) with
            {
                Inertia = Solid.Inertia,
                Damping = Solid.Damping,
                RotDamping = Solid.RotationDamping,
                Volume = Solid.Volume,
                DragCoefficient = Solid.DragCoefficient,
            };
    }

    /// <summary>Reads a model's <c>.phy</c>, loads it through the shipped <c>VCollideLoad</c>, and resolves solid 0's material.</summary>
    private static LoadedPhy? LoadCollide(TextWriter output, GameContent game, nint collision, nint surfaceProps, string model)
    {
        string physicsPath = Path.ChangeExtension(model, ".phy");

        if (game.Archives.Read(model) is null)
        {
            output.WriteLine($"{model}: not in the game's content");
            return null;
        }

        if (game.Archives.Read(physicsPath) is not { } bytes)
        {
            output.WriteLine($"{physicsPath}: not in the game's content");
            return null;
        }

        PhysicsModel parsed;

        try
        {
            parsed = PhysicsModel.Read(bytes);
        }
        catch (InvalidDataException failure)
        {
            output.WriteLine($"{physicsPath}: {failure.Message}");
            return null;
        }

        if (parsed.Solids.Count == 0)
        {
            output.WriteLine($"{physicsPath}: no solids");
            return null;
        }

        PhysicsSolid solid = parsed.Solids[0];
        bool massFromFile = solid.Mass > 0f;
        float mass = massFromFile ? solid.Mass : FallbackMass;

        string surfaceProp = string.IsNullOrEmpty(solid.SurfaceProperty) ? "default" : solid.SurfaceProperty;
        int materialIndex = VCall<GetSurfaceIndexDelegate>(surfaceProps, SurfacePropsGetSurfaceIndexSlot)(surfaceProps, surfaceProp);

        if (materialIndex < 0)
        {
            surfaceProp = "default";
            materialIndex = VCall<GetSurfaceIndexDelegate>(surfaceProps, SurfacePropsGetSurfaceIndexSlot)(surfaceProps, surfaceProp);
        }

        nint vcollide = Marshal.AllocHGlobal(Marshal.SizeOf<VCollide>());

        // VCollideLoad zeroes its own output, but a probe that crashes before the call should not free garbage.
        Marshal.StructureToPtr(default(VCollide), vcollide, false);

        nint buffer = Marshal.AllocHGlobal(bytes.Length - PhyHeaderSize);
        Marshal.Copy(bytes, PhyHeaderSize, buffer, bytes.Length - PhyHeaderSize);

        try
        {
            VCall<VCollideLoadDelegate>(collision, CollisionVCollideLoadSlot)(
                collision, vcollide, parsed.DeclaredSolidCount, buffer, bytes.Length - PhyHeaderSize, false);
        }
        finally
        {
            // FUN_18000bcf0 memcpy's each solid's own bytes into a fresh allocation before returning, so the buffer
            // handed to VCollideLoad does not need to outlive the call (docs/findings, this file's own remarks).
            Marshal.FreeHGlobal(buffer);
        }

        VCollide loaded = Marshal.PtrToStructure<VCollide>(vcollide);

        if (loaded.SolidCount == 0 || loaded.Solids == 0)
        {
            output.WriteLine($"{physicsPath}: VCollideLoad produced no solids");
            VCall<VCollideUnloadDelegate>(collision, CollisionVCollideUnloadSlot)(collision, vcollide);
            Marshal.FreeHGlobal(vcollide);
            return null;
        }

        nint solidZero = Marshal.ReadIntPtr(loaded.Solids, 0);

        if (solidZero == 0)
        {
            output.WriteLine($"{physicsPath}: solid 0 did not load (see 'Corrupt physics model' above, or a MOPP/unknown tag)");
            VCall<VCollideUnloadDelegate>(collision, CollisionVCollideUnloadSlot)(collision, vcollide);
            Marshal.FreeHGlobal(vcollide);
            return null;
        }

        return new LoadedPhy(solidZero, vcollide, materialIndex, mass, massFromFile, surfaceProp, solid);
    }

    /// <summary>Frees what <see cref="LoadCollide"/> allocated — <c>VCollideUnload</c>, verified to exist at slot 37, then the block itself.</summary>
    private static void FreeCollide(nint collision, LoadedPhy loaded)
    {
        VCall<VCollideUnloadDelegate>(collision, CollisionVCollideUnloadSlot)(collision, loaded.VCollide);
        Marshal.FreeHGlobal(loaded.VCollide);
    }

    private static float CoreFloat(nint core, int offset) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(core + offset));

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

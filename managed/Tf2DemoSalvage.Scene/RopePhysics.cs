using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>One simulated rope node — <c>CSimplePhysics::CNode</c> (`simple_physics.h:29-42`).</summary>
internal struct RopeNode
{
    /// <summary><c>m_vPos</c>, at time t.</summary>
    public Vector3 Position;

    /// <summary><c>m_vPrevPos</c>, at time t − the step.</summary>
    public Vector3 Previous;

    /// <summary><c>m_vPredicted</c>, between the two at the clock — what is drawn.</summary>
    public Vector3 Predicted;
}

/// <summary>
/// <c>CRopePhysics&lt;ROPE_MAX_SEGMENTS&gt;</c>: <c>CSimplePhysics</c>'s fixed-step Verlet integrator and
/// <c>CBaseRopePhysics</c>'s spring solver (`simple_physics.cpp`, `rope_physics.cpp`), both published.
/// </summary>
/// <remarks>
/// **The step is fixed at a fiftieth of a second and the clock is a double** (`rope_physics.cpp:57`,
/// `simple_physics.h:73`): <c>Simulate</c> runs as many whole steps as the accumulated time has crossed, then sets each
/// node's predicted position between its last two steps. The arithmetic below keeps the engine's types — the step and
/// its <c>dt² / 2</c> multiplier in float, the accumulated time in double — so the step count matches at the boundary.
/// </remarks>
internal sealed class RopePhysics
{
    /// <summary><c>ROPE_MAX_SEGMENTS</c> (`rope_shared.h:16`).</summary>
    public const int MaximumNodes = 10;

    /// <summary>The verlet damping <c>CBaseRopePhysics::Simulate</c> passes, <c>flEnergy = 0.98</c> (`rope_physics.cpp:97`).</summary>
    private const float Damping = 0.98f;

    /// <summary>The spring passes per step, <c>nIterations = 3</c> (`rope_physics.cpp:117`).</summary>
    private const int SpringIterations = 3;

    private readonly float[] _nodeSpringDistancesSquared = new float[MaximumNodes - 1];

    private double _predictedTime;
    private int _currentStep;
    private float _timeStep;
    private float _timeStepMultiplier;
    private float _springDistance = 1f;
    private float _springDistanceSquared = 1f;

    /// <summary>The constructor's <c>Restart()</c>, then every node zeroed and the full count set.</summary>
    public RopePhysics()
    {
        Restart();
        SetNumNodes(MaximumNodes);
    }

    /// <summary><c>m_Nodes</c>.</summary>
    public RopeNode[] Nodes { get; } = new RopeNode[MaximumNodes];

    /// <summary><c>NumNodes()</c>.</summary>
    public int NodeCount { get; private set; }

    /// <summary><c>GetSpringLength()</c>.</summary>
    public float SpringLength => _springDistance;

    /// <summary><c>SetNumNodes</c>: the count, and each spring's share of the squared length.</summary>
    /// <param name="count">How many nodes, at most <see cref="MaximumNodes"/>.</param>
    public void SetNumNodes(int count)
    {
        NodeCount = count;

        for (int spring = 0; spring < count - 1; spring++)
        {
            _nodeSpringDistancesSquared[spring] = _springDistanceSquared / (count - 1);
        }
    }

    /// <summary><c>Restart</c>: <c>m_Physics.Init( 1.0 / 50 )</c>.</summary>
    public void Restart()
    {
        _predictedTime = 0;
        _currentStep = 0;
        _timeStep = (float)(1.0 / 50);
        _timeStepMultiplier = _timeStep * _timeStep * 0.5f;
    }

    /// <summary><c>ResetSpringLength</c>: never negative, and shared out per spring squared.</summary>
    /// <param name="distance">The length each spring rests at.</param>
    public void ResetSpringLength(float distance)
    {
        _springDistance = Math.Max(distance, 0f);
        _springDistanceSquared = _springDistance * _springDistance;

        for (int spring = 0; spring < NodeCount - 1; spring++)
        {
            _nodeSpringDistancesSquared[spring] = _springDistanceSquared / (NodeCount - 1);
        }
    }

    /// <summary><c>CSimplePhysics::Simulate</c> with the rope's damping, the springs and the delegate's constraints.</summary>
    /// <param name="seconds">The time to advance.</param>
    /// <param name="forces"><c>GetNodeForces</c>: the acceleration on one node.</param>
    /// <param name="constraints"><c>ApplyConstraints</c> after the springs, each spring pass.</param>
    public void Simulate(float seconds, Func<int, Vector3> forces, Action<RopeNode[], int> constraints)
    {
        ArgumentNullException.ThrowIfNull(forces);
        ArgumentNullException.ThrowIfNull(constraints);

        // "Figure out how many time steps to run."
        _predictedTime += seconds;
        int newStep = (int)Math.Ceiling(_predictedTime / _timeStep);
        int steps = newStep - _currentStep;

        for (int step = 0; step < steps; step++)
        {
            for (int index = 0; index < NodeCount; index++)
            {
                Vector3 acceleration = forces(index);
                ref RopeNode node = ref Nodes[index];

                Vector3 previous = node.Position;
                node.Position = node.Position + ((node.Position - node.Previous) * Damping) + (acceleration * _timeStepMultiplier);
                node.Previous = previous;
            }

            ApplyConstraints(constraints);
        }

        _currentStep = newStep;

        // "Setup predicted positions." `GetCurTime()` is the float step times the int count, widened.
        double currentTime = _timeStep * _currentStep;
        float interpolant = (float)((_predictedTime - (currentTime - _timeStep)) / _timeStep);

        for (int index = 0; index < NodeCount; index++)
        {
            ref RopeNode node = ref Nodes[index];
            node.Predicted = Vector3.Lerp(node.Previous, node.Position, interpolant);
        }
    }

    /// <summary><c>CBaseRopePhysics::ApplyConstraints</c> (`rope_physics.cpp:111-150`).</summary>
    private void ApplyConstraints(Action<RopeNode[], int> constraints)
    {
        // "Iterate multiple times here. If we don't, then gravity tends to win over the constraint solver."
        for (int iteration = 0; iteration < SpringIterations; iteration++)
        {
            for (int spring = 0; spring < NodeCount - 1; spring++)
            {
                ref RopeNode first = ref Nodes[spring];
                ref RopeNode second = ref Nodes[spring + 1];

                Vector3 to = first.Position - second.Position;
                float distanceSquared = to.LengthSquared();

                // "If we don't have an overall spring distance, see if we have a per-node one" — compared per node,
                // and then corrected toward the overall distance regardless, as the engine does.
                float springSquared = _springDistanceSquared;

                // `if ( !flSpringDist )`: a square, so never below zero.
                if (springSquared <= 0f)
                {
                    springSquared = _nodeSpringDistancesSquared[spring];
                }

                if (distanceSquared > springSquared)
                {
                    float distance = MathF.Sqrt(distanceSquared);
                    to *= 1f - (_springDistance / distance);

                    first.Position -= to * 0.5f;
                    second.Position += to * 0.5f;
                }
            }

            constraints(Nodes, NodeCount);
        }
    }
}

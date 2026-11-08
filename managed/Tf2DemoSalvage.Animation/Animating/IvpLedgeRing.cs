using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A ring walk around a compact ledge's vertex (<see cref="IvpLedgeTopology.Ring"/>, <see cref="IvpLedgeTopology.EndingAt"/>),
/// enumerated by value: a <c>foreach</c> over it allocates nothing.
/// </summary>
/// <remarks>
/// **Step back, hop, and stop on the start** (`1800a1e29`–`1800a1fad`), exactly as the iterator it replaces walked: the hop
/// is handed out before it is compared with the start, so the start comes last, and a walk that has taken as many hops as
/// the ledge has edges without returning is refused. <see cref="IvpLedgeTopology.EndingAt"/> hands out each hop's previous
/// edge instead — the edge ending at the point.
/// </remarks>
public readonly struct IvpLedgeRing : IEnumerable<IvpLedgeEdge>, IEquatable<IvpLedgeRing>
{
    private readonly IvpLedgeTopology _topology;
    private readonly IvpLedgeEdge _start;
    private readonly bool _ending;

    internal IvpLedgeRing(IvpLedgeTopology topology, IvpLedgeEdge start, bool ending)
    {
        _topology = topology;
        _start = start;
        _ending = ending;
    }

    /// <summary>The walk's enumerator, by value.</summary>
    /// <returns>An enumerator at the walk's start.</returns>
    public Walker GetEnumerator() => new(_topology, _start, _ending);

    IEnumerator<IvpLedgeEdge> IEnumerable<IvpLedgeEdge>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public bool Equals(IvpLedgeRing other) =>
        ReferenceEquals(_topology, other._topology) && _start == other._start && _ending == other._ending;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IvpLedgeRing other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_topology, _start, _ending);

    /// <summary>Whether two walks are the same walk.</summary>
    /// <param name="left">One walk.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they walk the same ledge from the same edge the same way.</returns>
    public static bool operator ==(IvpLedgeRing left, IvpLedgeRing right) => left.Equals(right);

    /// <summary>Whether two walks differ.</summary>
    /// <param name="left">One walk.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they do not walk the same ledge from the same edge the same way.</returns>
    public static bool operator !=(IvpLedgeRing left, IvpLedgeRing right) => !left.Equals(right);

    /// <summary>The ring walk's position.</summary>
    public struct Walker : IEnumerator<IvpLedgeEdge>
    {
        private readonly IvpLedgeTopology _topology;
        private readonly IvpLedgeEdge _start;
        private readonly bool _ending;
        private IvpLedgeEdge _from;
        private int _hops;
        private int _state;

        internal Walker(IvpLedgeTopology topology, IvpLedgeEdge start, bool ending)
        {
            _topology = topology;
            _start = start;
            _ending = ending;
            _from = default;
            _hops = 0;
            _state = 0;
            Current = default;
        }

        /// <inheritdoc/>
        public IvpLedgeEdge Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc/>
        /// <exception cref="InvalidDataException">A hop is refused, or the ring does not return to its start.</exception>
        public bool MoveNext()
        {
            if (_state == 2)
            {
                return false;
            }

            // The step back from the last hop is taken when the walk resumes, after that hop was handed out, as the
            // iterator's `current = Previous(hop)` ran past its `yield`.
            _from = _state == 0 ? _topology.Previous(_start) : _topology.Previous(_from);
            _state = 1;

            if (_hops >= _topology.RingLimit)
            {
                throw new InvalidDataException("A ledge's edge ring did not return to the edge it started from.");
            }

            IvpLedgeEdge hop = _topology.Hop(_from);

            _hops++;
            _from = hop;

            if (hop == _start)
            {
                _state = 2;
            }

            Current = _ending ? _topology.Previous(hop) : hop;
            return true;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _from = default;
            _hops = 0;
            _state = 0;
            Current = default;
        }

        /// <inheritdoc/>
        public readonly void Dispose()
        {
            // Nothing is held: the walk reads the topology it was given and owns nothing.
        }
    }
}

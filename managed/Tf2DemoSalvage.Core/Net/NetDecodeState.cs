using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// State carried across packets while decoding a demo.
/// </summary>
/// <remarks>
/// Some messages describe how to read later ones — <c>svc_GameEventList</c> is the first
/// example, and string tables and the class list will follow. A packet therefore cannot be
/// decoded in isolation, which is why this is threaded through rather than each packet being
/// read independently.
/// </remarks>
public sealed class NetDecodeState
{
    private readonly Dictionary<int, GameEventDefinition> _eventDefinitions = [];

    /// <summary>
    /// The server's own description of itself, once seen. Its <c>MaxClasses</c> determines the
    /// bit width of entity class ids, so entity decoding cannot begin without it.
    /// </summary>
    public ServerInfoMessage? ServerInfo { get; set; }

    /// <summary>Game event definitions seen so far, keyed by event id.</summary>
    public IReadOnlyDictionary<int, GameEventDefinition> EventDefinitions => _eventDefinitions;

    /// <summary>
    /// The networked class list, once seen. Entity updates carry a class id sized from it, so
    /// entity decoding cannot start without it.
    /// </summary>
    public ClassInfoMessage? ClassInfo { get; set; }

    /// <summary>
    /// Capacities of the string tables declared so far, in creation order. An update names its
    /// table by that order, and needs the capacity to size its entry indices.
    /// </summary>
    private readonly List<int> _stringTableCapacities = [];

    /// <summary>Records a table's capacity as it is created.</summary>
    /// <param name="maxEntries">The table's capacity.</param>
    public void AddStringTable(int maxEntries) => _stringTableCapacities.Add(maxEntries);

    /// <summary>Capacity of the table with the given id, or 0 if it has not been seen.</summary>
    /// <param name="tableId">Table id, by creation order.</param>
    /// <returns>The capacity, or 0.</returns>
    public int StringTableCapacity(int tableId) =>
        tableId >= 0 && tableId < _stringTableCapacities.Count
            ? _stringTableCapacities[tableId]
            : 0;

    /// <summary>Records the definitions from a <c>svc_GameEventList</c>.</summary>
    /// <param name="definitions">Definitions to remember.</param>
    public void AddEventDefinitions(IEnumerable<GameEventDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (GameEventDefinition definition in definitions)
        {
            _eventDefinitions[definition.Id] = definition;
        }
    }
}

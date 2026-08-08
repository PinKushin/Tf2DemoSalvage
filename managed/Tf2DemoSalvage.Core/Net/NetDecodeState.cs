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

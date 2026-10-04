using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What the installed maps' soundscape entities declare that decides how the server places them.</summary>
/// <remarks>
/// **Four keys the engine reads and this project once ignored**, counted so a fix can say what it changes on a real
/// map rather than on a fixture: `StartDisabled` (`soundscape.cpp:91`), a `position&lt;N&gt;` set after a lower one
/// is left unset (the slot keeps its own index — `soundscape.cpp:217-229`), and an `env_soundscape_proxy` whose
/// master carries positions (`CEnvSoundscapeProxy::Activate` copies them, `soundscape.cpp:52-54`). The control is
/// the plain count of `env_soundscape` entities, which every TF2 map has.
/// </remarks>
public sealed class SoundscapeEntityProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "soundscape-entities";

    /// <inheritdoc/>
    public string Summary =>
        "soundscape entities on the installed maps: disabled, gapped positions, proxy masters, unnamed, targets with no origin: soundscape-entities [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        string[] maps = arguments.Count > 0
            ? [anyMap]
            : [.. Directory.EnumerateFiles(Path.GetDirectoryName(anyMap) ?? ".", "*.bsp").Order(StringComparer.Ordinal)];

        int soundscapes = 0;
        int proxies = 0;
        int triggerables = 0;
        int disabled = 0;
        int positioned = 0;
        int gapped = 0;
        int proxiesOfPositioned = 0;
        int unnamed = 0;
        int targetsWithoutOrigin = 0;
        int withoutRadius = 0;
        Dictionary<string, int> masters = [];

        foreach (string map in maps)
        {
            IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(File.ReadAllBytes(map));
            string name = Path.GetFileNameWithoutExtension(map);

            Dictionary<string, BspEntity> firstByName = new(StringComparer.OrdinalIgnoreCase);

            foreach (BspEntity entity in entities)
            {
                if (entity.TryGetValue("targetname", out string target))
                {
                    firstByName.TryAdd(target, entity);
                }
            }

            foreach (BspEntity entity in entities)
            {
                string kind = entity.ClassName.ToUpperInvariant();

                if (kind is not ("ENV_SOUNDSCAPE" or "ENV_SOUNDSCAPE_PROXY" or "ENV_SOUNDSCAPE_TRIGGERABLE"))
                {
                    continue;
                }

                soundscapes += kind == "ENV_SOUNDSCAPE" ? 1 : 0;
                proxies += kind == "ENV_SOUNDSCAPE_PROXY" ? 1 : 0;
                triggerables += kind == "ENV_SOUNDSCAPE_TRIGGERABLE" ? 1 : 0;

                if (entity.TryGetValue("StartDisabled", out string off) && off.Trim() is { Length: > 0 } and not "0")
                {
                    disabled++;
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name}: {entity.ClassName} StartDisabled \"{off}\""));
                }

                int[] slots = Slots(entity);

                if (slots.Length > 0)
                {
                    positioned++;

                    if (slots[^1] != slots.Length - 1)
                    {
                        gapped++;
                        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name}: {entity.ClassName} positions {string.Join(",", slots)}"));
                    }
                }

                if (kind == "ENV_SOUNDSCAPE_PROXY" &&
                    entity.TryGetValue("MainSoundscapeName", out string master) &&
                    firstByName.TryGetValue(master, out BspEntity? main) &&
                    Slots(main).Length > 0)
                {
                    proxiesOfPositioned++;
                }

                // **What `CEnvSoundscapeProxy::Activate` finds** (`soundscape.cpp:40-58`): the FIRST entity of the master's
                // name, cast to `CEnvSoundscape` — any of the three classes — or nothing.
                if (kind == "ENV_SOUNDSCAPE_PROXY")
                {
                    string masterKind = entity.TryGetValue("MainSoundscapeName", out string named) && named.Length > 0 &&
                                        firstByName.TryGetValue(named, out BspEntity? found)
                        ? found.ClassName.ToUpperInvariant()
                        : "(none)";

                    masters[masterKind] = masters.GetValueOrDefault(masterKind) + 1;

                    if (masterKind is not "ENV_SOUNDSCAPE")
                    {
                        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name}: proxy of {masterKind}"));
                    }
                }

                // `m_flRadius` is a `FIELD_FLOAT` keyfield, `atof`'d (`saverestore_gamedll.cpp:56-58`), and the constructor
                // never sets it — so an entity without the key has whatever the allocation held.
                if (!entity.TryGetValue("radius", out _))
                {
                    withoutRadius++;
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name}: {entity.ClassName} has no radius key"));
                }

                if (kind != "ENV_SOUNDSCAPE_PROXY" && !(entity.TryGetValue("soundscape", out string own) && own.Length > 0))
                {
                    unnamed++;
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name}: {entity.ClassName} names no soundscape"));
                }

                // `FindEntityByName( NULL, m_positionNames[i], this, this )` then `GetAbsOrigin()` (`:222-226`): the first
                // entity of the name, wherever it is — one with no `origin` key stands at the world origin.
                foreach (int slot in slots)
                {
                    if (entity.TryGetValue($"position{slot.ToString(CultureInfo.InvariantCulture)}", out string target) &&
                        firstByName.TryGetValue(target, out BspEntity? at) &&
                        !at.TryGetValue("origin", out _))
                    {
                        targetsWithoutOrigin++;
                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture, $"  {name}: position{slot} names {at.ClassName} '{target}' with no origin"));
                    }
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{maps.Length} maps: env_soundscape {soundscapes}, proxy {proxies}, triggerable {triggerables}; " +
            $"StartDisabled {disabled}; with positions {positioned}, gapped {gapped}; proxies of a positioned master {proxiesOfPositioned}"));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"proxy masters: {string.Join(", ", masters.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}"))}; " +
            $"non-proxies naming no soundscape {unnamed}; position targets with no origin {targetsWithoutOrigin}; " +
            $"soundscape entities with no radius key {withoutRadius}"));
    }

    /// <summary>Which of `position0`..`position7` an entity sets, in slot order.</summary>
    private static int[] Slots(BspEntity entity) =>
        [.. Enumerable.Range(0, 8).Where(slot =>
            entity.TryGetValue($"position{slot.ToString(CultureInfo.InvariantCulture)}", out string named) && named.Length > 0)];
}

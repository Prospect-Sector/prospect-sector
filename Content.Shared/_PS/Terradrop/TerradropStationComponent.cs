using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map;

namespace Content.Shared._PS.Terradrop;

/// <summary>
/// Added per station to store data on their available salvage missions.
/// </summary>
[RegisterComponent]
public sealed partial class TerradropStationComponent : Component
{
    [ViewVariables]
    public readonly Dictionary<ushort, SalvageMissionParams> Missions = new();

    [ViewVariables]
    public readonly Dictionary<MapId, EntityUid> ActiveMissions = new();

    public ushort NextIndex = 1;
}

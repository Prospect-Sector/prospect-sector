using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Shared._PS.Terradrop;

/// <summary>
/// Added per station to store data on their available salvage missions.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedTerradropSystem)), AutoGenerateComponentState]
public sealed partial class TerradropStationComponent : Component
{
    [NonSerialized]
    public readonly Dictionary<string, List<TerradropActiveMissionData>> ActiveMissions = new();

    [ViewVariables]
    public readonly Dictionary<string, SalvageMissionParams> Missions = new();

    /// <summary>
    /// The ids of all the maps which have been unlocked.
    /// </summary>
    [AutoNetworkedField]
    [DataField("unlockedMapNodes", customTypeSerializer: typeof(PrototypeIdListSerializer<TerradropMapPrototype>))]
    public List<string> UnlockedMapNodes = new();

    [NonSerialized]
    public EntityUid? ReturnMarker = null;

}

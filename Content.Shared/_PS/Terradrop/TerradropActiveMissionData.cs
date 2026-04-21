using Robust.Shared.Map;

namespace Content.Shared._PS.Terradrop;

/// <summary>
/// Contains data about an active terradrop mission.
/// </summary>
public struct TerradropActiveMissionData
{
    public EntityUid MapUid;
    public MapId MapId;
    public EntityUid MapPortalUid;
    public string InstanceName;

    public TerradropActiveMissionData(EntityUid mapUid, MapId mapId, EntityUid mapPortalUid, string instanceName)
    {
        MapUid = mapUid;
        MapId = mapId;
        MapPortalUid = mapPortalUid;
        InstanceName = instanceName;
    }
}

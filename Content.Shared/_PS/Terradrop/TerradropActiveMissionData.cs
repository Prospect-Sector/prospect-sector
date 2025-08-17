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

    public TerradropActiveMissionData(EntityUid mapUid, MapId mapId, EntityUid mapPortalUid)
    {
        MapUid = mapUid;
        MapId = mapId;
        MapPortalUid = mapPortalUid;
    }
}

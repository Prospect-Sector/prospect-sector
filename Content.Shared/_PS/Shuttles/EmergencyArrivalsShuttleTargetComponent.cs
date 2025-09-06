using Robust.Shared.Utility;

namespace Content.Shared._PS.Shuttles;

[RegisterComponent, Access(typeof(SharedProspectEmergencyArrivalsSystem))]
public sealed partial class ProspectEmergencyArrivalsShuttleTargetComponent: Component
{
    [DataField("shuttle")]
    public EntityUid Shuttle;

    [DataField("shuttlePath")] public ResPath ShuttlePath = new("/Maps/_PS/Shuttles/emercryo_8925.yml");
}

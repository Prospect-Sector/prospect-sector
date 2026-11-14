namespace Content.Server._PS.Shuttles;

/// <summary>
/// Added to a station that is available for arrivals shuttles.
/// </summary>
[RegisterComponent, Access(typeof(ProspectEmergencyArrivalsSystem))]
public sealed partial class ProspectEmergencyShuttleComponent : Component
{
    [DataField("station")]
    public EntityUid Station;

    /// <summary>
    /// The temporary map the shuttle was loaded on. Deleted when the first FTL completes.
    /// </summary>
    [DataField]
    public EntityUid? StartMap;
}

using Robust.Shared.GameStates;

namespace Content.Shared._PS.Terradrop;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TerradropMapComponent : Component
{
    public int ThreatLevel = 1;

    /// <summary>
    /// The level of this terradrop map. Higher levels increase stat rolls on spawned items.
    /// Level 10 = 10% better stats, Level 50 = 50% better stats, etc.
    /// </summary>
    [AutoNetworkedField]
    public int Level = 0;

    [NonSerialized]
    public EntityUid? StationUid = null;

    [NonSerialized]
    public TerradropMapPrototype? MapPrototype = null;

    [NonSerialized]
    public EntityUid? ReturnMarker = null;
}

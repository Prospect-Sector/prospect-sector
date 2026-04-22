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

    /// <summary>
    /// The human-readable instance name, e.g. "Zerona Prime #1".
    /// </summary>
    [AutoNetworkedField]
    public string InstanceName = string.Empty;

    [NonSerialized]
    public EntityUid? StationUid = null;

    [NonSerialized]
    public TerradropMapPrototype? MapPrototype = null;

    [NonSerialized]
    public EntityUid? ReturnMarker = null;

    /// <summary>
    /// Number of kills/destructions required to complete the mission objective.
    /// Set at generation time: 8 + (2 * Level).
    /// </summary>
    public int ObjectiveRequired = 0;

    /// <summary>
    /// How many objective targets have been killed or destroyed so far.
    /// </summary>
    public int ObjectiveProgress = 0;

    public bool ObjectiveCompleted = false;

    /// <summary>
    /// Prevents announcing the objective more than once (set true on first player entry).
    /// </summary>
    public bool ObjectiveAnnounced = false;
}

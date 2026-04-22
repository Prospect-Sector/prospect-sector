namespace Content.Server._PS.Terradrop;

/// <summary>
/// Marker added by <see cref="TerradropThreatSystem"/> to mobs it spawns from
/// outdoor incursion beacons. Scopes the hunt-targeting / diagnostic loop to
/// mobs we actually manage — dungeon-interior mobs still carry
/// <c>TerradropMobComponent</c> for stat scaling but run vanilla <c>XenoCompound</c>
/// without our blackboard overrides. Touching their blackboards was racing with
/// their own <c>MoveToOperator.ConditionalShutdown</c> and crashing.
/// </summary>
[RegisterComponent]
public sealed partial class TerradropHunterComponent : Component
{
    /// <summary>
    /// Game time at which this mob was spawned by a threat beacon. Used by
    /// <see cref="TerradropThreatSystem"/> to delete the oldest mobs when a
    /// new burst pushes the per-map hunter count over the level cap.
    /// </summary>
    public TimeSpan SpawnedAt;
}

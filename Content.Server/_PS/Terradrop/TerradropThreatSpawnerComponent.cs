using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._PS.Terradrop;

/// <summary>
/// Placed on outside-the-dungeon incursion markers by <see cref="TerradropThreatSystem"/>.
/// Each tick the system checks <see cref="NextFire"/> and emits one burst of mobs from
/// <see cref="Prototypes"/>, then applies terradrop hunt-mode (NavSmash / NavPry / NavClimb,
/// widened vision) to the spawned entity so it immediately A*-routes to the players and
/// smashes any obstacle in the way.
///
/// This exists instead of <c>TimedSpawnerComponent</c> so the spawn path owns the
/// post-spawn hook directly — subscribing to <c>HTNComponent, ComponentStartup</c> clashes
/// with <c>HTNSystem</c>'s own subscription.
/// </summary>
[RegisterComponent]
public sealed partial class TerradropThreatSpawnerComponent : Component
{
    [DataField]
    public List<EntProtoId> Prototypes = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextFire = TimeSpan.Zero;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(30);

    [DataField]
    public int MinBurst = 1;

    [DataField]
    public int MaxBurst = 2;
}

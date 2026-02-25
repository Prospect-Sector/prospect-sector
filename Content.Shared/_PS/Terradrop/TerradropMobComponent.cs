using Robust.Shared.GameStates;

namespace Content.Shared._PS.Terradrop;

/// <summary>
/// Marker component added to mobs spawned in terradrop dungeons.
/// Triggers level-based health and damage scaling via <see cref="Content.Server._PS.Terradrop.TerradropMobScalingSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TerradropMobComponent : Component
{
    /// <summary>
    /// Whether scaling has already been applied. Prevents re-scaling on save/load.
    /// </summary>
    [AutoNetworkedField]
    public bool Initialized;

    /// <summary>
    /// The dungeon level this mob was spawned at.
    /// </summary>
    [AutoNetworkedField]
    public int SpawnLevel;

    /// <summary>
    /// Health multiplier that was applied.
    /// </summary>
    [AutoNetworkedField]
    public float HealthMultiplier = 1f;

    /// <summary>
    /// Damage multiplier that was applied.
    /// </summary>
    [AutoNetworkedField]
    public float DamageMultiplier = 1f;
}

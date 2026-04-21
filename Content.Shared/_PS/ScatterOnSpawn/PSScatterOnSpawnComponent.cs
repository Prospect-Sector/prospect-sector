using Robust.Shared.GameObjects;

namespace Content.Shared._PS.ScatterOnSpawn;

/// <summary>
/// When added to an entity, causes it to be thrown in a random direction.
/// Used by loot crates to scatter items on destruction.
/// </summary>
[RegisterComponent]
public sealed partial class PSScatterOnSpawnComponent : Component
{
    /// <summary>
    /// Throw speed applied in a random direction.
    /// </summary>
    [DataField]
    public float Force = 5f;
}

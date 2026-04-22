using Robust.Shared.Prototypes;

namespace Content.Server._PS.Terradrop;

/// <summary>
/// Drives a gradually-escalating outside-the-dungeon threat on a terradrop map.
/// Placed on the map entity by <see cref="GenerateTerradropJob"/>; consumed by
/// <see cref="TerradropThreatSystem"/>, which periodically drops hostile mob
/// spawners in the biome around the BSP dungeon and ramps spawn frequency up
/// over the lifetime of the mission.
/// </summary>
[RegisterComponent]
public sealed partial class TerradropThreatComponent : Component
{
    /// <summary>Game time at which threat escalation began.</summary>
    public TimeSpan StartTime;

    /// <summary>Ramp length — after this, spawners fire at peak cadence.</summary>
    public TimeSpan Duration = TimeSpan.FromMinutes(15);

    /// <summary>Next game-time tick at which the system will try to place a new spawner.</summary>
    public TimeSpan NextPlacement;

    /// <summary>Faction prototype id from the mission, used to pick the mob proto pool.</summary>
    public string FactionId = string.Empty;

    /// <summary>Dungeon centre in grid-tile space.</summary>
    public Vector2i DungeonCenter;

    /// <summary>Axis-aligned dungeon footprint in grid tiles (for outside-spawn rejection).</summary>
    public Vector2i DungeonBounds;

    /// <summary>Landing pad radius in tiles (rejected from spawn area).</summary>
    public int LandingPadRadius = 6;

    /// <summary>
    /// Tile position of the return portal room (the guaranteed landing prefab
    /// inside the BSP dungeon — <c>Terradrop7x7a</c>). Spawners never appear
    /// within <see cref="PortalRoomSafeRadius"/> of this point so the player's
    /// arrival room stays clear. Set by <c>TerradropSystem.Generation</c> after
    /// the pad is located, defaulted to origin until then.
    /// </summary>
    public Vector2i PortalRoomPosition;

    /// <summary>
    /// Exclusion radius around <see cref="PortalRoomPosition"/>. The landing
    /// prefab is 7×7 tiles so 8 covers it plus a small breathing margin.
    /// </summary>
    public int PortalRoomSafeRadius = 8;

    /// <summary>Terradrop level at generation — scales spawner aggressiveness.</summary>
    public int Level = 0;

    /// <summary>How many spawners have been placed so far.</summary>
    public int SpawnerCount;

    /// <summary>Hard cap on outdoor spawners on a single map.</summary>
    public int MaxSpawners = 32;

    /// <summary>Seconds between placement attempts at t=0 (start).</summary>
    public float StartPlacementIntervalSeconds = 30f;

    /// <summary>Seconds between placement attempts at t=Duration (peak).</summary>
    public float PeakPlacementIntervalSeconds = 12f;

    /// <summary>TimedSpawner fire interval at t=0.</summary>
    public float StartSpawnerIntervalSeconds = 20f;

    /// <summary>TimedSpawner fire interval at t=Duration.</summary>
    public float PeakSpawnerIntervalSeconds = 10f;
}

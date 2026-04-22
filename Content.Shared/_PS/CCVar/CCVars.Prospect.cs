using Robust.Shared.Configuration;

// Intentional namespace placement.
namespace Content.Shared.CCVar;

/// <summary>
///     General CCVars for the Prospect Sector game mode.
/// </summary>
public sealed partial class CCVars
{
    /// <summary>
    /// Whether the EMERGENCY arrivals shuttle is enabled.
    /// Emergency because the shuttle has survived a faulty FTL!!
    /// This is a Prospect type arrivals that spawns everyone on the shuttle at any given time of the round.
    /// </summary>
    public static readonly CVarDef<bool> EmergencyArrivalsShuttle =
        CVarDef.Create("prospect.arrivals", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether to use the Prospect parallel dungeon generation system.
    /// When enabled, dungeon generation uses multi-threaded parallel processing for improved performance.
    /// </summary>
    public static readonly CVarDef<bool> ProspectParallelDungeons =
        CVarDef.Create("prospect.parallel_dungeons", true, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of parallel workers for dungeon generation.
    /// Set to 0 to use all available processors.
    /// </summary>
    public static readonly CVarDef<int> ProspectDungeonWorkers =
        CVarDef.Create("prospect.dungeon_workers", 0, CVar.SERVERONLY);

    /// <summary>
    /// Number of test dungeons to generate on round start for benchmarking.
    /// Set to 0 to disable.
    /// </summary>
    public static readonly CVarDef<int> ProspectDungeonBenchmark =
        CVarDef.Create("prospect.dungeon_benchmark", 0, CVar.SERVERONLY);

    /// <summary>
    /// Coefficient for item stat scaling power curve.
    /// Formula: bonusMultiplier = 1 + coefficient * level^exponent
    /// At 0.1 (default) with exponent 1.0: level 10 = 2x, level 50 = 6x, level 100 = 11x.
    /// Stored as int, divided by 100 at runtime (10 = 0.1).
    /// </summary>
    public static readonly CVarDef<int> TerradropItemStatCoefficient =
        CVarDef.Create("terradrop.item_stat_coefficient", 10, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// Exponent for item stat scaling power curve.
    /// Formula: bonusMultiplier = 1 + coefficient * level^exponent
    /// At 1.0 (default): linear. At 1.5: accelerating. At 2.0: quadratic.
    /// Stored as int, divided by 100 at runtime (100 = 1.0).
    /// </summary>
    public static readonly CVarDef<int> TerradropItemStatExponent =
        CVarDef.Create("terradrop.item_stat_exponent", 100, CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    /// Coefficient for mob health scaling power curve.
    /// Formula: multiplier = 1 + coefficient * level^exponent
    /// At 0.2 (default) with exponent 1.5: level 5 = 3.2x, level 10 = 7.3x, level 50 = 72x.
    /// Stored as int, divided by 100 at runtime (20 = 0.2).
    /// </summary>
    public static readonly CVarDef<int> TerradropMobHealthCoefficient =
        CVarDef.Create("terradrop.mob_health_coefficient", 20, CVar.SERVERONLY);

    /// <summary>
    /// Coefficient for mob damage scaling power curve.
    /// Formula: multiplier = 1 + coefficient * level^exponent
    /// At 0.2 (default) with exponent 1.5: level 5 = 3.2x, level 10 = 7.3x, level 50 = 72x.
    /// Stored as int, divided by 100 at runtime (20 = 0.2).
    /// </summary>
    public static readonly CVarDef<int> TerradropMobDamageCoefficient =
        CVarDef.Create("terradrop.mob_damage_coefficient", 20, CVar.SERVERONLY);

    /// <summary>
    /// Exponent for mob scaling power curve (shared by health and damage).
    /// Formula: multiplier = 1 + coefficient * level^exponent
    /// Higher values make the curve steeper at high levels.
    /// At 1.0: linear. At 1.5 (default): accelerating. At 2.0: quadratic.
    /// Stored as int, divided by 100 at runtime (150 = 1.5).
    /// </summary>
    public static readonly CVarDef<int> TerradropMobScalingExponent =
        CVarDef.Create("terradrop.mob_scaling_exponent", 150, CVar.SERVERONLY);

    /// <summary>
    /// Maximum multiplier for mob health/damage scaling.
    /// Prevents extreme values at very high levels. Default 100 = cap at 100x.
    /// </summary>
    public static readonly CVarDef<int> TerradropMobScalingCap =
        CVarDef.Create("terradrop.mob_scaling_cap", 100, CVar.SERVERONLY);

    /// <summary>
    /// Global multiplier applied to outside-the-dungeon threat spawning on a
    /// terradrop map. Scales placement cadence, per-spawner fire rate, burst
    /// sizes, and the hard cap on active spawners. Stored as int, divided by
    /// 100 at runtime (100 = 1.0x baseline, 500 = 5.0x → spawners appear five
    /// times faster, each fires five times as often, burst sizes 5x, cap 5x).
    /// </summary>
    public static readonly CVarDef<int> TerradropThreatMultiplier =
        CVarDef.Create("terradrop.threat_multiplier", 150, CVar.SERVERONLY);
}

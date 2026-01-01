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
}

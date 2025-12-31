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
    /// Modifier for how much dungeon level affects item stat rolls.
    /// Formula: bonusMultiplier = 1 + (level * 0.01 * modifier / 100)
    /// At 100 (default): level 100 gives 2x stats (current behavior)
    /// At 900: level 100 gives 10x stats
    /// At 1: level 100 gives ~1.01x stats (minimal scaling)
    /// </summary>
    public static readonly CVarDef<int> TerradropLevelStatModifier =
        CVarDef.Create("prospect.terradrop_level_stat_modifier", 100, CVar.REPLICATED | CVar.SERVER);
}

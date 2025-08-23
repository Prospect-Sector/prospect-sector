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
}

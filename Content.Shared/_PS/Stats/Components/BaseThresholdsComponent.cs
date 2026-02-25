using Content.Shared.FixedPoint;

namespace Content.Shared._PS.Stats.Components;

/// <summary>
/// Stores the original mob thresholds before any equipment bonuses are applied.
/// Added automatically by PlayerThresholdBonusSystem when an entity first receives threshold bonuses.
/// </summary>
[RegisterComponent]
public sealed partial class BaseThresholdsComponent : Component
{
    /// <summary>
    /// Base Critical (softcrit) threshold from the entity's prototype.
    /// </summary>
    public FixedPoint2 BaseCritThreshold;

    /// <summary>
    /// Base Dead threshold from the entity's prototype.
    /// </summary>
    public FixedPoint2 BaseDeadThreshold;
}

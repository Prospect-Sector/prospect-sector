namespace Content.Shared._PS.Terradrop;

/// <summary>
/// Marks an entity (mob or loot container) as counting toward the Terradrop win condition.
/// Added at spawn time in GenerateTerradropJob.
/// </summary>
[RegisterComponent]
public sealed partial class TerradropObjectiveTargetComponent : Component { }

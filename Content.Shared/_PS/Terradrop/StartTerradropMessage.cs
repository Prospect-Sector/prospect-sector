using Robust.Shared.Serialization;

namespace Content.Shared._PS.Terradrop;

[Serializable] [NetSerializable]
public sealed class StartTerradropMessage : BoundUserInterfaceMessage
{
    public string TerradropMapId;

    /// <summary>
    /// The level selected by the player. Higher levels = better loot stats.
    /// Level 10 = 10% better stats, Level 50 = 50% better stats, etc.
    /// </summary>
    public int Level;

    public StartTerradropMessage(string terradropMapId, int level = 0)
    {
        TerradropMapId = terradropMapId;
        Level = level;
    }
}

using Robust.Shared.Serialization;

namespace Content.Shared._PS.Stats;

/// <summary>
/// Core stat types that affect character and item performance.
/// </summary>
[Serializable, NetSerializable]
public enum StatType : byte
{
    /// <summary>
    /// Strength/Might: Increases melee damage and block/parry chance.
    /// </summary>
    Strength,

    /// <summary>
    /// Dexterity/Nimbleness: Increases ranged/throwing damage and crit chance.
    /// </summary>
    Dexterity,

    /// <summary>
    /// Agility/Reflex: Increases movement speed and dodge chance.
    /// </summary>
    Agility,

    /// <summary>
    /// Fortitude/Endurance: Increases HP thresholds, stamina resistance, and natural armor.
    /// </summary>
    Fortitude,

    /// <summary>
    /// Intelligence/Logic: Increases topical healing and elemental damage.
    /// </summary>
    Intelligence,

    /// <summary>
    /// Luck/Fortune: Increases crit chance (0.5% per point), dodge chance (0.3% per point),
    /// and enhances level bonus scaling (2.5% per point).
    /// </summary>
    Luck
}

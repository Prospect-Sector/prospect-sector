using Content.Shared.Procedural;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Procedural.StationGeneration;

/// <summary>
/// Defines a department for station generation.
/// Departments group rooms thematically and are placed in Voronoi zones.
/// </summary>
[Prototype("stationDepartment")]
public sealed partial class StationDepartmentPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localized display name of the department.
    /// </summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    /// Tags used to filter rooms from room presets for this department.
    /// Rooms matching any of these tags will be considered for placement.
    /// </summary>
    [DataField]
    public List<ProtoId<TagPrototype>> RoomTags { get; private set; } = new();

    /// <summary>
    /// Specific room presets to use for this department.
    /// If set, these presets will be used instead of tag-based filtering.
    /// </summary>
    [DataField]
    public List<ProtoId<DungeonRoomPrototype>> RoomPresets { get; private set; } = new();

    /// <summary>
    /// Minimum number of rooms to place in this department.
    /// </summary>
    [DataField]
    public int MinRooms { get; private set; } = 3;

    /// <summary>
    /// Maximum number of rooms to place in this department.
    /// </summary>
    [DataField]
    public int MaxRooms { get; private set; } = 8;

    /// <summary>
    /// Priority for zone placement. Higher values place the department closer to station center.
    /// </summary>
    [DataField]
    public float Priority { get; private set; } = 1f;

    /// <summary>
    /// Color used for debug visualization.
    /// </summary>
    [DataField]
    public Color DebugColor { get; private set; } = Color.Gray;
}

using Content.Shared.Maps;
using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Procedural.StationGeneration;

/// <summary>
/// Generates a station using Voronoi zones for departments, graph-based hallways,
/// and 1-wide shaft tunnels between rooms.
/// </summary>
public sealed partial class VoronoiStationDunGen : IDunGenLayer
{
    /// <summary>
    /// Radius of the station in tiles from center.
    /// </summary>
    [DataField]
    public int StationRadius { get; set; } = 100;

    /// <summary>
    /// Departments to generate zones for.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<StationDepartmentPrototype>> Departments { get; set; } = new();

    /// <summary>
    /// Tile to use for hallway floors.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> FloorTile { get; set; } = "FloorSteel";

    /// <summary>
    /// Tile to use for shaft tunnel floors.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> MaintsTile { get; set; } = "FloorDark";

    /// <summary>
    /// Entity prototype for walls.
    /// </summary>
    [DataField]
    public EntProtoId WallPrototype { get; set; } = "WallSolid";

    /// <summary>
    /// Entity prototype for hallway doors.
    /// </summary>
    [DataField]
    public EntProtoId HallwayDoorPrototype { get; set; } = "AirlockGlass";

    /// <summary>
    /// Entity prototype for shaft tunnel doors.
    /// </summary>
    [DataField]
    public EntProtoId MaintsDoorPrototype { get; set; } = "AirlockMaint";

    /// <summary>
    /// Width of main hallways in tiles.
    /// </summary>
    [DataField]
    public int HallwayWidth { get; set; } = 3;

    /// <summary>
    /// Gap between room walls for shaft tunnels (always 1 for single-wide tunnels).
    /// </summary>
    [DataField]
    public int RoomGap { get; set; } = 1;

    /// <summary>
    /// Whether to add redundant hallway connections beyond the minimum spanning tree.
    /// </summary>
    [DataField]
    public bool AddRedundantHallways { get; set; } = true;

    /// <summary>
    /// Probability of adding each potential redundant hallway (0-1).
    /// </summary>
    [DataField]
    public float RedundantHallwayChance { get; set; } = 0.3f;

    /// <summary>
    /// Minimum distance between department centers (Poisson disk sampling).
    /// </summary>
    [DataField]
    public float MinDepartmentSpacing { get; set; } = 30f;
}

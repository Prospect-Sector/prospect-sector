using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Procedural;

/// <summary>
/// BSP (Binary Space Partitioning) dungeon generator. Recursively splits a rectangular footprint
/// into leaves, places a handmade prefab room in each leaf, and (in a later pass) connects siblings
/// via the compass-midpoint door slots that prefabs leave clear by design.
/// Prefabs are wall-free; <c>BoundaryWallDunGen</c> supplies walls around the resulting room/corridor union.
/// </summary>
public sealed partial class BspDungeonDunGen : IDunGenLayer
{
    /// <summary>
    /// Overall dungeon footprint in tiles. The BSP area is centered on the generation position.
    /// </summary>
    [DataField]
    public Vector2i Bounds = new(60, 60);

    /// <summary>
    /// Smallest leaf size allowed. Leaves at or below this size on both axes will not be split.
    /// Leaves may be rectangular — either axis can hit the minimum independently.
    /// </summary>
    [DataField]
    public Vector2i MinLeafSize = new(9, 9);

    /// <summary>
    /// Largest leaf size allowed. If either axis exceeds this, the leaf is force-split.
    /// </summary>
    [DataField]
    public Vector2i MaxLeafSize = new(22, 22);

    /// <summary>
    /// Minimum split ratio along the chosen axis (0-1). 0.35 means the split never falls closer
    /// than 35% from the near edge.
    /// </summary>
    [DataField]
    public float SplitRatioMin = 0.35f;

    /// <summary>
    /// Maximum split ratio along the chosen axis (0-1). Paired with <see cref="SplitRatioMin"/>.
    /// </summary>
    [DataField]
    public float SplitRatioMax = 0.65f;

    /// <summary>
    /// Corridor width in tiles between connected leaves.
    /// </summary>
    [DataField]
    public int CorridorWidth = 3;

    /// <summary>
    /// Tiles of clearance between a prefab and its leaf boundary. The effective gap between two
    /// adjacent prefabs is therefore <c>2 * PrefabMargin</c>. Must be large enough for a
    /// <see cref="CorridorWidth"/>-wide corridor plus walls to route between neighbours without
    /// the L-bend pivot block overlapping a prefab's exterior wall ring.
    /// </summary>
    [DataField]
    public int PrefabMargin = 3;

    /// <summary>
    /// Filters which <c>DungeonRoomPrototype</c>s are eligible for placement in leaves.
    /// Matches against the room's tag list.
    /// </summary>
    [DataField]
    public EntityWhitelist? RoomWhitelist;

    /// <summary>
    /// Tile used to fill leaves and corridors.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> FallbackTile = "FloorSteel";

    /// <summary>
    /// After the spanning tree of sibling corridors is built, this many additional T-junction
    /// corridors are added — each from an unused leaf compass-midpoint door to the nearest
    /// existing corridor tile, shortest-first (deterministic). Produces branching loops so the
    /// topology isn't a pure tree. Set to 0 to disable.
    /// </summary>
    [DataField]
    public int ExtraJunctions = 3;

    /// <summary>
    /// When true, a post-pass thickens the outer walls at random so the dungeon silhouette is not
    /// strictly rectilinear — appropriate for cave/mineshaft biomes. Prefab interiors are untouched.
    /// </summary>
    [DataField]
    public bool Irregularize = false;

    /// <summary>
    /// Probability per exterior tile of extending the wall outward by one additional tile.
    /// Only relevant when <see cref="Irregularize"/> is true.
    /// </summary>
    [DataField]
    public float IrregularizeChance = 0.35f;

    /// <summary>
    /// Number of outward-bump passes. More passes produce rougher, thicker irregular walls.
    /// </summary>
    [DataField]
    public int IrregularizePasses = 2;
}

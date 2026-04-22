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
    /// If set, this specific prefab is force-placed in exactly one leaf, overriding the normal
    /// whitelist-driven pick for that leaf. Used to guarantee a landing/entry room (e.g. the
    /// Terradrop7x7a prefab with its TerradropPad) lands somewhere inside the generated dungeon
    /// instead of being spawned separately at a hardcoded map position.
    /// The leaf chosen is the one whose centre is closest to the dungeon centre, among leaves
    /// where the prefab fits with either 0° or 90° rotation and the current <see cref="PrefabMargin"/>.
    /// </summary>
    [DataField]
    public ProtoId<DungeonRoomPrototype>? GuaranteedPrefab;

    /// <summary>
    /// Tile used to fill leaves and corridors.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> FallbackTile = "FloorSteel";

    /// <summary>
    /// After the spanning tree of sibling corridors is built, this many additional T-junction
    /// corridors are added — each from an unused leaf compass-midpoint door to the nearest
    /// existing corridor tile that's at least <see cref="MinExtraJunctionDistance"/> tiles away.
    /// Deterministic (shortest-first among valid candidates). Produces loop branches on top of
    /// the tree. Set to 0 to disable.
    /// </summary>
    [DataField]
    public int ExtraJunctions = 1;

    /// <summary>
    /// Minimum Manhattan distance (in tiles) between an extra-junction door and its target
    /// corridor tile. Guarantees the extra corridor actually spans new ground rather than
    /// stubbing to a corridor already hugging the door's own leaf.
    /// </summary>
    [DataField]
    public int MinExtraJunctionDistance = 8;

    /// <summary>
    /// Additional random corridors between the root's two child sub-regions, on top of the
    /// single deterministic closest-pair connection. Endpoints (door or corridor tile) are
    /// picked uniformly at random from each half — this is the one spot where randomness is
    /// intentional, to break up the otherwise rigid big-picture split of the dungeon.
    /// </summary>
    [DataField]
    public int RootExtras = 1;

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

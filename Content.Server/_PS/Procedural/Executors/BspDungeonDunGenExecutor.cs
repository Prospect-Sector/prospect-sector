using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Shared._PS.Procedural;
using Content.Shared.Procedural;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Executor for <see cref="BspDungeonDunGen"/>.
/// Runs four phases: partition, plan prefab placements per leaf, apply shift-to-align constraints
/// between sibling-leaf pairs, then commit placements and emit corridors through the compass-midpoint
/// door slots the prefabs leave clear by design.
/// </summary>
public sealed class BspDungeonDunGenExecutor : LayerExecutorBase<BspDungeonDunGen>
{
    private static readonly Angle[] RotationOptions =
    {
        Angle.Zero,
        new(Math.PI / 2),
    };

    private readonly ISawmill _log;

    public BspDungeonDunGenExecutor(DungeonGenerationContext context, ISawmill log)
        : base(context)
    {
        _log = log;
    }

    protected override Task ExecuteAsync(
        BspDungeonDunGen layer,
        Dungeon dungeon,
        Vector2i position,
        Random random)
    {
        var halfBounds = layer.Bounds / 2;
        var rootBounds = new Box2i(
            position.X - halfBounds.X,
            position.Y - halfBounds.Y,
            position.X + (layer.Bounds.X - halfBounds.X),
            position.Y + (layer.Bounds.Y - halfBounds.Y));

        var root = BspPartitioner.Partition(
            rootBounds,
            layer.MinLeafSize,
            layer.MaxLeafSize,
            layer.SplitRatioMin,
            layer.SplitRatioMax,
            random);

        var pool = BuildPrefabPool(layer);
        var plans = new Dictionary<BspNode, LeafPlan>();

        foreach (var leaf in root.Leaves())
        {
            plans[leaf] = PlanLeaf(leaf, pool, layer.PrefabMargin, random);
        }

        // If the config asks for a specific prefab to be guaranteed-placed somewhere in the
        // dungeon (e.g. a landing/portal room that must exist), pick the leaf whose centre is
        // closest to the dungeon centre and override its plan. This replaces the older pattern
        // of spawning the landing prefab separately at a hardcoded map coordinate.
        if (layer.GuaranteedPrefab is { } guaranteedId
            && Context.Prototype.TryIndex<DungeonRoomPrototype>(guaranteedId, out var guaranteedProto))
        {
            ForcePlaceGuaranteedPrefab(guaranteedProto, plans, layer.PrefabMargin, position, random);
        }

        var fallbackTileDef = Context.TileDef[layer.FallbackTile];

        // Commit placements first so prefab bounds (and thus compass midpoint doors) exist
        // before the bottom-up corridor pass reads them.
        foreach (var plan in plans.Values)
        {
            CommitLeaf(plan, dungeon, fallbackTileDef, layer.PrefabMargin);
        }

        // Bottom-up corridor construction (RogueBasin style): each internal node connects its
        // two sub-regions at the pair of endpoints with the shortest corridor. Endpoints are
        // either compass-midpoint doors of leaves or existing corridor tiles from deeper levels
        // — that's what turns the tree of sibling links into a branching graph instead of a snake.
        // The root is special-cased so we can emit additional randomized cross-half corridors.
        ProcessRoot(root, plans, dungeon, fallbackTileDef, layer.CorridorWidth, layer.RootExtras, random);

        // Post-pass: a handful of extra T-junction corridors on top of the spanning tree, so
        // the topology has some loops instead of being a strict N-1 tree.
        AddExtraJunctions(
            plans, dungeon, fallbackTileDef,
            layer.CorridorWidth, layer.ExtraJunctions, layer.MinExtraJunctionDistance);

        // Tiles that became corridor floor must not be walled, whether they were previously
        // tracked as room exterior (adjacent to a prefab) or corridor exterior (adjacent to a
        // corridor built earlier in the pass). The second line fixes walls appearing inside
        // corridor-over-corridor crossings.
        dungeon.RoomExteriorTiles.ExceptWith(dungeon.CorridorTiles);
        dungeon.CorridorExteriorTiles.ExceptWith(dungeon.CorridorTiles);
        dungeon.RefreshAllTiles();

        if (layer.Irregularize && layer.IrregularizePasses > 0 && layer.IrregularizeChance > 0f)
            ApplyIrregularization(dungeon, random, layer.IrregularizeChance, layer.IrregularizePasses);

        _log.Debug(
            $"BSP partitioned {rootBounds}: {plans.Count} leaves, {pool.Length} prefabs in pool");

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------------------------------
    // Phase 1: plan
    // ------------------------------------------------------------------------------------------

    private DungeonRoomPrototype[] BuildPrefabPool(BspDungeonDunGen layer)
    {
        var tags = layer.RoomWhitelist?.Tags;
        if (tags == null || tags.Count == 0)
            return Array.Empty<DungeonRoomPrototype>();

        var list = new List<DungeonRoomPrototype>(64);
        foreach (var proto in Context.Prototype.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            foreach (var tag in tags)
            {
                if (!proto.Tags.Contains(tag))
                    continue;
                list.Add(proto);
                break;
            }
        }

        list.Sort((a, b) => string.Compare(a.ID, b.ID, StringComparison.Ordinal));
        return list.ToArray();
    }

    private LeafPlan PlanLeaf(BspNode leaf, DungeonRoomPrototype[] pool, int prefabMargin, Random random)
    {
        var plan = new LeafPlan { Leaf = leaf };

        if (pool.Length > 0)
        {
            var order = Enumerable.Range(0, pool.Length).ToArray();
            Shuffle(order, random);

            foreach (var idx in order)
            {
                var proto = pool[idx];
                foreach (var rotation in RotationOptions)
                {
                    var rotated = rotation != Angle.Zero;
                    var destSize = rotated ? new Vector2i(proto.Size.Y, proto.Size.X) : proto.Size;

                    if (!TryComputeCenterRange(leaf.Bounds, destSize, prefabMargin, out var cxMin, out var cxMax, out var cyMin, out var cyMax))
                        continue;

                    plan.Prefab = proto;
                    plan.Rotation = rotation;
                    plan.DestSize = destSize;
                    plan.CxMin = cxMin;
                    plan.CxMax = cxMax;
                    plan.CyMin = cyMin;
                    plan.CyMax = cyMax;
                    plan.Center = new Vector2i(
                        random.Next(cxMin, cxMax + 1),
                        random.Next(cyMin, cyMax + 1));
                    return plan;
                }
            }
        }

        // Fallback: no prefab fits. The leaf will be filled with plain floor.
        plan.Prefab = null;
        return plan;
    }

    /// <summary>
    /// Overrides one leaf's plan to place the supplied prefab, picking the leaf whose centre is
    /// closest (Manhattan) to the dungeon centre and where the prefab fits with either rotation.
    /// If no leaf fits the prefab at the current margin, the guarantee is silently skipped — the
    /// dungeon still generates, it just won't contain this specific prefab.
    /// </summary>
    private void ForcePlaceGuaranteedPrefab(
        DungeonRoomPrototype proto,
        Dictionary<BspNode, LeafPlan> plans,
        int prefabMargin,
        Vector2i dungeonCenter,
        Random random)
    {
        LeafPlan? bestPlan = null;
        Angle bestRotation = Angle.Zero;
        Vector2i bestDestSize = default;
        int bestCxMin = 0, bestCxMax = 0, bestCyMin = 0, bestCyMax = 0;
        var bestDist = int.MaxValue;

        foreach (var plan in plans.Values)
        {
            var leafCenter = new Vector2i(
                (plan.Leaf.Bounds.Left + plan.Leaf.Bounds.Right) / 2,
                (plan.Leaf.Bounds.Bottom + plan.Leaf.Bounds.Top) / 2);
            var dist = Math.Abs(leafCenter.X - dungeonCenter.X) + Math.Abs(leafCenter.Y - dungeonCenter.Y);
            if (dist >= bestDist)
                continue;

            foreach (var rotation in RotationOptions)
            {
                var rotated = rotation != Angle.Zero;
                var destSize = rotated ? new Vector2i(proto.Size.Y, proto.Size.X) : proto.Size;

                if (!TryComputeCenterRange(plan.Leaf.Bounds, destSize, prefabMargin, out var cxMin, out var cxMax, out var cyMin, out var cyMax))
                    continue;

                bestDist = dist;
                bestPlan = plan;
                bestRotation = rotation;
                bestDestSize = destSize;
                bestCxMin = cxMin;
                bestCxMax = cxMax;
                bestCyMin = cyMin;
                bestCyMax = cyMax;
                break; // prefer 0° rotation if both fit — more predictable layout
            }
        }

        if (bestPlan == null)
        {
            _log.Warning($"BSP: no leaf fit guaranteed prefab '{proto.ID}' with margin {prefabMargin}");
            return;
        }

        bestPlan.Prefab = proto;
        bestPlan.Rotation = bestRotation;
        bestPlan.DestSize = bestDestSize;
        bestPlan.CxMin = bestCxMin;
        bestPlan.CxMax = bestCxMax;
        bestPlan.CyMin = bestCyMin;
        bestPlan.CyMax = bestCyMax;
        bestPlan.Center = new Vector2i(
            random.Next(bestCxMin, bestCxMax + 1),
            random.Next(bestCyMin, bestCyMax + 1));
    }

    private static bool TryComputeCenterRange(
        Box2i leafBounds,
        Vector2i destSize,
        int margin,
        out int cxMin,
        out int cxMax,
        out int cyMin,
        out int cyMax)
    {
        // Leaf interior reserves a margin of `margin` tiles on all sides. This protects L-bend
        // pivot blocks from overlapping neighbouring prefabs' exterior walls.
        // Prefab of size S placed at center c occupies tiles [c - S/2, c + (S-1)/2] (integer math,
        // matching DungeonSystem.SpawnRoom's transform rounding).
        cxMin = leafBounds.Left + margin + destSize.X / 2;
        cxMax = leafBounds.Right - margin - (destSize.X + 1) / 2;
        cyMin = leafBounds.Bottom + margin + destSize.Y / 2;
        cyMax = leafBounds.Top - margin - (destSize.Y + 1) / 2;
        return cxMin <= cxMax && cyMin <= cyMax;
    }

    // ------------------------------------------------------------------------------------------
    // Phase 2: collect pairs and apply shift-to-align
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Root-specific entry point. Builds the two halves' sub-regions recursively, emits the
    /// deterministic closest-pair corridor between them, then emits <paramref name="extras"/>
    /// additional random corridors across the root split. The extras are the one sanctioned
    /// place for randomness in corridor construction — they reliably break up the big-picture
    /// rigid-split look without touching the bottom-up deterministic tree.
    /// </summary>
    private void ProcessRoot(
        BspNode root,
        Dictionary<BspNode, LeafPlan> plans,
        Dungeon dungeon,
        ITileDefinition tileDef,
        int corridorWidth,
        int extras,
        Random random)
    {
        if (root.IsLeaf)
            return;

        var leftSub = BuildSubRegion(root.Left!, plans, dungeon, tileDef, corridorWidth);
        var rightSub = BuildSubRegion(root.Right!, plans, dungeon, tileDef, corridorWidth);

        if (TryFindClosestPair(leftSub, rightSub, out var nl, out var nr))
            EmitCorridorBetween(nl, nr, dungeon, tileDef, corridorWidth);

        if (extras <= 0 || leftSub.Endpoints.Count == 0 || rightSub.Endpoints.Count == 0)
            return;

        for (var i = 0; i < extras; i++)
        {
            var l = leftSub.Endpoints[random.Next(leftSub.Endpoints.Count)];
            var r = rightSub.Endpoints[random.Next(rightSub.Endpoints.Count)];
            EmitCorridorBetween(l, r, dungeon, tileDef, corridorWidth);
        }
    }

    /// <summary>
    /// Walks the BSP tree bottom-up. At each internal node the two child sub-regions are already
    /// internally connected; this method picks the closest pair of endpoints (one from each side)
    /// and emits a single corridor between them. Endpoints may be either leaf compass-midpoint
    /// doors or corridor tiles from deeper levels — the latter produces T-junctions, which is
    /// what breaks the otherwise linear "snake" topology.
    /// </summary>
    private SubRegion BuildSubRegion(
        BspNode node,
        Dictionary<BspNode, LeafPlan> plans,
        Dungeon dungeon,
        ITileDefinition tileDef,
        int corridorWidth)
    {
        if (node.IsLeaf)
        {
            var region = new SubRegion();
            if (plans.TryGetValue(node, out var plan) && plan.Room != null)
                AddLeafDoorCandidates(plan, region);
            return region;
        }

        var left = BuildSubRegion(node.Left!, plans, dungeon, tileDef, corridorWidth);
        var right = BuildSubRegion(node.Right!, plans, dungeon, tileDef, corridorWidth);

        var merged = new SubRegion();
        merged.Endpoints.AddRange(left.Endpoints);
        merged.Endpoints.AddRange(right.Endpoints);

        if (!TryFindClosestPair(left, right, out var bestLeft, out var bestRight))
            return merged;

        var emitted = EmitCorridorBetween(bestLeft, bestRight, dungeon, tileDef, corridorWidth);
        foreach (var tile in emitted)
            merged.Endpoints.Add(new Endpoint(tile, null));

        return merged;
    }

    /// <summary>
    /// Appends up to <paramref name="maxExtras"/> corridors on top of the spanning tree, each
    /// going from a currently-unused leaf compass-midpoint door to the nearest existing corridor
    /// tile. Deterministic: candidates are scored by Manhattan distance and the shortest are
    /// emitted first. Produces loop edges with guaranteed T-junction endpoints.
    /// </summary>
    private void AddExtraJunctions(
        Dictionary<BspNode, LeafPlan> plans,
        Dungeon dungeon,
        ITileDefinition tileDef,
        int corridorWidth,
        int maxExtras,
        int minDistance)
    {
        if (maxExtras <= 0 || dungeon.CorridorTiles.Count == 0)
            return;

        // Collect every spare door with its nearest corridor tile that lies at least
        // `minDistance` Manhattan tiles away. This filters out stubs that would only connect
        // to a corridor already hugging the door's own leaf.
        var candidates = new List<(Endpoint Door, Vector2i Target, int Dist)>();
        Span<Vector2i> doors = stackalloc Vector2i[4];
        foreach (var plan in plans.Values)
        {
            if (plan.Room == null)
                continue;

            var b = plan.Room.Bounds;
            var midX = (b.Left + b.Right) / 2;
            var midY = (b.Bottom + b.Top) / 2;

            doors[0] = new(b.Right + 1, midY);
            doors[1] = new(b.Left - 1, midY);
            doors[2] = new(midX, b.Top + 1);
            doors[3] = new(midX, b.Bottom - 1);

            foreach (var door in doors)
            {
                if (dungeon.Entrances.Contains(door))
                    continue;

                var bestDist = int.MaxValue;
                var bestTarget = Vector2i.Zero;
                foreach (var corridorTile in dungeon.CorridorTiles)
                {
                    var dist = Math.Abs(door.X - corridorTile.X) + Math.Abs(door.Y - corridorTile.Y);
                    if (dist < minDistance)
                        continue;
                    if (dist >= bestDist)
                        continue;
                    bestDist = dist;
                    bestTarget = corridorTile;
                }

                if (bestDist != int.MaxValue)
                    candidates.Add((new Endpoint(door, plan), bestTarget, bestDist));
            }
        }

        candidates.Sort(static (a, b) => a.Dist.CompareTo(b.Dist));

        var emitted = 0;
        foreach (var (door, target, _) in candidates)
        {
            if (emitted >= maxExtras)
                break;
            // Recheck — an earlier extra may have registered this door as an entrance.
            if (dungeon.Entrances.Contains(door.Pos))
                continue;

            EmitCorridorBetween(door, new Endpoint(target, null), dungeon, tileDef, corridorWidth);
            emitted++;
        }

        _log.Debug($"BSP added {emitted}/{maxExtras} extra T-junction corridors (min distance {minDistance})");
    }

    private static void AddLeafDoorCandidates(LeafPlan plan, SubRegion region)
    {
        var b = plan.Room!.Bounds;
        var midX = (b.Left + b.Right) / 2;
        var midY = (b.Bottom + b.Top) / 2;
        region.Endpoints.Add(new Endpoint(new Vector2i(b.Right + 1, midY), plan));
        region.Endpoints.Add(new Endpoint(new Vector2i(b.Left - 1, midY), plan));
        region.Endpoints.Add(new Endpoint(new Vector2i(midX, b.Top + 1), plan));
        region.Endpoints.Add(new Endpoint(new Vector2i(midX, b.Bottom - 1), plan));
    }

    /// <summary>
    /// How many extra tiles of Manhattan distance we're willing to pay in exchange for a link
    /// whose endpoint is an existing corridor tile (T-junction) rather than a leaf door. Biasing
    /// the selection this way turns boundary-to-boundary leaf chains into genuine branches at
    /// upper levels without introducing any randomness — the spec's "room-room / corridor-room /
    /// corridor-corridor" link enumeration is honoured as a preference rather than a dice roll.
    /// </summary>
    private const int CorridorEndpointBias = 4;

    private static bool TryFindClosestPair(SubRegion left, SubRegion right, out Endpoint bestLeft, out Endpoint bestRight)
    {
        bestLeft = default;
        bestRight = default;

        var bestDoorDist = int.MaxValue;
        Endpoint bestDoorLeft = default;
        Endpoint bestDoorRight = default;
        var foundDoor = false;

        var bestCorridorDist = int.MaxValue;
        Endpoint bestCorridorLeft = default;
        Endpoint bestCorridorRight = default;
        var foundCorridor = false;

        foreach (var l in left.Endpoints)
        {
            foreach (var r in right.Endpoints)
            {
                var dist = Math.Abs(l.Pos.X - r.Pos.X) + Math.Abs(l.Pos.Y - r.Pos.Y);
                var corridorInvolved = l.Owner == null || r.Owner == null;

                if (corridorInvolved)
                {
                    if (dist < bestCorridorDist)
                    {
                        bestCorridorDist = dist;
                        bestCorridorLeft = l;
                        bestCorridorRight = r;
                        foundCorridor = true;
                    }
                }
                else if (dist < bestDoorDist)
                {
                    bestDoorDist = dist;
                    bestDoorLeft = l;
                    bestDoorRight = r;
                    foundDoor = true;
                }
            }
        }

        // Prefer a corridor-involving pair whenever it's within tolerance of the closest pure
        // door-to-door pair. At the deepest level only door pairs exist (no corridors built yet),
        // so this collapses to the original closest-pair behaviour.
        if (foundCorridor && (!foundDoor || bestCorridorDist <= bestDoorDist + CorridorEndpointBias))
        {
            bestLeft = bestCorridorLeft;
            bestRight = bestCorridorRight;
            return true;
        }

        if (foundDoor)
        {
            bestLeft = bestDoorLeft;
            bestRight = bestDoorRight;
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------
    // Phase 3: commit placements
    // ------------------------------------------------------------------------------------------

    private void CommitLeaf(LeafPlan plan, Dungeon dungeon, ITileDefinition fallbackTileDef, int prefabMargin)
    {
        if (plan.Prefab != null)
        {
            plan.Room = SpawnPrefabRoom(plan);
        }
        else
        {
            plan.Room = FillFallbackRoom(plan.Leaf, fallbackTileDef, prefabMargin);
        }

        if (plan.Room != null)
            dungeon.AddRoom(plan.Room);
    }

    private DungeonRoom SpawnPrefabRoom(LeafPlan plan)
    {
        var proto = plan.Prefab!;

        // The transform's translation must land the prefab's geometric center on a tile-aligned
        // world position: integer corner for even-sized prefabs, integer + 0.5 for odd-sized.
        // Feeding a bare integer (plan.Center) instead offsets decals by (-0.5, -0.5) on odd
        // dimensions because tile placement floors away the mismatch but decal coordinates do not.
        var originX = plan.Center.X - plan.DestSize.X / 2;
        var originY = plan.Center.Y - plan.DestSize.Y / 2;
        var centerPoint = new Vector2(originX, originY) + (Vector2)plan.DestSize / 2f;
        var transform = Matrix3Helpers.CreateTransform(centerPoint, plan.Rotation);

        Context.RoomSpawnCommands.Enqueue(new RoomSpawnCommand(proto, transform));

        var roomCenter = (proto.Offset + proto.Size / 2f) * Context.Grid.TileSize;
        var tileOffset = -roomCenter + Context.Grid.TileSizeHalfVector;

        var tiles = new HashSet<Vector2i>(proto.Size.X * proto.Size.Y);
        var boundsMin = new Vector2i(int.MaxValue, int.MaxValue);
        var boundsMax = new Vector2i(int.MinValue, int.MinValue);
        var centerAccum = Vector2.Zero;

        for (var x = 0; x < proto.Size.X; x++)
        {
            for (var y = 0; y < proto.Size.Y; y++)
            {
                var src = new Vector2(x + proto.Offset.X, y + proto.Offset.Y);
                var destWorld = Vector2.Transform(src + tileOffset, transform);
                var destTile = destWorld.Floored();

                tiles.Add(destTile);
                centerAccum += destWorld + Context.Grid.TileSizeHalfVector;

                if (destTile.X < boundsMin.X) boundsMin.X = destTile.X;
                if (destTile.Y < boundsMin.Y) boundsMin.Y = destTile.Y;
                if (destTile.X > boundsMax.X) boundsMax.X = destTile.X;
                if (destTile.Y > boundsMax.Y) boundsMax.Y = destTile.Y;
            }
        }

        plan.DestBounds = new Box2i(boundsMin, boundsMax);
        var center = centerAccum / Math.Max(1, tiles.Count);

        var exterior = new HashSet<Vector2i>(2 * (plan.DestBounds.Width + plan.DestBounds.Height) + 4);
        for (var x = plan.DestBounds.Left - 1; x <= plan.DestBounds.Right + 1; x++)
        {
            for (var y = plan.DestBounds.Bottom - 1; y <= plan.DestBounds.Top + 1; y++)
            {
                var tile = new Vector2i(x, y);
                if (tiles.Contains(tile))
                    continue;
                exterior.Add(tile);
            }
        }

        return new DungeonRoom(tiles, center, plan.DestBounds, exterior);
    }

    private DungeonRoom? FillFallbackRoom(BspNode leaf, ITileDefinition tileDef, int margin)
    {
        var interiorLeft = leaf.Bounds.Left + margin;
        var interiorBottom = leaf.Bounds.Bottom + margin;
        var interiorRight = leaf.Bounds.Right - margin;
        var interiorTop = leaf.Bounds.Top - margin;

        if (interiorRight <= interiorLeft || interiorTop <= interiorBottom)
            return null;

        var area = (interiorRight - interiorLeft) * (interiorTop - interiorBottom);
        var tiles = new HashSet<Vector2i>(area);
        var center = Vector2.Zero;

        for (var x = interiorLeft; x < interiorRight; x++)
        {
            for (var y = interiorBottom; y < interiorTop; y++)
            {
                var tile = new Vector2i(x, y);
                if (!IsTileAvailable(tile))
                    continue;

                QueueTile(tile, new Tile(tileDef.TileId));
                tiles.Add(tile);
                center += new Vector2(x, y) + Context.Grid.TileSizeHalfVector;
            }
        }

        if (tiles.Count == 0)
            return null;

        center /= tiles.Count;
        var bounds = new Box2i(interiorLeft, interiorBottom, interiorRight - 1, interiorTop - 1);

        var exterior = new HashSet<Vector2i>(2 * ((interiorRight - interiorLeft) + (interiorTop - interiorBottom)) + 4);
        for (var x = interiorLeft - 1; x <= interiorRight; x++)
        {
            for (var y = interiorBottom - 1; y <= interiorTop; y++)
            {
                if (x >= interiorLeft && x < interiorRight && y >= interiorBottom && y < interiorTop)
                    continue;
                exterior.Add(new Vector2i(x, y));
            }
        }

        return new DungeonRoom(tiles, center, bounds, exterior);
    }

    // ------------------------------------------------------------------------------------------
    // Phase 4: corridors
    // ------------------------------------------------------------------------------------------

    private const int DoorApproachLength = 3;

    /// <summary>
    /// Emits the corridor between two pre-selected endpoints. Endpoints may be either leaf
    /// compass-midpoint doors (Owner != null) — which get registered as <see cref="DungeonRoom.Entrances"/>
    /// so airlocks spawn — or existing corridor tiles (Owner == null) — which just T-join the
    /// existing corridor without a door. Returns the tiles actually written (for merging into
    /// the sub-region's endpoint list at the level above).
    ///
    /// Corridors are built in three parts so each door gets a straight <see cref="DoorApproachLength"/>-tile
    /// run-up: door → approach (tapered at door), approach → approach (plain L-bend, no taper), then
    /// approach → door on the other side. This prevents the L-bend pivot from landing adjacent to
    /// the door, which would produce awkward diagonal entries.
    /// </summary>
    private List<Vector2i> EmitCorridorBetween(
        Endpoint neg,
        Endpoint pos,
        Dungeon dungeon,
        ITileDefinition tileDef,
        int corridorWidth)
    {
        var negDoor = neg.Pos;
        var posDoor = pos.Pos;
        var negFace = ComputeFaceDir(neg);
        var posFace = ComputeFaceDir(pos);

        var negApproach = negDoor + negFace * DoorApproachLength;
        var posApproach = posDoor + posFace * DoorApproachLength;

        var halfW = corridorWidth / 2;
        var spine = new HashSet<Vector2i>();
        var flank = new HashSet<Vector2i>();

        AddDoorApproach(negDoor, negApproach, negFace, halfW, spine, flank);
        AddDoorApproach(posDoor, posApproach, posFace, halfW, spine, flank);
        BuildMiddleBend(negApproach, posApproach, halfW, spine, flank);

        var emitted = new List<Vector2i>();

        // Spine tiles: strict. Never overwrite room interiors or exterior wall ring, except at
        // the two sanctioned door tiles. This keeps the main axis of the corridor from punching
        // through prefab walls.
        foreach (var tile in spine)
        {
            if (!IsTileAvailable(tile))
                continue;
            if (dungeon.RoomTiles.Contains(tile))
                continue;
            if (tile != negDoor && tile != posDoor && dungeon.RoomExteriorTiles.Contains(tile))
                continue;

            if (!dungeon.CorridorTiles.Contains(tile))
            {
                QueueTile(tile, new Tile(tileDef.TileId));
                dungeon.CorridorTiles.Add(tile);
            }
            emitted.Add(tile);
        }

        // Flank tiles: relaxed vs spine, but not unconditionally. A flank may overwrite a
        // neighbour's exterior ring ONLY if that ring tile isn't cardinally adjacent to a prefab
        // interior — i.e., only CORNER exterior tiles, whose purpose is cosmetic, not protective.
        // Edge exterior tiles stand between the corridor and the prefab interior: overwriting one
        // replaces the wall with corridor floor immediately adjacent to the room floor, giving an
        // unintended side entry into the prefab. The cardinal-neighbour test is what tells the two
        // cases apart.
        foreach (var tile in flank)
        {
            if (!IsTileAvailable(tile))
                continue;
            if (dungeon.RoomTiles.Contains(tile))
                continue;
            if (spine.Contains(tile))
                continue;
            if (HasCardinalRoomTileNeighbor(dungeon, tile))
                continue;

            if (!dungeon.CorridorTiles.Contains(tile))
            {
                QueueTile(tile, new Tile(tileDef.TileId));
                dungeon.CorridorTiles.Add(tile);
            }
            emitted.Add(tile);
        }

        foreach (var tile in emitted)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var neighbor = new Vector2i(tile.X + dx, tile.Y + dy);
                    if (dungeon.CorridorTiles.Contains(neighbor))
                        continue;
                    dungeon.CorridorExteriorTiles.Add(neighbor);
                }
            }
        }

        // Only register entrance airlocks at leaf-owned endpoints. Endpoints that are existing
        // corridor tiles (Owner == null) are T-junctions and don't get airlocks.
        if (neg.Owner?.Room != null)
            RegisterEntrance(neg.Owner.Room, dungeon, negDoor);
        if (pos.Owner?.Room != null)
            RegisterEntrance(pos.Owner.Room, dungeon, posDoor);

        return emitted;
    }

    /// <summary>
    /// Returns the outward-facing cardinal direction of a leaf-owned door, or <c>Vector2i.Zero</c>
    /// for corridor-tile endpoints (which have no face direction — the corridor just T-joins there).
    /// </summary>
    private static Vector2i ComputeFaceDir(Endpoint endpoint)
    {
        if (endpoint.Owner?.Room == null)
            return Vector2i.Zero;

        var bounds = endpoint.Owner.Room.Bounds;
        var pos = endpoint.Pos;

        if (pos.X == bounds.Right + 1) return new Vector2i(1, 0);   // East face
        if (pos.X == bounds.Left - 1) return new Vector2i(-1, 0);   // West face
        if (pos.Y == bounds.Top + 1) return new Vector2i(0, 1);     // North face
        if (pos.Y == bounds.Bottom - 1) return new Vector2i(0, -1); // South face
        return Vector2i.Zero;
    }

    /// <summary>
    /// Adds the straight 3-wide approach segment from a door toward its approach point. Tapers
    /// to 1 at the door tile (so airlock placement works) but stays 3-wide at the approach end
    /// where it meets the middle bend. Spine (centre line) and flank (±1 perpendicular) tiles go
    /// into separate sets so the emission pass can apply different overlap rules to each — see
    /// <c>EmitCorridorBetween</c> for why.
    /// </summary>
    private static void AddDoorApproach(
        Vector2i door,
        Vector2i approach,
        Vector2i faceDir,
        int halfW,
        HashSet<Vector2i> spine,
        HashSet<Vector2i> flank)
    {
        if (faceDir == Vector2i.Zero || door == approach)
            return;

        if (faceDir.Y == 0)
        {
            var xA = Math.Min(door.X, approach.X);
            var xB = Math.Max(door.X, approach.X);
            AddHorizontalSegment(xA, xB, door.Y, halfW, spine, flank, doorAtA: door.X == xA, doorAtB: door.X == xB);
        }
        else
        {
            var yA = Math.Min(door.Y, approach.Y);
            var yB = Math.Max(door.Y, approach.Y);
            AddVerticalSegment(door.X, yA, yB, halfW, spine, flank, doorAtA: door.Y == yA, doorAtB: door.Y == yB);
        }
    }

    /// <summary>
    /// Connects two approach points with a straight segment or an L-bend. Both ends stay 3-wide
    /// because neither is a door — the taper lives in the door-approach segments instead.
    /// Populates <paramref name="spine"/> and <paramref name="flank"/> separately (see
    /// <c>EmitCorridorBetween</c> for the rationale).
    /// </summary>
    private static void BuildMiddleBend(
        Vector2i a,
        Vector2i b,
        int halfW,
        HashSet<Vector2i> spine,
        HashSet<Vector2i> flank)
    {
        if (a == b)
            return;

        if (a.X == b.X)
        {
            AddVerticalSegment(a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), halfW, spine, flank, doorAtA: false, doorAtB: false);
            return;
        }

        if (a.Y == b.Y)
        {
            AddHorizontalSegment(Math.Min(a.X, b.X), Math.Max(a.X, b.X), a.Y, halfW, spine, flank, doorAtA: false, doorAtB: false);
            return;
        }

        if (a.X > b.X) (a, b) = (b, a);
        var pivotX = Math.Clamp((a.X + b.X) / 2, a.X, b.X);

        AddHorizontalSegment(a.X, pivotX, a.Y, halfW, spine, flank, doorAtA: false, doorAtB: false);
        AddVerticalSegment(pivotX, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), halfW, spine, flank, doorAtA: false, doorAtB: false);
        AddHorizontalSegment(pivotX, b.X, b.Y, halfW, spine, flank, doorAtA: false, doorAtB: false);

        AddPivotBlock(new Vector2i(pivotX, a.Y), halfW, spine, flank);
        AddPivotBlock(new Vector2i(pivotX, b.Y), halfW, spine, flank);
    }

    /// <summary>
    /// Emits a horizontal corridor segment. The centre line is added to <paramref name="spine"/>
    /// and the ±halfW perpendicular tiles go into <paramref name="flank"/>. Doors taper to 1 on
    /// the flanking set (so the corridor narrows to the door tile only at the face that needs it).
    /// Keeping the two sets separate lets the emission pass treat spine as the strict "wall-safe"
    /// path and flank as a widening allowed to clip neighbours' exterior wall ring.
    /// </summary>
    private static void AddHorizontalSegment(
        int xA,
        int xB,
        int y,
        int halfW,
        HashSet<Vector2i> spine,
        HashSet<Vector2i> flank,
        bool doorAtA,
        bool doorAtB)
    {
        if (xA > xB) (xA, xB) = (xB, xA);

        for (var x = xA; x <= xB; x++)
            spine.Add(new Vector2i(x, y));

        var flankXStart = doorAtA ? xA + 1 : xA;
        var flankXEnd = doorAtB ? xB - 1 : xB;
        for (var x = flankXStart; x <= flankXEnd; x++)
        {
            for (var dy = 1; dy <= halfW; dy++)
            {
                flank.Add(new Vector2i(x, y + dy));
                flank.Add(new Vector2i(x, y - dy));
            }
        }
    }

    /// <summary>
    /// Vertical counterpart of <see cref="AddHorizontalSegment"/>. Same spine/flank semantics.
    /// </summary>
    private static void AddVerticalSegment(
        int x,
        int yA,
        int yB,
        int halfW,
        HashSet<Vector2i> spine,
        HashSet<Vector2i> flank,
        bool doorAtA,
        bool doorAtB)
    {
        if (yA > yB) (yA, yB) = (yB, yA);

        for (var y = yA; y <= yB; y++)
            spine.Add(new Vector2i(x, y));

        var flankYStart = doorAtA ? yA + 1 : yA;
        var flankYEnd = doorAtB ? yB - 1 : yB;
        for (var y = flankYStart; y <= flankYEnd; y++)
        {
            for (var dx = 1; dx <= halfW; dx++)
            {
                flank.Add(new Vector2i(x + dx, y));
                flank.Add(new Vector2i(x - dx, y));
            }
        }
    }

    /// <summary>
    /// Smooths an L-bend pivot corner by filling the (2·halfW+1)² block around the pivot. The
    /// centre tile is the pivot itself (spine); the surrounding 8 act as flank-widening so the
    /// corner isn't a single-tile pinch. Kept separate so spine/flank overlap rules still apply.
    /// </summary>
    private static void AddPivotBlock(Vector2i pivot, int halfW, HashSet<Vector2i> spine, HashSet<Vector2i> flank)
    {
        for (var dx = -halfW; dx <= halfW; dx++)
        {
            for (var dy = -halfW; dy <= halfW; dy++)
            {
                var tile = new Vector2i(pivot.X + dx, pivot.Y + dy);
                if (dx == 0 && dy == 0)
                    spine.Add(tile);
                else
                    flank.Add(tile);
            }
        }
    }

    private static void RegisterEntrance(DungeonRoom room, Dungeon dungeon, Vector2i pos)
    {
        if (!room.Entrances.Contains(pos))
            room.Entrances.Add(pos);
        dungeon.Entrances.Add(pos);
    }

    /// <summary>
    /// True if any of the 4 cardinal neighbours of <paramref name="tile"/> is a prefab interior
    /// tile. Used to keep corridor flanks from overwriting edge exterior walls, which would otherwise
    /// leave corridor floor sitting directly next to room floor with no wall between them.
    /// </summary>
    private static bool HasCardinalRoomTileNeighbor(Dungeon dungeon, Vector2i tile)
    {
        return dungeon.RoomTiles.Contains(new Vector2i(tile.X + 1, tile.Y))
            || dungeon.RoomTiles.Contains(new Vector2i(tile.X - 1, tile.Y))
            || dungeon.RoomTiles.Contains(new Vector2i(tile.X, tile.Y + 1))
            || dungeon.RoomTiles.Contains(new Vector2i(tile.X, tile.Y - 1));
    }

    // ------------------------------------------------------------------------------------------
    // Phase 5: irregularization (cave/mineshaft biomes)
    // ------------------------------------------------------------------------------------------

    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    /// <summary>
    /// Thickens the dungeon's outer wall silhouette by randomly extending exterior tiles outward
    /// into otherwise-empty space. Produces a rough, cave-like outline without touching prefab
    /// interiors, rooms, or corridors.
    /// </summary>
    private static void ApplyIrregularization(Dungeon dungeon, Random random, float chance, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var bumps = new HashSet<Vector2i>();
            var sources = new List<Vector2i>(dungeon.RoomExteriorTiles.Count + dungeon.CorridorExteriorTiles.Count);
            sources.AddRange(dungeon.RoomExteriorTiles);
            sources.AddRange(dungeon.CorridorExteriorTiles);

            foreach (var source in sources)
            {
                if (random.NextDouble() > chance)
                    continue;

                foreach (var offset in CardinalOffsets)
                {
                    var neighbor = source + offset;
                    if (dungeon.RoomTiles.Contains(neighbor)) continue;
                    if (dungeon.CorridorTiles.Contains(neighbor)) continue;
                    if (dungeon.RoomExteriorTiles.Contains(neighbor)) continue;
                    if (dungeon.CorridorExteriorTiles.Contains(neighbor)) continue;
                    if (dungeon.Entrances.Contains(neighbor)) continue;

                    bumps.Add(neighbor);
                    break;
                }
            }

            dungeon.RoomExteriorTiles.UnionWith(bumps);
        }

        dungeon.RefreshAllTiles();
    }

    private static void Shuffle(int[] array, Random random)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Plan state types
    // ------------------------------------------------------------------------------------------

    private sealed class LeafPlan
    {
        public BspNode Leaf = null!;
        public DungeonRoomPrototype? Prefab;
        public Angle Rotation;
        public Vector2i DestSize;
        public int CxMin;
        public int CxMax;
        public int CyMin;
        public int CyMax;
        public Vector2i Center;
        public Box2i DestBounds;
        public DungeonRoom? Room;
    }

    /// <summary>
    /// Connection candidate for RogueBasin-style bottom-up corridor building.
    /// </summary>
    /// <param name="Pos">Tile position of the endpoint.</param>
    /// <param name="Owner">The leaf whose door this is, or null if this is an existing corridor tile (T-junction candidate).</param>
    private readonly record struct Endpoint(Vector2i Pos, LeafPlan? Owner);

    private sealed class SubRegion
    {
        public readonly List<Endpoint> Endpoints = new();
    }
}

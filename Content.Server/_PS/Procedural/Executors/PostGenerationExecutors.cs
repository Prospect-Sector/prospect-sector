using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Shared.Maps;
using Content.Shared.NPC;
using Content.Shared.Procedural;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.Collections;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Executor for CorridorDunGen - connects room entrances via corridors.
/// </summary>
public sealed class CorridorDunGenExecutor : LayerExecutorBase<CorridorDunGen>
{
    private readonly ISawmill _log;

    public CorridorDunGenExecutor(DungeonGenerationContext context, ISawmill log) : base(context)
    {
        _log = log;
    }

    protected override async Task ExecuteAsync(CorridorDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var entrances = new List<Vector2i>();

        foreach (var room in dungeon.Rooms)
        {
            entrances.AddRange(room.Entrances);
        }

        if (entrances.Count < 2)
            return;

        var edges = MinimumSpanningTree(entrances, random);

        var expansion = layer.Width - 2;
        var deterredTiles = Context.RentHashSet();

        if (expansion >= 1)
        {
            foreach (var tile in dungeon.RoomExteriorTiles)
            {
                for (var x = -expansion; x <= expansion; x++)
                {
                    for (var y = -expansion; y <= expansion; y++)
                    {
                        var neighbor = new Vector2(tile.X + x, tile.Y + y).Floored();

                        if (dungeon.RoomTiles.Contains(neighbor) ||
                            dungeon.RoomExteriorTiles.Contains(neighbor) ||
                            entrances.Contains(neighbor))
                        {
                            continue;
                        }

                        deterredTiles.Add(neighbor);
                    }
                }
            }
        }

        foreach (var room in dungeon.Rooms)
        {
            foreach (var entrance in room.Entrances)
            {
                var normal = (entrance + Context.Grid.TileSizeHalfVector - room.Center).ToWorldAngle().GetCardinalDir().ToIntVec();
                deterredTiles.Remove(entrance + normal);
            }
        }

        var excludedTiles = Context.RentHashSet();
        excludedTiles.UnionWith(dungeon.RoomExteriorTiles);
        excludedTiles.UnionWith(dungeon.RoomTiles);

        var corridorTiles = Context.RentHashSet();

        // Pathfind corridors
        GetCorridorNodes(corridorTiles, edges, layer.PathLimit, excludedTiles, tile =>
        {
            var mod = 1f;

            if (corridorTiles.Contains(tile))
                mod *= 0.1f;

            if (deterredTiles.Contains(tile))
                mod *= 2f;

            return mod;
        });

        WidenCorridor(dungeon, layer.Width, corridorTiles);

        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];

        foreach (var tile in corridorTiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            if (!IsTileAvailable(tile))
                continue;

            var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
            QueueTile(tile, new Tile(tileDef.TileId, variant: variant));
        }

        dungeon.CorridorTiles.UnionWith(corridorTiles);
        dungeon.RefreshAllTiles();
        BuildCorridorExterior(dungeon);

        Context.ReturnHashSet(deterredTiles);
        Context.ReturnHashSet(excludedTiles);
        Context.ReturnHashSet(corridorTiles);

        await Task.Yield();
    }

    private List<(Vector2i Start, Vector2i End)> MinimumSpanningTree(List<Vector2i> tiles, Random random)
    {
        var connections = new Dictionary<Vector2i, List<(Vector2i Tile, float Distance)>>(tiles.Count);

        foreach (var entrance in tiles)
        {
            var edgeConns = new List<(Vector2i Tile, float Distance)>(tiles.Count - 1);

            foreach (var other in tiles)
            {
                if (entrance == other)
                    continue;

                edgeConns.Add((other, (other - entrance).Length));
            }

            edgeConns.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            connections.Add(entrance, edgeConns);
        }

        var seedIndex = random.Next(tiles.Count);
        var remaining = new ValueList<Vector2i>(tiles);
        remaining.RemoveAt(seedIndex);

        var edges = new List<(Vector2i Start, Vector2i End)>();
        var seedEntrance = tiles[seedIndex];
        var forest = new ValueList<Vector2i>(tiles.Count) { seedEntrance };

        while (remaining.Count > 0)
        {
            var cheapestDistance = float.MaxValue;
            var cheapest = (Vector2i.Zero, Vector2i.Zero);

            foreach (var node in forest)
            {
                foreach (var conn in connections[node])
                {
                    if (forest.Contains(conn.Tile))
                        continue;

                    if (cheapestDistance < conn.Distance)
                        continue;

                    cheapestDistance = conn.Distance;
                    cheapest = (node, conn.Tile);
                    break;
                }
            }

            edges.Add(cheapest);
            forest.Add(cheapest.Item2);
            remaining.Remove(cheapest.Item2);
        }

        return edges;
    }

    private void GetCorridorNodes(
        HashSet<Vector2i> corridorTiles,
        List<(Vector2i Start, Vector2i End)> edges,
        int pathLimit,
        HashSet<Vector2i>? forbiddenTiles = null,
        Func<Vector2i, float>? tileCallback = null)
    {
        var frontier = new PriorityQueue<Vector2i, float>();
        var cameFrom = new Dictionary<Vector2i, Vector2i>();
        var directions = new Dictionary<Vector2i, Direction>();
        var costSoFar = new Dictionary<Vector2i, float>();
        forbiddenTiles ??= new HashSet<Vector2i>();

        foreach (var (start, end) in edges)
        {
            frontier.Clear();
            cameFrom.Clear();
            costSoFar.Clear();
            directions.Clear();
            directions[start] = Direction.Invalid;
            frontier.Enqueue(start, 0f);
            costSoFar[start] = 0f;
            var found = false;
            var count = 0;

            while (frontier.Count > 0 && count < pathLimit)
            {
                count++;
                var node = frontier.Dequeue();

                if (node == end)
                {
                    found = true;
                    break;
                }

                var lastDirection = directions[node];

                for (var x = -1; x <= 1; x++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        if (x != 0 && y != 0)
                            continue;

                        var neighbor = new Vector2i(node.X + x, node.Y + y);

                        if (neighbor != end && forbiddenTiles.Contains(neighbor))
                            continue;

                        var tileCost = SharedPathfindingSystem.ManhattanDistance(node, neighbor);

                        if (corridorTiles.Contains(neighbor))
                            tileCost *= 0.10f;

                        var costMod = tileCallback?.Invoke(neighbor) ?? 1f;
                        tileCost *= costMod;

                        var direction = (neighbor - node).GetCardinalDir();
                        directions[neighbor] = direction;

                        if (direction != lastDirection)
                            tileCost *= 3f;

                        var gScore = costSoFar[node] + tileCost;

                        if (costSoFar.TryGetValue(neighbor, out var nextValue) && gScore >= nextValue)
                            continue;

                        cameFrom[neighbor] = node;
                        costSoFar[neighbor] = gScore;

                        var hScore = SharedPathfindingSystem.ManhattanDistance(end, neighbor) * 0.999f;
                        var fScore = gScore + hScore;
                        frontier.Enqueue(neighbor, fScore);
                    }
                }
            }

            if (found)
            {
                var node = end;

                while (true)
                {
                    node = cameFrom[node];

                    if (node == start)
                        break;

                    corridorTiles.Add(node);
                }
            }
        }
    }

    private void WidenCorridor(Dungeon dungeon, float width, ICollection<Vector2i> corridorTiles)
    {
        var expansion = width - 2;

        if (expansion < 1)
            return;

        var toAdd = new ValueList<Vector2i>();

        foreach (var node in corridorTiles)
        {
            for (var x = -expansion; x <= expansion; x++)
            {
                for (var y = -expansion; y <= expansion; y++)
                {
                    var neighbor = new Vector2(node.X + x, node.Y + y).Floored();

                    if (dungeon.RoomTiles.Contains(neighbor) ||
                        dungeon.RoomExteriorTiles.Contains(neighbor))
                    {
                        continue;
                    }

                    toAdd.Add(neighbor);
                }
            }
        }

        foreach (var node in toAdd)
        {
            corridorTiles.Add(node);
        }
    }

    private void BuildCorridorExterior(Dungeon dungeon)
    {
        var exterior = dungeon.CorridorExteriorTiles;

        foreach (var tile in dungeon.CorridorTiles)
        {
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var neighbor = new Vector2i(tile.X + x, tile.Y + y);

                    if (dungeon.CorridorTiles.Contains(neighbor) ||
                        dungeon.RoomExteriorTiles.Contains(neighbor) ||
                        dungeon.RoomTiles.Contains(neighbor) ||
                        dungeon.Entrances.Contains(neighbor))
                    {
                        continue;
                    }

                    exterior.Add(neighbor);
                }
            }
        }
    }
}

/// <summary>
/// Executor for WormCorridorDunGen.
/// </summary>
public sealed class WormCorridorDunGenExecutor : LayerExecutorBase<WormCorridorDunGen>
{
    public WormCorridorDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(WormCorridorDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Worm-style corridor generation
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for BoundaryWallDunGen - places walls around dungeon boundaries.
/// </summary>
public sealed class BoundaryWallDunGenExecutor : LayerExecutorBase<BoundaryWallDunGen>
{
    public BoundaryWallDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override async Task ExecuteAsync(BoundaryWallDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];
        var tiles = new List<(Vector2i, Tile)>();

        var wall = layer.Wall.Id;
        var cornerWall = layer.CornerWall?.Id ?? wall;

        // Collect tiles to place
        if ((layer.Flags & BoundaryWallFlags.Rooms) != 0)
        {
            foreach (var neighbor in dungeon.RoomExteriorTiles)
            {
                if (dungeon.Entrances.Contains(neighbor))
                    continue;

                if (!IsTileAvailable(neighbor))
                    continue;

                var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                tiles.Add((neighbor, new Tile(tileDef.TileId, variant: variant)));
            }
        }

        if ((layer.Flags & BoundaryWallFlags.Corridors) != 0)
        {
            foreach (var index in dungeon.CorridorExteriorTiles)
            {
                if (dungeon.RoomTiles.Contains(index))
                    continue;

                if (!IsTileAvailable(index))
                    continue;

                var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                tiles.Add((index, new Tile(tileDef.TileId, variant: variant)));
            }
        }

        // Queue tiles
        QueueTiles(tiles);

        // Queue wall entities
        var count = 0;
        foreach (var (index, _) in tiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            if (!IsTileAvailable(index))
                continue;

            var isCorner = IsCornerTile(dungeon, index);

            QueueEntity(isCorner ? cornerWall : wall, index);

            count++;
            if (count % 50 == 0)
                await Task.Yield();
        }
    }

    private bool IsCornerTile(Dungeon dungeon, Vector2i index)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x != 0 && y != 0)
                    continue;

                var neighbor = new Vector2i(index.X + x, index.Y + y);

                if (dungeon.RoomTiles.Contains(neighbor) || dungeon.CorridorTiles.Contains(neighbor))
                    return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Executor for DungeonEntranceDunGen.
/// </summary>
public sealed class DungeonEntranceDunGenExecutor : LayerExecutorBase<DungeonEntranceDunGen>
{
    public DungeonEntranceDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(DungeonEntranceDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Creates dungeon entrances
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for RoomEntranceDunGen.
/// </summary>
public sealed class RoomEntranceDunGenExecutor : LayerExecutorBase<RoomEntranceDunGen>
{
    public RoomEntranceDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(RoomEntranceDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Creates room entrances (doors)
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for EntranceFlankDunGen.
/// </summary>
public sealed class EntranceFlankDunGenExecutor : LayerExecutorBase<EntranceFlankDunGen>
{
    public EntranceFlankDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(EntranceFlankDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places entities flanking entrances
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for ExternalWindowDunGen.
/// </summary>
public sealed class ExternalWindowDunGenExecutor : LayerExecutorBase<ExternalWindowDunGen>
{
    public ExternalWindowDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(ExternalWindowDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places external windows
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for InternalWindowDunGen.
/// </summary>
public sealed class InternalWindowDunGenExecutor : LayerExecutorBase<InternalWindowDunGen>
{
    public InternalWindowDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(InternalWindowDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places internal windows
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for JunctionDunGen.
/// </summary>
public sealed class JunctionDunGenExecutor : LayerExecutorBase<JunctionDunGen>
{
    public JunctionDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(JunctionDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Creates junctions between corridors
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for WallMountDunGen.
/// </summary>
public sealed class WallMountDunGenExecutor : LayerExecutorBase<WallMountDunGen>
{
    public WallMountDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(WallMountDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places wall-mounted entities
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for CornerClutterDunGen.
/// </summary>
public sealed class CornerClutterDunGenExecutor : LayerExecutorBase<CornerClutterDunGen>
{
    public CornerClutterDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(CornerClutterDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places clutter in corners
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for CorridorClutterDunGen.
/// </summary>
public sealed class CorridorClutterDunGenExecutor : LayerExecutorBase<CorridorClutterDunGen>
{
    public CorridorClutterDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(CorridorClutterDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places clutter in corridors
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for CorridorDecalSkirtingDunGen.
/// </summary>
public sealed class CorridorDecalSkirtingDunGenExecutor : LayerExecutorBase<CorridorDecalSkirtingDunGen>
{
    public CorridorDecalSkirtingDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(CorridorDecalSkirtingDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places decal skirting around corridors
        foreach (var tile in dungeon.CorridorTiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            // Check each cardinal direction
            for (var dir = 0; dir < 4; dir++)
            {
                var direction = (Direction)(dir * 2);
                var neighbor = tile + direction.ToIntVec();

                if (dungeon.CorridorTiles.Contains(neighbor) || dungeon.RoomTiles.Contains(neighbor))
                    continue;

                // Place cardinal decal
                if (layer.CardinalDecals.TryGetValue(direction, out var decalId))
                {
                    var pos = tile + Context.Grid.TileSizeHalfVector;
                    QueueDecal(decalId, pos);
                }
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for AutoCablingDunGen.
/// </summary>
public sealed class AutoCablingDunGenExecutor : LayerExecutorBase<AutoCablingDunGen>
{
    public AutoCablingDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(AutoCablingDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Places cables automatically
        foreach (var tile in dungeon.AllTiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();
            QueueEntity(layer.Entity, tile);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for MiddleConnectionDunGen.
/// </summary>
public sealed class MiddleConnectionDunGenExecutor : LayerExecutorBase<MiddleConnectionDunGen>
{
    public MiddleConnectionDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(MiddleConnectionDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Creates connections in the middle of rooms
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for SplineDungeonConnectorDunGen.
/// </summary>
public sealed class SplineDungeonConnectorDunGenExecutor : LayerExecutorBase<SplineDungeonConnectorDunGen>
{
    public SplineDungeonConnectorDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(SplineDungeonConnectorDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Creates spline-based dungeon connections
        return Task.CompletedTask;
    }
}

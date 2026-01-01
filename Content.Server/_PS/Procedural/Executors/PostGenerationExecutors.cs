using System.Buffers;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Maths;

#pragma warning disable CS1591

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Optimized executor for CorridorDunGen - efficient pathfinding with pooled buffers.
/// </summary>
public sealed class CorridorDunGenExecutor : LayerExecutorBase<CorridorDunGen>
{
    // Pre-allocated cardinal direction offsets
    private static readonly Vector2i[] Cardinals =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
    };

    public CorridorDunGenExecutor(DungeonGenerationContext context) : base(context)
    {
    }

    protected override async Task ExecuteAsync(CorridorDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Collect all entrances
        var entranceCount = 0;
        foreach (var room in dungeon.Rooms)
        {
            entranceCount += room.Entrances.Count;
        }

        if (entranceCount < 2)
            return;

        // Use array pool for entrance list
        var entrances = ArrayPool<Vector2i>.Shared.Rent(entranceCount);
        var entranceIdx = 0;

        try
        {
            foreach (var room in dungeon.Rooms)
            {
                foreach (var entrance in room.Entrances)
                {
                    entrances[entranceIdx++] = entrance;
                }
            }

            // Build MST with optimized algorithm
            var edges = BuildMinimumSpanningTree(entrances, entranceIdx, random);

            var expansion = (int)(layer.Width - 2);

            // Build deterred tiles set
            var deterredTiles = Context.RentHashSet();
            if (expansion >= 1)
            {
                BuildDeterredTiles(dungeon, expansion, entrances, entranceIdx, deterredTiles);
            }

            // Remove tiles near entrances from deterred set
            foreach (var room in dungeon.Rooms)
            {
                foreach (var entrance in room.Entrances)
                {
                    var normal = (entrance + Context.Grid.TileSizeHalfVector - room.Center)
                        .ToWorldAngle().GetCardinalDir().ToIntVec();
                    deterredTiles.Remove(entrance + normal);
                }
            }

            // Build excluded tiles
            var excludedTiles = Context.RentHashSet();
            excludedTiles.UnionWith(dungeon.RoomExteriorTiles);
            excludedTiles.UnionWith(dungeon.RoomTiles);

            // Find corridor paths
            var corridorTiles = Context.RentHashSet();
            await FindCorridorPaths(edges, layer.PathLimit, excludedTiles, deterredTiles, corridorTiles);

            // Widen corridors
            WidenCorridorOptimized(dungeon, layer.Width, corridorTiles);

            // Queue tiles
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
            BuildCorridorExteriorOptimized(dungeon);

            Context.ReturnHashSet(deterredTiles);
            Context.ReturnHashSet(excludedTiles);
            Context.ReturnHashSet(corridorTiles);
        }
        finally
        {
            ArrayPool<Vector2i>.Shared.Return(entrances);
        }
    }

    /// <summary>
    /// Prim's algorithm for MST with O(n²) complexity.
    /// </summary>
    private List<(Vector2i Start, Vector2i End)> BuildMinimumSpanningTree(Vector2i[] tiles, int count, Random random)
    {
        if (count < 2)
            return new List<(Vector2i, Vector2i)>();

        var edges = new List<(Vector2i Start, Vector2i End)>(count - 1);

        // Use arrays instead of dictionaries for small counts
        var inTree = ArrayPool<bool>.Shared.Rent(count);
        var distances = ArrayPool<float>.Shared.Rent(count * count);

        try
        {
            Array.Clear(inTree, 0, count);

            // Pre-compute all pairwise distances
            for (var i = 0; i < count; i++)
            {
                for (var j = i + 1; j < count; j++)
                {
                    var dist = (tiles[j] - tiles[i]).Length;
                    distances[i * count + j] = dist;
                    distances[j * count + i] = dist;
                }
            }

            // Start from random node
            var startIdx = random.Next(count);
            inTree[startIdx] = true;
            var treeSize = 1;

            while (treeSize < count)
            {
                var bestDist = float.MaxValue;
                var bestFrom = -1;
                var bestTo = -1;

                // Find cheapest edge from tree to non-tree
                for (var i = 0; i < count; i++)
                {
                    if (!inTree[i])
                        continue;

                    for (var j = 0; j < count; j++)
                    {
                        if (inTree[j])
                            continue;

                        var dist = distances[i * count + j];
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestFrom = i;
                            bestTo = j;
                        }
                    }
                }

                if (bestTo >= 0)
                {
                    edges.Add((tiles[bestFrom], tiles[bestTo]));
                    inTree[bestTo] = true;
                    treeSize++;
                }
            }

            return edges;
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(inTree);
            ArrayPool<float>.Shared.Return(distances);
        }
    }

    private void BuildDeterredTiles(Dungeon dungeon, int expansion, Vector2i[] entrances, int entranceCount, HashSet<Vector2i> deterredTiles)
    {
        foreach (var tile in dungeon.RoomExteriorTiles)
        {
            for (var x = -expansion; x <= expansion; x++)
            {
                for (var y = -expansion; y <= expansion; y++)
                {
                    var neighbor = new Vector2i(tile.X + x, tile.Y + y);

                    if (dungeon.RoomTiles.Contains(neighbor) ||
                        dungeon.RoomExteriorTiles.Contains(neighbor))
                        continue;

                    // Check if it's an entrance
                    var isEntrance = false;
                    for (var i = 0; i < entranceCount; i++)
                    {
                        if (entrances[i] == neighbor)
                        {
                            isEntrance = true;
                            break;
                        }
                    }

                    if (!isEntrance)
                        deterredTiles.Add(neighbor);
                }
            }
        }
    }

    private async Task FindCorridorPaths(
        List<(Vector2i Start, Vector2i End)> edges,
        int pathLimit,
        HashSet<Vector2i> excludedTiles,
        HashSet<Vector2i> deterredTiles,
        HashSet<Vector2i> corridorTiles)
    {
        // Pre-allocate pathfinding data structures
        var frontier = new PriorityQueue<Vector2i, float>(256);
        var cameFrom = new Dictionary<Vector2i, Vector2i>(pathLimit);
        var directions = new Dictionary<Vector2i, Direction>(pathLimit);
        var costSoFar = new Dictionary<Vector2i, float>(pathLimit);

        foreach (var (start, end) in edges)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            frontier.Clear();
            cameFrom.Clear();
            directions.Clear();
            costSoFar.Clear();

            directions[start] = Direction.Invalid;
            frontier.Enqueue(start, 0f);
            costSoFar[start] = 0f;

            var found = false;
            var iterations = 0;

            while (frontier.Count > 0 && iterations < pathLimit)
            {
                iterations++;
                var node = frontier.Dequeue();

                if (node == end)
                {
                    found = true;
                    break;
                }

                var lastDirection = directions[node];
                var baseCost = costSoFar[node];

                // Unrolled cardinal neighbor loop
                for (var d = 0; d < 4; d++)
                {
                    var offset = Cardinals[d];
                    var neighbor = new Vector2i(node.X + offset.X, node.Y + offset.Y);

                    if (neighbor != end && excludedTiles.Contains(neighbor))
                        continue;

                    var tileCost = 1f; // Manhattan distance to neighbor is always 1

                    if (corridorTiles.Contains(neighbor))
                        tileCost *= 0.1f;

                    if (deterredTiles.Contains(neighbor))
                        tileCost *= 2f;

                    var direction = offset.GetCardinalDir();

                    if (direction != lastDirection)
                        tileCost *= 3f;

                    var gScore = baseCost + tileCost;

                    if (costSoFar.TryGetValue(neighbor, out var existingCost) && gScore >= existingCost)
                        continue;

                    cameFrom[neighbor] = node;
                    costSoFar[neighbor] = gScore;
                    directions[neighbor] = direction;

                    var hScore = ManhattanDistance(end, neighbor) * 0.999f;
                    frontier.Enqueue(neighbor, gScore + hScore);
                }
            }

            // Reconstruct path
            if (found)
            {
                var current = end;
                while (cameFrom.TryGetValue(current, out var prev))
                {
                    if (prev == start)
                        break;
                    corridorTiles.Add(prev);
                    current = prev;
                }
            }

            await Task.Yield();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ManhattanDistance(Vector2i a, Vector2i b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private void WidenCorridorOptimized(Dungeon dungeon, float width, HashSet<Vector2i> corridorTiles)
    {
        var expansion = (int)(width - 2);
        if (expansion < 1)
            return;

        // Collect tiles to add (can't modify during iteration)
        var toAdd = new ValueList<Vector2i>(corridorTiles.Count * (2 * expansion + 1) * (2 * expansion + 1));

        foreach (var node in corridorTiles)
        {
            for (var x = -expansion; x <= expansion; x++)
            {
                for (var y = -expansion; y <= expansion; y++)
                {
                    var neighbor = new Vector2i(node.X + x, node.Y + y);

                    if (dungeon.RoomTiles.Contains(neighbor) ||
                        dungeon.RoomExteriorTiles.Contains(neighbor))
                        continue;

                    toAdd.Add(neighbor);
                }
            }
        }

        foreach (var node in toAdd)
        {
            corridorTiles.Add(node);
        }
    }

    private void BuildCorridorExteriorOptimized(Dungeon dungeon)
    {
        var exterior = dungeon.CorridorExteriorTiles;

        foreach (var tile in dungeon.CorridorTiles)
        {
            // Unrolled neighbor check
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var neighbor = new Vector2i(tile.X + x, tile.Y + y);

                    if (dungeon.CorridorTiles.Contains(neighbor) ||
                        dungeon.RoomExteriorTiles.Contains(neighbor) ||
                        dungeon.RoomTiles.Contains(neighbor) ||
                        dungeon.Entrances.Contains(neighbor))
                        continue;

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
        return Task.CompletedTask;
    }
}

/// <summary>
/// Optimized executor for BoundaryWallDunGen - parallel corner detection.
/// </summary>
public sealed class BoundaryWallDunGenExecutor : LayerExecutorBase<BoundaryWallDunGen>
{
    // Pre-computed cardinal offsets for corner detection
    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
    };

    public BoundaryWallDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override async Task ExecuteAsync(BoundaryWallDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];
        var wall = layer.Wall.Id;
        var cornerWall = layer.CornerWall?.Id ?? wall;

        // Estimate capacity
        var estimatedCount = dungeon.RoomExteriorTiles.Count + dungeon.CorridorExteriorTiles.Count;

        // Use array pool for tile collection
        var tileBuffer = ArrayPool<(Vector2i Pos, bool IsCorner)>.Shared.Rent(estimatedCount);
        var tileCount = 0;

        try
        {
            // Collect room exterior tiles
            if ((layer.Flags & BoundaryWallFlags.Rooms) != 0)
            {
                foreach (var tile in dungeon.RoomExteriorTiles)
                {
                    if (dungeon.Entrances.Contains(tile))
                        continue;

                    if (!IsTileAvailable(tile))
                        continue;

                    var isCorner = IsCornerTile(dungeon, tile);

                    if (tileCount < tileBuffer.Length)
                        tileBuffer[tileCount++] = (tile, isCorner);
                }
            }

            // Collect corridor exterior tiles
            if ((layer.Flags & BoundaryWallFlags.Corridors) != 0)
            {
                foreach (var tile in dungeon.CorridorExteriorTiles)
                {
                    if (dungeon.RoomTiles.Contains(tile))
                        continue;

                    if (!IsTileAvailable(tile))
                        continue;

                    var isCorner = IsCornerTile(dungeon, tile);

                    if (tileCount < tileBuffer.Length)
                        tileBuffer[tileCount++] = (tile, isCorner);
                }
            }

            // Batch queue tiles and entities
            for (var i = 0; i < tileCount; i++)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                var (pos, isCorner) = tileBuffer[i];
                var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;

                QueueTile(pos, new Tile(tileDef.TileId, variant: variant));
                QueueEntity(isCorner ? cornerWall : wall, pos);

                // Yield periodically
                if ((i & 63) == 0)
                    await Task.Yield();
            }
        }
        finally
        {
            ArrayPool<(Vector2i, bool)>.Shared.Return(tileBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCornerTile(Dungeon dungeon, Vector2i pos)
    {
        // A tile is a corner if no cardinal neighbor is in dungeon tiles
        for (var i = 0; i < 4; i++)
        {
            var neighbor = pos + CardinalOffsets[i];
            if (dungeon.RoomTiles.Contains(neighbor) || dungeon.CorridorTiles.Contains(neighbor))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Executor for DungeonEntranceDunGen - creates dungeon external entrances.
/// </summary>
public sealed class DungeonEntranceDunGenExecutor : LayerExecutorBase<DungeonEntranceDunGen>
{
    public DungeonEntranceDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(DungeonEntranceDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var rooms = new List<DungeonRoom>(dungeon.Rooms);
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];

        for (var i = 0; i < layer.Count && rooms.Count > 0; i++)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            var roomIndex = random.Next(rooms.Count);
            var room = rooms[roomIndex];

            // Get shuffled exterior tiles
            var roomTiles = room.Exterior.ToList();
            ShuffleList(roomTiles, random);

            foreach (var tile in roomTiles)
            {
                var isValid = false;

                // Check if one side is dungeon and the other side is empty
                for (var j = 0; j < 4; j++)
                {
                    var dir = (Direction)(j * 2);
                    var oppositeDir = dir.GetOpposite();
                    var dirVec = tile + dir.ToIntVec();
                    var oppositeDirVec = tile + oppositeDir.ToIntVec();

                    if (!dungeon.RoomTiles.Contains(dirVec))
                        continue;

                    if (dungeon.RoomTiles.Contains(oppositeDirVec) ||
                        dungeon.RoomExteriorTiles.Contains(oppositeDirVec) ||
                        dungeon.CorridorExteriorTiles.Contains(oppositeDirVec) ||
                        dungeon.CorridorTiles.Contains(oppositeDirVec))
                        continue;

                    if (!IsTileAvailable(tile))
                        continue;

                    // Valid entrance found
                    isValid = true;

                    var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                    QueueTile(tile, new Tile(tileDef.TileId, variant: variant));
                    QueueEntityTable(layer.Contents, tile);
                    break;
                }

                if (isValid)
                    break;
            }
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ShuffleList<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// Executor for RoomEntranceDunGen - places tiles and entities at room entrances.
/// </summary>
public sealed class RoomEntranceDunGenExecutor : LayerExecutorBase<RoomEntranceDunGen>
{
    public RoomEntranceDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(RoomEntranceDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];

        foreach (var room in dungeon.Rooms)
        {
            foreach (var entrance in room.Entrances)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                if (!IsTileAvailable(entrance))
                    continue;

                var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                QueueTile(entrance, new Tile(tileDef.TileId, variant: variant));
                QueueEntityTable(layer.Contents, entrance);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for EntranceFlankDunGen - places flanking tiles/entities around entrances.
/// </summary>
public sealed class EntranceFlankDunGenExecutor : LayerExecutorBase<EntranceFlankDunGen>
{
    public EntranceFlankDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(EntranceFlankDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];
        var spawnPositions = new ValueList<Vector2i>(dungeon.Rooms.Count * 2);

        foreach (var room in dungeon.Rooms)
        {
            foreach (var entrance in room.Entrances)
            {
                // Check all 8 directions around the entrance
                for (var i = 0; i < 8; i++)
                {
                    var dir = (Direction)i;
                    var neighbor = entrance + dir.ToIntVec();

                    if (!dungeon.RoomExteriorTiles.Contains(neighbor))
                        continue;

                    if (!IsTileAvailable(neighbor))
                        continue;

                    var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                    QueueTile(neighbor, new Tile(tileDef.TileId, variant: variant));
                    spawnPositions.Add(neighbor);
                }
            }
        }

        // Queue entity spawns
        foreach (var pos in spawnPositions)
        {
            Context.Cancellation.ThrowIfCancellationRequested();
            QueueEntityTable(layer.Contents, pos);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for ExternalWindowDunGen - places windows on exterior walls.
/// </summary>
public sealed class ExternalWindowDunGenExecutor : LayerExecutorBase<ExternalWindowDunGen>
{
    public ExternalWindowDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(ExternalWindowDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var chance = 0.25 / 3.0;
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];

        // Combine all exterior tiles
        var allExterior = Context.RentHashSet();
        allExterior.UnionWith(dungeon.CorridorExteriorTiles);
        allExterior.UnionWith(dungeon.RoomExteriorTiles);

        var validTiles = allExterior.ToList();
        ShuffleListOptimized(validTiles, random);

        var count = (int)Math.Floor(validTiles.Count * chance);
        var index = 0;
        var takenTiles = Context.RentHashSet();
        var windowTiles = new ValueList<Vector2i>(count);

        try
        {
            foreach (var tile in validTiles)
            {
                if (index >= count)
                    break;

                if (takenTiles.Contains(tile))
                    continue;

                // Check we're not on a corner - need 3 tiles in a row
                for (var i = 0; i < 2; i++)
                {
                    var dir = (Direction)(i * 2);
                    var dirVec = dir.ToIntVec();
                    var isValid = true;

                    // Check 1 beyond either side to ensure it's not a corner
                    for (var j = -1; j < 4; j++)
                    {
                        var neighbor = tile + dirVec * j;

                        if (!allExterior.Contains(neighbor) || takenTiles.Contains(neighbor))
                        {
                            isValid = false;
                            break;
                        }
                    }

                    if (!isValid)
                        continue;

                    // Place 3 window tiles in a row
                    for (var j = 0; j < 3; j++)
                    {
                        var neighbor = tile + dirVec * j;

                        if (!IsTileAvailable(neighbor))
                            continue;

                        var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                        QueueTile(neighbor, new Tile(tileDef.TileId, variant: variant));
                        windowTiles.Add(neighbor);
                        takenTiles.Add(neighbor);
                        index++;
                    }

                    break;
                }
            }

            // Spawn window entities
            foreach (var tile in windowTiles)
            {
                Context.Cancellation.ThrowIfCancellationRequested();
                QueueEntityTable(layer.Contents, tile);
            }
        }
        finally
        {
            Context.ReturnHashSet(allExterior);
            Context.ReturnHashSet(takenTiles);
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ShuffleListOptimized<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// Executor for InternalWindowDunGen - places windows between adjacent rooms.
/// </summary>
public sealed class InternalWindowDunGenExecutor : LayerExecutorBase<InternalWindowDunGen>
{
    public InternalWindowDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(InternalWindowDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Similar to external but for room-to-room boundaries
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];
        var windowTiles = new ValueList<Vector2i>(32);

        foreach (var room in dungeon.Rooms)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            foreach (var tile in room.Exterior)
            {
                // Check if this exterior tile borders another room
                var bordersAnotherRoom = false;
                for (var i = 0; i < 4; i++)
                {
                    var dir = (Direction)(i * 2);
                    var neighbor = tile + dir.ToIntVec();

                    if (room.Tiles.Contains(neighbor))
                        continue;

                    if (dungeon.RoomTiles.Contains(neighbor))
                    {
                        bordersAnotherRoom = true;
                        break;
                    }
                }

                if (!bordersAnotherRoom || !IsTileAvailable(tile))
                    continue;

                var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                QueueTile(tile, new Tile(tileDef.TileId, variant: variant));
                windowTiles.Add(tile);
            }
        }

        foreach (var tile in windowTiles)
        {
            QueueEntityTable(layer.Contents, tile);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for JunctionDunGen - creates junctions in corridors where paths widen.
/// </summary>
public sealed class JunctionDunGenExecutor : LayerExecutorBase<JunctionDunGen>
{
    public JunctionDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(JunctionDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];
        var width = (int)Math.Ceiling(layer.Width / 2f);
        var exteriorWidth = (int)Math.Floor(layer.Width / 2f);

        foreach (var tile in dungeon.CorridorTiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            // Check each cardinal direction for junction potential
            for (var i = 0; i < 2; i++)
            {
                var isValid = true;
                var neighborDir = (Direction)(i * 2);
                var neighborVec = neighborDir.ToIntVec();

                // Check tiles along the width
                for (var j = -width; j <= width; j++)
                {
                    if (j == 0)
                        continue;

                    var neighbor = tile + neighborVec * j;

                    // End tiles should be walls
                    if (j == -width || j == width)
                    {
                        if (!dungeon.RoomExteriorTiles.Contains(neighbor) &&
                            !dungeon.CorridorExteriorTiles.Contains(neighbor))
                        {
                            isValid = false;
                            break;
                        }
                        continue;
                    }

                    // Interior tiles should be free
                    if (!dungeon.CorridorTiles.Contains(neighbor) && !dungeon.RoomTiles.Contains(neighbor))
                    {
                        isValid = false;
                        break;
                    }
                }

                if (!isValid)
                    continue;

                // Check corners to see if the corridor opens up
                foreach (var j in new[] { -exteriorWidth, exteriorWidth })
                {
                    var freeCount = 0;

                    for (var k = 0; k < 4; k++)
                    {
                        var cornerDir = (Direction)(k * 2 + 1);
                        var cornerVec = cornerDir.ToIntVec();
                        var cornerNeighbor = tile + neighborVec * j + cornerVec;

                        if (dungeon.CorridorTiles.Contains(cornerNeighbor) || dungeon.RoomTiles.Contains(cornerNeighbor))
                            freeCount++;
                    }

                    if (freeCount < layer.Width)
                        continue;

                    // Valid junction found - place tiles
                    for (var x = -width + 1; x < width; x++)
                    {
                        var junctionTile = tile + neighborDir.ToIntVec() * x;

                        if (!IsTileAvailable(junctionTile))
                            continue;

                        var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                        QueueTile(junctionTile, new Tile(tileDef.TileId, variant: variant));
                        QueueEntityTable(layer.Contents, junctionTile);
                    }

                    break;
                }
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for WallMountDunGen - places wall-mounted items on exterior tiles.
/// </summary>
public sealed class WallMountDunGenExecutor : LayerExecutorBase<WallMountDunGen>
{
    public WallMountDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(WallMountDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var tileDef = (ContentTileDefinition)Context.TileDef[layer.Tile];
        var checkedTiles = Context.RentHashSet();

        // Combine all exterior tiles
        var allExterior = Context.RentHashSet();
        allExterior.UnionWith(dungeon.CorridorExteriorTiles);
        allExterior.UnionWith(dungeon.RoomExteriorTiles);

        try
        {
            foreach (var neighbor in allExterior)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                // Skip room tiles and already checked tiles
                if (dungeon.RoomTiles.Contains(neighbor) || checkedTiles.Contains(neighbor))
                    continue;

                if (!random.Prob(layer.Prob) || !checkedTiles.Add(neighbor))
                    continue;

                if (!IsTileAvailable(neighbor))
                    continue;

                var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
                QueueTile(neighbor, new Tile(tileDef.TileId, variant: variant));
                QueueEntityTable(layer.Contents, neighbor);
            }
        }
        finally
        {
            Context.ReturnHashSet(checkedTiles);
            Context.ReturnHashSet(allExterior);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for CornerClutterDunGen - places clutter in corridor corners.
/// </summary>
public sealed class CornerClutterDunGenExecutor : LayerExecutorBase<CornerClutterDunGen>
{
    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0), new(0, 1), new(-1, 0), new(0, -1)
    };

    public CornerClutterDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(CornerClutterDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        foreach (var tile in dungeon.CorridorTiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            if (!IsTileAvailable(tile))
                continue;

            // Check if at least 2 adjacent tiles are walls (corner detection)
            for (var i = 0; i < 4; i++)
            {
                var blocked = IsWallTile(dungeon, tile + CardinalOffsets[i]);
                if (!blocked)
                    continue;

                var nextDir = (i + 1) % 4;
                blocked = IsWallTile(dungeon, tile + CardinalOffsets[nextDir]);
                if (!blocked)
                    continue;

                // This is a corner - spawn clutter with probability
                if (random.Prob(layer.Chance))
                {
                    QueueEntityTable(layer.Contents, tile);
                }

                break;
            }
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWallTile(Dungeon dungeon, Vector2i pos)
    {
        return dungeon.RoomExteriorTiles.Contains(pos) || dungeon.CorridorExteriorTiles.Contains(pos);
    }
}

/// <summary>
/// Executor for CorridorClutterDunGen - places random clutter in corridors.
/// </summary>
public sealed class CorridorClutterDunGenExecutor : LayerExecutorBase<CorridorClutterDunGen>
{
    public CorridorClutterDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(CorridorClutterDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        if (dungeon.CorridorTiles.Count == 0)
            return Task.CompletedTask;

        var count = (int)Math.Ceiling(dungeon.CorridorTiles.Count * layer.Chance);
        var corridorList = dungeon.CorridorTiles.ToList();
        var attempts = 0;
        var maxAttempts = count * 3; // Prevent infinite loops

        while (count > 0 && attempts < maxAttempts)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            attempts++;
            var tile = corridorList[random.Next(corridorList.Count)];

            if (!IsTileAvailable(tile))
                continue;

            count--;
            QueueEntityTable(layer.Contents, tile);
        }

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
        foreach (var tile in dungeon.CorridorTiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            for (var dir = 0; dir < 4; dir++)
            {
                var direction = (Direction)(dir * 2);
                var neighbor = tile + direction.ToIntVec();

                if (dungeon.CorridorTiles.Contains(neighbor) || dungeon.RoomTiles.Contains(neighbor))
                    continue;

                if (layer.CardinalDecals.TryGetValue(direction.AsFlag(), out var decalId))
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
        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension methods for Random probability checks.
/// </summary>
internal static class RandomProbExtensions
{
    /// <summary>
    /// Returns true with the given probability (0.0 to 1.0).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Prob(this Random random, double chance)
    {
        return random.NextDouble() < chance;
    }
}

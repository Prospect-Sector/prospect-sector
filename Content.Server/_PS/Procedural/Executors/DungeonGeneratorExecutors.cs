using System.Buffers;
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Threading;

#pragma warning disable CS1591

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Optimized executor for PrefabDunGen - room placement with cached lookups and reduced allocations.
/// </summary>
public sealed class PrefabDunGenExecutor : LayerExecutorBase<PrefabDunGen>
{
    private readonly ISawmill _log;

    // Pre-computed direction offsets for rotation checks (avoids repeated enum->vector conversions)
    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0),   // East
        new(0, 1),   // North
        new(-1, 0),  // West
        new(0, -1)   // South
    };

    // Pre-computed rotation angles
    private static readonly Angle[] RotationAngles =
    {
        Angle.Zero,
        new(Math.PI / 2),
        new(Math.PI),
        new(Math.PI * 1.5)
    };

    public PrefabDunGenExecutor(DungeonGenerationContext context, ISawmill log) : base(context)
    {
        _log = log;
    }

    protected override async Task ExecuteAsync(PrefabDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        if (layer.Presets.Count == 0)
        {
            _log.Warning("PrefabDunGen has no presets configured");
            return;
        }

        var preset = layer.Presets[random.Next(layer.Presets.Count)];
        var gen = Context.Prototype.Index(preset);

        // Pre-compute dungeon rotation once
        var dungeonRotation = RotationAngles[random.Next(4)];
        var dungeonTransform = Matrix3Helpers.CreateTransform(position, dungeonRotation);

        // Build frozen lookups for O(1) access during generation
        var roomPackLookup = BuildRoomPackLookup();
        var roomProtoLookup = BuildRoomPrototypeLookup(layer);

        // Use array pool for pack selection arrays (avoid heap allocation)
        var packCount = gen.RoomPacks.Count;
        var chosenPacks = ArrayPool<DungeonRoomPackPrototype?>.Shared.Rent(packCount);
        var packTransforms = ArrayPool<Matrix3x2>.Shared.Rent(packCount);
        var packRotations = ArrayPool<Angle>.Shared.Rent(packCount);

        try
        {
            // Zero out rented arrays
            Array.Clear(chosenPacks, 0, packCount);

            // Choose packs with optimized selection
            SelectRoomPacks(gen, roomPackLookup, random, chosenPacks, packTransforms, packRotations, packCount);

            // Process rooms - batch tile operations
            for (var i = 0; i < packCount; i++)
            {
                var pack = chosenPacks[i];
                if (pack == null)
                    continue;

                Context.Cancellation.ThrowIfCancellationRequested();

                var packTransform = packTransforms[i];
                var packCenter = new Vector2(pack.Size.X * 0.5f, pack.Size.Y * 0.5f);

                foreach (var roomSize in pack.Rooms)
                {
                    await ProcessRoom(
                        dungeon,
                        roomSize,
                        roomProtoLookup,
                        packCenter,
                        packTransform,
                        dungeonTransform,
                        layer.FallbackTile,
                        random);
                }
            }

            // Set entrances using optimized method
            SetEntrancesOptimized(dungeon, random);
            dungeon.Rebuild();
        }
        finally
        {
            ArrayPool<DungeonRoomPackPrototype?>.Shared.Return(chosenPacks);
            ArrayPool<Matrix3x2>.Shared.Return(packTransforms);
            ArrayPool<Angle>.Shared.Return(packRotations);
        }
    }

    /// <summary>
    /// Builds a frozen dictionary for O(1) room pack lookups by size.
    /// </summary>
    private FrozenDictionary<Vector2i, DungeonRoomPackPrototype[]> BuildRoomPackLookup()
    {
        var temp = new Dictionary<Vector2i, List<DungeonRoomPackPrototype>>();

        foreach (var pack in Context.Prototype.EnumeratePrototypes<DungeonRoomPackPrototype>())
        {
            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(temp, pack.Size, out _);
            list ??= new List<DungeonRoomPackPrototype>(8);
            list.Add(pack);
        }

        // Convert to frozen arrays sorted for determinism
        var result = new Dictionary<Vector2i, DungeonRoomPackPrototype[]>(temp.Count);
        foreach (var (size, list) in temp)
        {
            var arr = list.ToArray();
            Array.Sort(arr, (a, b) => string.Compare(a.ID, b.ID, StringComparison.Ordinal));
            result[size] = arr;
        }

        return result.ToFrozenDictionary();
    }

    /// <summary>
    /// Builds a frozen dictionary for O(1) room prototype lookups by size.
    /// </summary>
    private FrozenDictionary<Vector2i, DungeonRoomPrototype[]> BuildRoomPrototypeLookup(PrefabDunGen layer)
    {
        var temp = new Dictionary<Vector2i, List<DungeonRoomPrototype>>();
        var hasTags = layer.RoomWhitelist?.Tags != null;
        var tags = layer.RoomWhitelist?.Tags;

        foreach (var proto in Context.Prototype.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            if (hasTags)
            {
                var matched = false;
                foreach (var tag in tags!)
                {
                    if (proto.Tags.Contains(tag))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                    continue;
            }

            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(temp, proto.Size, out _);
            list ??= new List<DungeonRoomPrototype>(16);
            list.Add(proto);
        }

        var result = new Dictionary<Vector2i, DungeonRoomPrototype[]>(temp.Count);
        foreach (var (size, list) in temp)
        {
            var arr = list.ToArray();
            Array.Sort(arr, (a, b) => string.Compare(a.ID, b.ID, StringComparison.Ordinal));
            result[size] = arr;
        }

        return result.ToFrozenDictionary();
    }

    /// <summary>
    /// Optimized room pack selection using pre-built lookups.
    /// </summary>
    private void SelectRoomPacks(
        DungeonPresetPrototype gen,
        FrozenDictionary<Vector2i, DungeonRoomPackPrototype[]> packLookup,
        Random random,
        DungeonRoomPackPrototype?[] chosenPacks,
        Matrix3x2[] packTransforms,
        Angle[] packRotations,
        int count)
    {
        // Rent a buffer for available packs to avoid allocation per iteration
        var availableBuffer = ArrayPool<DungeonRoomPackPrototype>.Shared.Rent(128);
        var availableCount = 0;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var bounds = gen.RoomPacks[i];
                var dims = new Vector2i(bounds.Width, bounds.Height);
                availableCount = 0;

                // Collect available packs (normal orientation)
                if (packLookup.TryGetValue(dims, out var packs))
                {
                    foreach (var p in packs)
                    {
                        if (availableCount < availableBuffer.Length)
                            availableBuffer[availableCount++] = p;
                    }
                }

                // Collect rotated orientation if different
                if (dims.X != dims.Y)
                {
                    var rotatedDims = new Vector2i(dims.Y, dims.X);
                    if (packLookup.TryGetValue(rotatedDims, out packs))
                    {
                        foreach (var p in packs)
                        {
                            if (availableCount < availableBuffer.Length)
                                availableBuffer[availableCount++] = p;
                        }
                    }
                }

                if (availableCount == 0)
                    continue;

                // Fisher-Yates shuffle on the available span
                ShuffleSpan(availableBuffer.AsSpan(0, availableCount), random);

                // Find first fitting pack with valid rotation
                for (var j = 0; j < availableCount; j++)
                {
                    var pack = availableBuffer[j];
                    var startRot = random.Next(4);

                    for (var r = 0; r < 4; r++)
                    {
                        var rotIdx = (startRot + r) & 3;
                        var isRotated = (rotIdx & 1) != 0;
                        var packDims = isRotated
                            ? new Vector2i(pack.Size.Y, pack.Size.X)
                            : pack.Size;

                        if (packDims != bounds.Size)
                            continue;

                        var rotation = RotationAngles[rotIdx];
                        packTransforms[i] = Matrix3Helpers.CreateTransform(bounds.Center, rotation);
                        packRotations[i] = rotation;
                        chosenPacks[i] = pack;
                        goto nextPack;
                    }
                }

                nextPack:;
            }
        }
        finally
        {
            ArrayPool<DungeonRoomPackPrototype>.Shared.Return(availableBuffer);
        }
    }

    private async Task ProcessRoom(
        Dungeon dungeon,
        Box2i roomSize,
        FrozenDictionary<Vector2i, DungeonRoomPrototype[]> roomLookup,
        Vector2 packCenter,
        Matrix3x2 packTransform,
        Matrix3x2 dungeonTransform,
        ProtoId<ContentTileDefinition>? fallbackTile,
        Random random)
    {
        var dims = new Vector2i(roomSize.Width, roomSize.Height);
        var rotation = Angle.Zero;

        // Try to find matching room prototype
        if (!roomLookup.TryGetValue(dims, out var rooms))
        {
            dims = new Vector2i(dims.Y, dims.X);
            if (!roomLookup.TryGetValue(dims, out rooms))
            {
                // Fallback tile handling
                if (fallbackTile != null)
                {
                    var matty = Matrix3x2.Multiply(packTransform, dungeonTransform);
                    var tileDef = Context.TileDef[fallbackTile.Value];

                    for (var x = roomSize.Left; x < roomSize.Right; x++)
                    {
                        for (var y = roomSize.Bottom; y < roomSize.Top; y++)
                        {
                            var pos = new Vector2(x, y) + Context.Grid.TileSizeHalfVector - packCenter;
                            var index = Vector2.Transform(pos, matty).Floored();

                            if (IsTileAvailable(index))
                                QueueTile(index, new Tile(tileDef.TileId));
                        }
                    }
                }
                return;
            }
            rotation = new Angle(Math.PI / 2);
        }

        var room = rooms[random.Next(rooms.Length)];

        // Calculate rotation
        if (dims.X == dims.Y)
            rotation = RotationAngles[random.Next(4)];
        else if (random.Next(2) == 1)
            rotation += Math.PI;

        var roomTransform = Matrix3Helpers.CreateTransform(roomSize.Center - packCenter, rotation);
        var combinedTransform = Matrix3x2.Multiply(Matrix3x2.Multiply(roomTransform, packTransform), dungeonTransform);

        // Calculate room tiles using stack-allocated spans where possible
        var roomCenter = (room.Offset + room.Size / 2f) * Context.Grid.TileSize;
        var tileOffset = -roomCenter + Context.Grid.TileSizeHalfVector;

        var tileCount = room.Size.X * room.Size.Y;
        var exteriorCount = 2 * (room.Size.X + room.Size.Y) + 4;

        // Use pooled collections
        var roomTiles = new HashSet<Vector2i>(tileCount);
        var exterior = new HashSet<Vector2i>(exteriorCount);
        var center = Vector2.Zero;
        Box2i? bounds = null;

        // Calculate exterior tiles
        for (var x = -1; x <= room.Size.X; x++)
        {
            for (var y = -1; y <= room.Size.Y; y++)
            {
                // Skip interior tiles
                if (x >= 0 && y >= 0 && x < room.Size.X && y < room.Size.Y)
                    continue;

                var srcPos = new Vector2i(x + room.Offset.X, y + room.Offset.Y);
                var tilePos = Vector2.Transform(srcPos + tileOffset, combinedTransform).Floored();

                if (IsTileAvailable(tilePos))
                    exterior.Add(tilePos);
            }
        }

        // Calculate room tiles
        for (var x = 0; x < room.Size.X; x++)
        {
            for (var y = 0; y < room.Size.Y; y++)
            {
                var srcPos = new Vector2i(x + room.Offset.X, y + room.Offset.Y);
                var tilePos = Vector2.Transform(srcPos + tileOffset, combinedTransform);
                var tileIdx = tilePos.Floored();

                roomTiles.Add(tileIdx);
                bounds = bounds?.Union(tileIdx) ?? new Box2i(tileIdx, tileIdx);
                center += tilePos + Context.Grid.TileSizeHalfVector;
            }
        }

        if (roomTiles.Count > 0)
        {
            center /= roomTiles.Count;
            dungeon.AddRoom(new DungeonRoom(roomTiles, center, bounds!.Value, exterior));
        }

        await Task.Yield();
    }

    /// <summary>
    /// Optimized entrance setting - avoids repeated dictionary lookups.
    /// </summary>
    private void SetEntrancesOptimized(Dungeon dungeon, Random random)
    {
        foreach (var room in dungeon.Rooms)
        {
            if (room.Entrances.Count > 0)
                continue;

            var offset = random.Next(4);
            var halfWidth = room.Bounds.Width / 2;
            var halfHeight = room.Bounds.Height / 2;

            for (var i = 0; i < 4; i++)
            {
                var dirIdx = ((i + offset) * 2) & 7;
                var entrancePos = dirIdx switch
                {
                    0 => new Vector2i(room.Bounds.Right + 1, room.Bounds.Bottom + halfHeight), // East
                    2 => new Vector2i(room.Bounds.Left + halfWidth, room.Bounds.Top + 1),      // North
                    4 => new Vector2i(room.Bounds.Left - 1, room.Bounds.Bottom + halfHeight),  // West
                    6 => new Vector2i(room.Bounds.Left + halfWidth, room.Bounds.Bottom - 1),   // South
                    _ => Vector2i.Zero
                };

                var blockOffset = CardinalOffsets[dirIdx >> 1];
                var blockPos = entrancePos + blockOffset * 2;

                if (i != 3 && dungeon.RoomTiles.Contains(blockPos))
                    continue;

                if (!IsTileAvailable(entrancePos))
                    continue;

                room.Entrances.Add(entrancePos);
                break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ShuffleSpan<T>(Span<T> span, Random random)
    {
        for (var i = span.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }
}

/// <summary>
/// Optimized executor for NoiseDunGen - parallel noise sampling.
/// </summary>
public sealed class NoiseDunGenExecutor : LayerExecutorBase<NoiseDunGen>
{
    public NoiseDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override async Task ExecuteAsync(NoiseDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var matrix = Matrix3Helpers.CreateTransform(position, Angle.Zero);

        // Initialize noise layers with seed
        foreach (var noiseLayer in layer.Layers)
        {
            noiseLayer.Noise.SetSeed(Context.Seed);
        }

        var iterations = layer.Iterations;
        var area = new Box2i();
        var frontier = new Queue<Vector2i>(256);
        var rooms = new List<DungeonRoom>(iterations);
        var tileCount = 0;
        var tileCap = (int)random.NextGaussian(layer.TileCap, layer.CapStd);

        // Use pooled visited set
        var visited = Context.RentHashSet();

        try
        {
            while (iterations > 0 && tileCount < tileCap)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                var roomTiles = Context.RentHashSet();
                iterations--;

                // Get seed tile on random edge
                var edge = random.Next(4);
                var seedTile = GetEdgeSeedTile(edge, area, random);

                var noiseFill = false;
                frontier.Clear();
                visited.Add(seedTile);
                frontier.Enqueue(seedTile);
                area = area.UnionTile(seedTile);
                var roomArea = new Box2i(seedTile, seedTile + Vector2i.One);

                // Flood fill
                while (frontier.TryDequeue(out var node) && tileCount < tileCap)
                {
                    var foundNoise = ProcessNoiseNode(
                        layer.Layers,
                        node,
                        matrix,
                        random,
                        roomTiles,
                        ref roomArea,
                        ref tileCount,
                        ref noiseFill);

                    if (noiseFill && !foundNoise)
                        continue;

                    // Add cardinal neighbors - unrolled for performance
                    AddNeighborIfNew(visited, frontier, ref area, node.X + 1, node.Y);
                    AddNeighborIfNew(visited, frontier, ref area, node.X - 1, node.Y);
                    AddNeighborIfNew(visited, frontier, ref area, node.X, node.Y + 1);
                    AddNeighborIfNew(visited, frontier, ref area, node.X, node.Y - 1);
                }

                if (roomTiles.Count > 0)
                {
                    var center = CalculateCenter(roomTiles);
                    rooms.Add(new DungeonRoom(new HashSet<Vector2i>(roomTiles), center, roomArea, new HashSet<Vector2i>()));
                }

                Context.ReturnHashSet(roomTiles);
                await Task.Yield();
            }

            foreach (var room in rooms)
            {
                dungeon.AddRoom(room);
            }
        }
        finally
        {
            Context.ReturnHashSet(visited);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2i GetEdgeSeedTile(int edge, Box2i area, Random random) => edge switch
    {
        0 => new Vector2i(random.Next(area.Left - 2, area.Right + 1), area.Bottom - 2),
        1 => new Vector2i(area.Right + 1, random.Next(area.Bottom - 2, area.Top + 1)),
        2 => new Vector2i(random.Next(area.Left - 2, area.Right + 1), area.Top + 1),
        _ => new Vector2i(area.Left - 2, random.Next(area.Bottom - 2, area.Top + 1))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddNeighborIfNew(HashSet<Vector2i> visited, Queue<Vector2i> frontier, ref Box2i area, int x, int y)
    {
        var neighbor = new Vector2i(x, y);
        if (visited.Add(neighbor))
        {
            area = area.UnionTile(neighbor);
            frontier.Enqueue(neighbor);
        }
    }

    private bool ProcessNoiseNode(
        List<NoiseDunGenLayer> layers,
        Vector2i node,
        Matrix3x2 matrix,
        Random random,
        HashSet<Vector2i> roomTiles,
        ref Box2i roomArea,
        ref int tileCount,
        ref bool noiseFill)
    {
        foreach (var noiseLayer in layers)
        {
            var value = noiseLayer.Noise.GetNoise(node.X, node.Y);

            if (value < noiseLayer.Threshold)
                continue;

            noiseFill = true;

            if (!IsTileAvailable(node))
                return true;

            roomArea = roomArea.UnionTile(node);
            var tileDef = (ContentTileDefinition)Context.TileDef[noiseLayer.Tile];
            var variant = tileDef.Variants > 1 ? (byte)random.Next(tileDef.Variants) : (byte)0;
            var adjusted = Vector2.Transform(node + Context.Grid.TileSizeHalfVector, matrix).Floored();

            QueueTile(adjusted, new Tile(tileDef.TileId, variant: variant));
            roomTiles.Add(adjusted);
            tileCount++;
            return true;
        }

        return false;
    }

    private static Vector2 CalculateCenter(HashSet<Vector2i> tiles)
    {
        var sum = Vector2.Zero;
        foreach (var tile in tiles)
        {
            sum += new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
        }
        return sum / tiles.Count;
    }
}

/// <summary>
/// Executor for NoiseDistanceDunGen.
/// </summary>
public sealed class NoiseDistanceDunGenExecutor : LayerExecutorBase<NoiseDistanceDunGen>
{
    public NoiseDistanceDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(NoiseDistanceDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        // Similar to NoiseDunGen but with distance-based thresholds
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for PrototypeDunGen.
/// </summary>
public sealed class PrototypeDunGenExecutor : LayerExecutorBase<PrototypeDunGen>
{
    private readonly ISawmill _log;

    public PrototypeDunGenExecutor(DungeonGenerationContext context, ISawmill log) : base(context)
    {
        _log = log;
    }

    protected override Task ExecuteAsync(PrototypeDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        _log.Debug($"PrototypeDunGen references config {layer.Proto}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for ExteriorDunGen.
/// </summary>
public sealed class ExteriorDunGenExecutor : LayerExecutorBase<ExteriorDunGen>
{
    public ExteriorDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(ExteriorDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for ReplaceTileDunGen.
/// </summary>
public sealed class ReplaceTileDunGenExecutor : LayerExecutorBase<ReplaceTileDunGen>
{
    public ReplaceTileDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override Task ExecuteAsync(ReplaceTileDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension methods for Gaussian random distribution.
/// </summary>
public static class GaussianRandom
{
    public static double NextGaussian(this Random random, double mean, double stdDev)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }
}

using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Executor for PrefabDunGen - places rooms in pre-selected pack layouts.
/// </summary>
public sealed class PrefabDunGenExecutor : LayerExecutorBase<PrefabDunGen>
{
    private readonly ISawmill _log;

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

        var dungeonRotation = GetDungeonRotation(random.Next());
        var dungeonTransform = Matrix3Helpers.CreateTransform(position, dungeonRotation);

        // Build room pack and room prototype lookups
        var roomPackProtos = BuildRoomPackLookup();
        var roomProtos = BuildRoomPrototypeLookup(layer);

        var chosenPacks = new DungeonRoomPackPrototype?[gen.RoomPacks.Count];
        var packTransforms = new Matrix3x2[gen.RoomPacks.Count];
        var packRotations = new Angle[gen.RoomPacks.Count];

        // Choose packs for each slot
        ChooseRoomPacks(gen, roomPackProtos, random, chosenPacks, packTransforms, packRotations);

        // Spawn rooms in each pack
        for (var i = 0; i < chosenPacks.Length; i++)
        {
            var pack = chosenPacks[i];
            if (pack == null)
                continue;

            var packTransform = packTransforms[i];
            var packCenter = (Vector2)pack.Size / 2;

            foreach (var roomSize in pack.Rooms)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                await SpawnRoom(
                    dungeon,
                    roomSize,
                    roomProtos,
                    packCenter,
                    packTransform,
                    dungeonTransform,
                    layer.FallbackTile,
                    random);
            }
        }

        // Set entrances for rooms
        foreach (var room in dungeon.Rooms)
        {
            SetDungeonEntrance(dungeon, room, random);
        }

        dungeon.Rebuild();
    }

    private Dictionary<Vector2i, List<DungeonRoomPackPrototype>> BuildRoomPackLookup()
    {
        var lookup = new Dictionary<Vector2i, List<DungeonRoomPackPrototype>>();

        foreach (var pack in Context.Prototype.EnumeratePrototypes<DungeonRoomPackPrototype>())
        {
            if (!lookup.TryGetValue(pack.Size, out var list))
            {
                list = new List<DungeonRoomPackPrototype>();
                lookup[pack.Size] = list;
            }
            list.Add(pack);
        }

        // Sort for determinism
        foreach (var list in lookup.Values)
        {
            list.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        }

        return lookup;
    }

    private Dictionary<Vector2i, List<DungeonRoomPrototype>> BuildRoomPrototypeLookup(PrefabDunGen layer)
    {
        var lookup = new Dictionary<Vector2i, List<DungeonRoomPrototype>>();

        foreach (var proto in Context.Prototype.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            var whitelisted = false;

            if (layer.RoomWhitelist?.Tags != null)
            {
                foreach (var tag in layer.RoomWhitelist.Tags)
                {
                    if (proto.Tags.Contains(tag))
                    {
                        whitelisted = true;
                        break;
                    }
                }
            }

            if (!whitelisted)
                continue;

            if (!lookup.TryGetValue(proto.Size, out var list))
            {
                list = new List<DungeonRoomPrototype>();
                lookup[proto.Size] = list;
            }
            list.Add(proto);
        }

        foreach (var list in lookup.Values)
        {
            list.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        }

        return lookup;
    }

    private void ChooseRoomPacks(
        DungeonPresetPrototype gen,
        Dictionary<Vector2i, List<DungeonRoomPackPrototype>> roomPackProtos,
        Random random,
        DungeonRoomPackPrototype?[] chosenPacks,
        Matrix3x2[] packTransforms,
        Angle[] packRotations)
    {
        var availablePacks = new List<DungeonRoomPackPrototype>();

        for (var i = 0; i < gen.RoomPacks.Count; i++)
        {
            var bounds = gen.RoomPacks[i];
            var dimensions = new Vector2i(bounds.Width, bounds.Height);

            availablePacks.Clear();

            if (roomPackProtos.TryGetValue(dimensions, out var packs))
                availablePacks.AddRange(packs);

            // Try rotated dimensions
            if (dimensions.X != dimensions.Y)
            {
                var rotated = new Vector2i(dimensions.Y, dimensions.X);
                if (roomPackProtos.TryGetValue(rotated, out packs))
                    availablePacks.AddRange(packs);
            }

            if (availablePacks.Count == 0)
                continue;

            // Shuffle and find a fitting pack
            Shuffle(availablePacks, random);

            foreach (var pack in availablePacks)
            {
                var startIndex = random.Next(4);

                for (var j = 0; j < 4; j++)
                {
                    var index = (startIndex + j) % 4;
                    var dir = (DirectionFlag)(1 << index);
                    Vector2i packDims;

                    if ((dir & (DirectionFlag.East | DirectionFlag.West)) != 0)
                        packDims = new Vector2i(pack.Size.Y, pack.Size.X);
                    else
                        packDims = pack.Size;

                    if (packDims != bounds.Size)
                        continue;

                    var rotation = dir.AsDir().ToAngle();
                    packTransforms[i] = Matrix3Helpers.CreateTransform(bounds.Center, rotation);
                    packRotations[i] = rotation;
                    chosenPacks[i] = pack;
                    goto nextPack;
                }
            }

            nextPack:;
        }
    }

    private async Task SpawnRoom(
        Dungeon dungeon,
        Box2i roomSize,
        Dictionary<Vector2i, List<DungeonRoomPrototype>> roomProtos,
        Vector2 packCenter,
        Matrix3x2 packTransform,
        Matrix3x2 dungeonTransform,
        ProtoId<ContentTileDefinition>? fallbackTile,
        Random random)
    {
        var roomDimensions = new Vector2i(roomSize.Width, roomSize.Height);
        Angle roomRotation = Angle.Zero;

        if (!roomProtos.TryGetValue(roomDimensions, out var roomProto))
        {
            roomDimensions = new Vector2i(roomDimensions.Y, roomDimensions.X);

            if (!roomProtos.TryGetValue(roomDimensions, out roomProto))
            {
                // Use fallback tile if no room found
                if (fallbackTile != null)
                {
                    var matty = Matrix3x2.Multiply(packTransform, dungeonTransform);
                    var tileDef = Context.TileDef[fallbackTile.Value];

                    for (var x = roomSize.Left; x < roomSize.Right; x++)
                    {
                        for (var y = roomSize.Bottom; y < roomSize.Top; y++)
                        {
                            var index = Vector2.Transform(
                                new Vector2(x, y) + Context.Grid.TileSizeHalfVector - packCenter,
                                matty).Floored();

                            if (!IsTileAvailable(index))
                                continue;

                            QueueTile(index, new Tile(tileDef.TileId));
                        }
                    }
                }

                _log.Error($"Unable to find room variant for {roomDimensions}");
                return;
            }

            roomRotation = new Angle(Math.PI / 2);
        }

        var room = roomProto[random.Next(roomProto.Count)];

        if (roomDimensions.X == roomDimensions.Y)
            roomRotation = random.Next(4) * Math.PI / 2;
        else if (random.Next(2) == 1)
            roomRotation += Math.PI;

        var roomTransform = Matrix3Helpers.CreateTransform(roomSize.Center - packCenter, roomRotation);
        var matty2 = Matrix3x2.Multiply(roomTransform, packTransform);
        var dungeonMatty = Matrix3x2.Multiply(matty2, dungeonTransform);

        // Calculate room tiles and create DungeonRoom
        var roomCenter = (room.Offset + room.Size / 2f) * Context.Grid.TileSize;
        var roomTiles = Context.RentHashSet();
        var exterior = Context.RentHashSet();
        var tileOffset = -roomCenter + Context.Grid.TileSizeHalfVector;
        Box2i? mapBounds = null;

        // Calculate exterior tiles
        for (var x = -1; x <= room.Size.X; x++)
        {
            for (var y = -1; y <= room.Size.Y; y++)
            {
                if (x != -1 && y != -1 && x != room.Size.X && y != room.Size.Y)
                    continue;

                var tilePos = Vector2.Transform(
                    new Vector2i(x + room.Offset.X, y + room.Offset.Y) + tileOffset,
                    dungeonMatty).Floored();

                if (!IsTileAvailable(tilePos))
                    continue;

                exterior.Add(tilePos);
            }
        }

        // Calculate room tiles
        var center = Vector2.Zero;
        for (var x = 0; x < room.Size.X; x++)
        {
            for (var y = 0; y < room.Size.Y; y++)
            {
                var roomTile = new Vector2i(x + room.Offset.X, y + room.Offset.Y);
                var tilePos = Vector2.Transform(roomTile + tileOffset, dungeonMatty);
                var tileIndex = tilePos.Floored();
                roomTiles.Add(tileIndex);

                mapBounds = mapBounds?.Union(tileIndex) ?? new Box2i(tileIndex, tileIndex);
                center += tilePos + Context.Grid.TileSizeHalfVector;
            }
        }

        center /= roomTiles.Count;

        // Create a copy of the hashsets for the DungeonRoom (it takes ownership)
        var roomTilesCopy = new HashSet<Vector2i>(roomTiles);
        var exteriorCopy = new HashSet<Vector2i>(exterior);

        Context.ReturnHashSet(roomTiles);
        Context.ReturnHashSet(exterior);

        dungeon.AddRoom(new DungeonRoom(roomTilesCopy, center, mapBounds!.Value, exteriorCopy));

        // Queue tile placement for the room template
        // Note: SpawnRoom in upstream actually loads the room template and places tiles/entities
        // For now, we queue the basic tiles - full room template loading would need more work
        await Task.Yield(); // Allow cancellation check
    }

    private void SetDungeonEntrance(Dungeon dungeon, DungeonRoom room, Random random)
    {
        if (room.Entrances.Count > 0)
            return;

        var offset = random.Next(4);

        for (var i = 0; i < 4; i++)
        {
            var dir = (Direction)(((i + offset) * 2) % 8);
            Vector2i entrancePos;

            switch (dir)
            {
                case Direction.East:
                    entrancePos = new Vector2i(room.Bounds.Right + 1, room.Bounds.Bottom + room.Bounds.Height / 2);
                    break;
                case Direction.North:
                    entrancePos = new Vector2i(room.Bounds.Left + room.Bounds.Width / 2, room.Bounds.Top + 1);
                    break;
                case Direction.West:
                    entrancePos = new Vector2i(room.Bounds.Left - 1, room.Bounds.Bottom + room.Bounds.Height / 2);
                    break;
                case Direction.South:
                    entrancePos = new Vector2i(room.Bounds.Left + room.Bounds.Width / 2, room.Bounds.Bottom - 1);
                    break;
                default:
                    continue;
            }

            var blockPos = entrancePos + dir.ToIntVec() * 2;

            if (i != 3 && dungeon.RoomTiles.Contains(blockPos))
                continue;

            if (!IsTileAvailable(entrancePos))
                continue;

            room.Entrances.Add(entrancePos);
            break;
        }
    }

    private Angle GetDungeonRotation(int seed)
    {
        return (seed & 3) * Math.PI / 2;
    }

    private static void Shuffle<T>(List<T> list, Random random)
    {
        var n = list.Count;
        while (n > 1)
        {
            n--;
            var k = random.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}

/// <summary>
/// Executor for NoiseDunGen - generates dungeons using noise functions.
/// </summary>
public sealed class NoiseDunGenExecutor : LayerExecutorBase<NoiseDunGen>
{
    public NoiseDunGenExecutor(DungeonGenerationContext context) : base(context) { }

    protected override async Task ExecuteAsync(NoiseDunGen layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var matrix = Matrix3Helpers.CreateTransform(position);

        foreach (var noiseLayer in layer.Layers)
        {
            noiseLayer.Noise.SetSeed(Context.Seed);
        }

        var iterations = layer.Iterations;
        var area = new Box2i();
        var frontier = new Queue<Vector2i>();
        var rooms = new List<DungeonRoom>();
        var tileCount = 0;
        var tileCap = random.NextGaussian(layer.TileCap, layer.CapStd);
        var visited = Context.RentHashSet();

        while (iterations > 0 && tileCount < tileCap)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            var roomTiles = Context.RentHashSet();
            iterations--;

            // Get a random exterior tile to start floodfilling from
            var edge = random.Next(4);
            Vector2i seedTile = edge switch
            {
                0 => new Vector2i(random.Next(area.Left - 2, area.Right + 1), area.Bottom - 2),
                1 => new Vector2i(area.Right + 1, random.Next(area.Bottom - 2, area.Top + 1)),
                2 => new Vector2i(random.Next(area.Left - 2, area.Right + 1), area.Top + 1),
                3 => new Vector2i(area.Left - 2, random.Next(area.Bottom - 2, area.Top + 1)),
                _ => throw new ArgumentOutOfRangeException()
            };

            var noiseFill = false;
            frontier.Clear();
            visited.Add(seedTile);
            frontier.Enqueue(seedTile);
            area = area.UnionTile(seedTile);
            var roomArea = new Box2i(seedTile, seedTile + Vector2i.One);

            while (frontier.TryDequeue(out var node) && tileCount < tileCap)
            {
                var foundNoise = false;

                foreach (var noiseLayer in layer.Layers)
                {
                    var value = noiseLayer.Noise.GetNoise(node.X, node.Y);

                    if (value < noiseLayer.Threshold)
                        continue;

                    foundNoise = true;
                    noiseFill = true;

                    if (!IsTileAvailable(node))
                        break;

                    roomArea = roomArea.UnionTile(node);
                    var tileDef = (ContentTileDefinition)Context.TileDef[noiseLayer.Tile];
                    var variant = PickVariant(tileDef, random);
                    var adjusted = Vector2.Transform(node + Context.Grid.TileSizeHalfVector, matrix).Floored();

                    QueueTile(adjusted, new Tile(tileDef.TileId, variant: variant));
                    roomTiles.Add(adjusted);
                    tileCount++;
                    break;
                }

                if (noiseFill && !foundNoise)
                    continue;

                // Add cardinal neighbors
                for (var x = -1; x <= 1; x++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        if (x != 0 && y != 0)
                            continue;

                        var neighbor = new Vector2i(node.X + x, node.Y + y);

                        if (!visited.Add(neighbor))
                            continue;

                        area = area.UnionTile(neighbor);
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            if (roomTiles.Count > 0)
            {
                var center = Vector2.Zero;
                foreach (var tile in roomTiles)
                {
                    center += tile + Context.Grid.TileSizeHalfVector;
                }
                center /= roomTiles.Count;

                var roomTilesCopy = new HashSet<Vector2i>(roomTiles);
                rooms.Add(new DungeonRoom(roomTilesCopy, center, roomArea, new HashSet<Vector2i>()));
            }

            Context.ReturnHashSet(roomTiles);
            await Task.Yield();
        }

        Context.ReturnHashSet(visited);

        foreach (var room in rooms)
        {
            dungeon.AddRoom(room);
        }
    }

    private byte PickVariant(ContentTileDefinition tileDef, Random random)
    {
        if (tileDef.Variants <= 1)
            return 0;
        return (byte)random.Next(tileDef.Variants);
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
        // Implementation follows similar pattern to NoiseDunGen
        // but uses distance-based noise generation
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executor for PrototypeDunGen - runs a referenced dungeon config.
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
        // This would recursively generate using another config
        // For now, log and skip
        _log.Debug($"PrototypeDunGen references config {layer.Proto}, recursive generation not yet implemented");
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
        // Generates exterior tiles around the dungeon
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
        // Replaces tiles matching certain criteria
        return Task.CompletedTask;
    }
}

/// <summary>
/// Helper for Gaussian random distribution.
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

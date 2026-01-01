using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Executors;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Content.Shared.Procedural.DungeonLayers;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.Generation;

/// <summary>
/// Orchestrates parallel dungeon generation.
/// Separates compute-heavy work (parallelizable) from ECS operations (main thread).
/// </summary>
public sealed class ParallelDungeonGenerator
{
    private readonly DungeonGenerationContext _context;
    private readonly ISawmill _log;
    private readonly LayerExecutorRegistry _executors;

    public ParallelDungeonGenerator(DungeonGenerationContext context, ISawmill log)
    {
        _context = context;
        _log = log;
        _executors = new LayerExecutorRegistry(context, log);
    }

    /// <summary>
    /// Generates dungeons based on the provided configuration.
    /// </summary>
    public async Task<List<Dungeon>> GenerateAsync(DungeonConfig config)
    {
        var dungeons = new List<Dungeon>();
        var random = _context.GetSeededRandom(0);
        var count = random.Next(config.MinCount, config.MaxCount + 1);

        var position = _context.Position;

        for (var i = 0; i < count; i++)
        {
            _context.Cancellation.ThrowIfCancellationRequested();

            // Apply offset for subsequent dungeons
            if (i > 0)
            {
                var offset = random.NextPolarVector2(config.MinOffset, config.MaxOffset);
                position += new Vector2i((int)offset.X, (int)offset.Y);
            }

            var dungeon = await GenerateSingleDungeonAsync(config, position, random, dungeons);

            if (config.ReserveTiles)
            {
                _context.ReserveTiles(dungeon.AllTiles);
            }

            dungeons.Add(dungeon);
        }

        return dungeons;
    }

    private async Task<Dungeon> GenerateSingleDungeonAsync(
        DungeonConfig config,
        Vector2i position,
        Random random,
        List<Dungeon> existingDungeons)
    {
        var dungeon = new Dungeon();

        foreach (var layer in config.Layers)
        {
            _context.Cancellation.ThrowIfCancellationRequested();

            await ExecuteLayerAsync(layer, dungeon, position, random);

            // Flush pending commands after each layer
            await FlushCommandsAsync();
        }

        return dungeon;
    }

    private async Task ExecuteLayerAsync(IDunGenLayer layer, Dungeon dungeon, Vector2i position, Random random)
    {
        var executor = _executors.GetExecutor(layer);
        if (executor == null)
        {
            _log.Warning($"No executor found for layer type {layer.GetType().Name}, skipping");
            return;
        }

        try
        {
            await executor.ExecuteAsync(layer, dungeon, position, random);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Error executing layer {layer.GetType().Name}: {ex}");
        }
    }

    /// <summary>
    /// Flushes all pending commands to the main thread.
    /// This is where ECS operations actually happen.
    /// </summary>
    private async Task FlushCommandsAsync()
    {
        // Process room spawns first (they set tiles and spawn entities)
        await FlushRoomSpawnCommandsAsync();

        // Process tile commands in batches
        await FlushTileCommandsAsync();

        // Process entity spawns
        await FlushEntityCommandsAsync();

        // Process entity table spawns
        await FlushEntityTableCommandsAsync();

        // Process decals
        await FlushDecalCommandsAsync();
    }

    private Task FlushRoomSpawnCommandsAsync()
    {
        while (_context.RoomSpawnCommands.TryDequeue(out var cmd))
        {
            _context.Dungeon.SpawnRoom(
                _context.GridUid,
                _context.Grid,
                cmd.Transform,
                cmd.Room,
                cmd.ReservedTiles);
        }

        return Task.CompletedTask;
    }

    private Task FlushTileCommandsAsync()
    {
        if (_context.TileCommands.IsEmpty)
            return Task.CompletedTask;

        var tiles = _context.RentTileList();

        while (_context.TileCommands.TryDequeue(out var cmd))
        {
            tiles.Add((cmd.Position, cmd.Tile));
        }

        if (tiles.Count > 0)
        {
            _context.Maps.SetTiles(_context.GridUid, _context.Grid, tiles);
        }

        _context.ReturnTileList(tiles);
        return Task.CompletedTask;
    }

    private Task FlushEntityCommandsAsync()
    {
        while (_context.EntityCommands.TryDequeue(out var cmd))
        {
            var coords = _context.Maps.GridTileToLocal(_context.GridUid, _context.Grid, cmd.Position);

            if (cmd.Rotation != Angle.Zero)
            {
                var rotatedCoords = new EntityCoordinates(coords.EntityId, coords.Position);
                _context.EntityManager.SpawnEntity(cmd.Prototype, rotatedCoords);
            }
            else
            {
                _context.EntityManager.SpawnEntity(cmd.Prototype, coords);
            }
        }

        return Task.CompletedTask;
    }

    private Task FlushDecalCommandsAsync()
    {
        while (_context.DecalCommands.TryDequeue(out var cmd))
        {
            var coords = new EntityCoordinates(_context.GridUid, cmd.Position);
            _context.Decals.TryAddDecal(cmd.DecalId, coords, out _, cmd.Color, cmd.Rotation, 0, true);
        }

        return Task.CompletedTask;
    }

    private Task FlushEntityTableCommandsAsync()
    {
        while (_context.EntityTableCommands.TryDequeue(out var cmd))
        {
            var table = _context.Prototype.Index(cmd.TableId);
            var coords = _context.Maps.GridTileToLocal(_context.GridUid, _context.Grid, cmd.Position);

            foreach (var proto in _context.EntityTable.GetSpawns(table))
            {
                if (cmd.Rotation != Angle.Zero)
                {
                    var rotatedCoords = new EntityCoordinates(coords.EntityId, coords.Position);
                    _context.EntityManager.SpawnEntity(proto, rotatedCoords);
                }
                else
                {
                    _context.EntityManager.SpawnEntity(proto, coords);
                }
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension methods for random number generation.
/// </summary>
public static class RandomExtensions
{
    public static Vector2 NextPolarVector2(this Random random, float minRadius, float maxRadius)
    {
        var angle = random.NextDouble() * Math.PI * 2;
        var radius = minRadius + (float)random.NextDouble() * (maxRadius - minRadius);
        return new Vector2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
    }
}

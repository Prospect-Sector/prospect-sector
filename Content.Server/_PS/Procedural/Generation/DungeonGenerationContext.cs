using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using Content.Server.Decals;
using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Microsoft.Extensions.ObjectPool;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Threading;

namespace Content.Server._PS.Procedural.Generation;

/// <summary>
/// Shared context for dungeon generation, providing access to systems and pooled resources.
/// This class is designed to minimize allocations during generation.
/// </summary>
public sealed class DungeonGenerationContext : IDisposable
{
    public IEntityManager EntityManager { get; }
    public IPrototypeManager Prototype { get; }
    public ITileDefinitionManager TileDef { get; }
    public SharedMapSystem Maps { get; }
    public DecalSystem Decals { get; }
    public SharedTransformSystem Transform { get; }
    public IParallelManager Parallel { get; }
    public EntityTableSystem EntityTable { get; }

    public EntityUid GridUid { get; }
    public MapGridComponent Grid { get; }
    public Vector2i Position { get; }
    public int Seed { get; }
    public int WorkerCount { get; }
    public CancellationToken Cancellation { get; }

    /// <summary>
    /// Thread-safe random for parallel operations.
    /// Each thread should use GetThreadRandom() for deterministic results.
    /// </summary>
    private readonly ThreadLocal<Random> _threadRandom;

    /// <summary>
    /// Tiles that have been reserved and cannot be used.
    /// Thread-safe for parallel access.
    /// </summary>
    public ConcurrentDictionary<Vector2i, byte> ReservedTiles { get; } = new();

    /// <summary>
    /// Queued tile operations to be executed on the main thread.
    /// </summary>
    public ConcurrentQueue<TileCommand> TileCommands { get; } = new();

    /// <summary>
    /// Queued entity spawn operations to be executed on the main thread.
    /// </summary>
    public ConcurrentQueue<EntitySpawnCommand> EntityCommands { get; } = new();

    /// <summary>
    /// Queued decal operations to be executed on the main thread.
    /// </summary>
    public ConcurrentQueue<DecalCommand> DecalCommands { get; } = new();

    /// <summary>
    /// Queued entity table spawn operations to be executed on the main thread.
    /// </summary>
    public ConcurrentQueue<EntityTableSpawnCommand> EntityTableCommands { get; } = new();

    // Object pools to reduce allocations
    private readonly ObjectPool<HashSet<Vector2i>> _hashSetPool;
    private readonly ObjectPool<List<Vector2i>> _listPool;
    private readonly ObjectPool<List<(Vector2i, Tile)>> _tileListPool;

    public DungeonGenerationContext(
        IEntityManager entityManager,
        IPrototypeManager prototype,
        ITileDefinitionManager tileDef,
        SharedMapSystem maps,
        DecalSystem decals,
        SharedTransformSystem transform,
        IParallelManager parallel,
        EntityTableSystem entityTable,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i position,
        int seed,
        int workerCount,
        CancellationToken cancellation)
    {
        EntityManager = entityManager;
        Prototype = prototype;
        TileDef = tileDef;
        Maps = maps;
        Decals = decals;
        Transform = transform;
        Parallel = parallel;
        EntityTable = entityTable;
        GridUid = gridUid;
        Grid = grid;
        Position = position;
        Seed = seed;
        WorkerCount = workerCount;
        Cancellation = cancellation;

        // Create thread-local randoms seeded deterministically
        _threadRandom = new ThreadLocal<Random>(() =>
        {
            var threadId = Environment.CurrentManagedThreadId;
            return new Random(seed ^ threadId);
        }, trackAllValues: false);

        // Initialize object pools
        _hashSetPool = new DefaultObjectPool<HashSet<Vector2i>>(new HashSetPolicy(), 64);
        _listPool = new DefaultObjectPool<List<Vector2i>>(new ListPolicy<Vector2i>(), 64);
        _tileListPool = new DefaultObjectPool<List<(Vector2i, Tile)>>(new ListPolicy<(Vector2i, Tile)>(), 32);
    }

    /// <summary>
    /// Gets a thread-local random instance for deterministic parallel generation.
    /// </summary>
    public Random GetThreadRandom() => _threadRandom.Value!;

    /// <summary>
    /// Gets a seeded random for a specific sub-operation.
    /// Useful when you need reproducible results for a particular step.
    /// </summary>
    public Random GetSeededRandom(int additionalSeed) => new(Seed ^ additionalSeed);

    /// <summary>
    /// Rent a HashSet from the pool.
    /// </summary>
    public HashSet<Vector2i> RentHashSet() => _hashSetPool.Get();

    /// <summary>
    /// Return a HashSet to the pool.
    /// </summary>
    public void ReturnHashSet(HashSet<Vector2i> set)
    {
        set.Clear();
        _hashSetPool.Return(set);
    }

    /// <summary>
    /// Rent a List from the pool.
    /// </summary>
    public List<Vector2i> RentList() => _listPool.Get();

    /// <summary>
    /// Return a List to the pool.
    /// </summary>
    public void ReturnList(List<Vector2i> list)
    {
        list.Clear();
        _listPool.Return(list);
    }

    /// <summary>
    /// Rent a tile list from the pool.
    /// </summary>
    public List<(Vector2i, Tile)> RentTileList() => _tileListPool.Get();

    /// <summary>
    /// Return a tile list to the pool.
    /// </summary>
    public void ReturnTileList(List<(Vector2i, Tile)> list)
    {
        list.Clear();
        _tileListPool.Return(list);
    }

    /// <summary>
    /// Checks if a tile is available (not reserved).
    /// </summary>
    public bool IsTileAvailable(Vector2i tile) => !ReservedTiles.ContainsKey(tile);

    /// <summary>
    /// Attempts to reserve a tile. Returns true if successful.
    /// </summary>
    public bool TryReserveTile(Vector2i tile) => ReservedTiles.TryAdd(tile, 0);

    /// <summary>
    /// Reserves multiple tiles atomically.
    /// </summary>
    public void ReserveTiles(IEnumerable<Vector2i> tiles)
    {
        foreach (var tile in tiles)
        {
            ReservedTiles.TryAdd(tile, 0);
        }
    }

    public void Dispose()
    {
        _threadRandom.Dispose();
    }

    private sealed class HashSetPolicy : PooledObjectPolicy<HashSet<Vector2i>>
    {
        public override HashSet<Vector2i> Create() => new(256);
        public override bool Return(HashSet<Vector2i> obj)
        {
            obj.Clear();
            return true;
        }
    }

    private sealed class ListPolicy<T> : PooledObjectPolicy<List<T>>
    {
        public override List<T> Create() => new(128);
        public override bool Return(List<T> obj)
        {
            obj.Clear();
            return true;
        }
    }
}

/// <summary>
/// Command to set tiles on the grid. Executed on main thread.
/// </summary>
public readonly record struct TileCommand(Vector2i Position, Tile Tile);

/// <summary>
/// Command to spawn an entity. Executed on main thread.
/// </summary>
public readonly record struct EntitySpawnCommand(string Prototype, Vector2i Position, Angle Rotation = default);

/// <summary>
/// Command to place a decal. Executed on main thread.
/// </summary>
public readonly record struct DecalCommand(string DecalId, Vector2 Position, Angle Rotation = default, Color? Color = null);

/// <summary>
/// Command to spawn entities from an entity table. Executed on main thread.
/// </summary>
public readonly record struct EntityTableSpawnCommand(ProtoId<EntityTablePrototype> TableId, Vector2i Position, Angle Rotation = default);

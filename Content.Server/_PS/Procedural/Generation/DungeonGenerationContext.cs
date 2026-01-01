using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using Content.Server.Decals;
using Content.Server.Procedural;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Doors.Components;
using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Tag;
using Microsoft.Extensions.ObjectPool;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
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
    public DungeonSystem Dungeon { get; }
    public AnchorableSystem Anchorable { get; }
    public EntityLookupSystem Lookup { get; }
    public TagSystem Tags { get; }

    // Entity queries for physics checks
    public EntityQuery<PhysicsComponent> PhysicsQuery { get; }
    public EntityQuery<DoorComponent> DoorQuery { get; }

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

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

    /// <summary>
    /// Queued room spawn operations to be executed on the main thread.
    /// </summary>
    public ConcurrentQueue<RoomSpawnCommand> RoomSpawnCommands { get; } = new();

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
        DungeonSystem dungeon,
        AnchorableSystem anchorable,
        EntityLookupSystem lookup,
        TagSystem tags,
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
        Dungeon = dungeon;
        Anchorable = anchorable;
        Lookup = lookup;
        Tags = tags;
        GridUid = gridUid;
        Grid = grid;
        Position = position;
        Seed = seed;
        WorkerCount = workerCount;
        Cancellation = cancellation;

        // Initialize entity queries
        PhysicsQuery = entityManager.GetEntityQuery<PhysicsComponent>();
        DoorQuery = entityManager.GetEntityQuery<DoorComponent>();

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
    /// Checks if a tile is free of collision entities.
    /// </summary>
    public bool TileFree(Vector2i tile, int collisionLayer, int collisionMask)
    {
        return Anchorable.TileFree((GridUid, Grid), tile, collisionLayer, collisionMask);
    }

    /// <summary>
    /// Checks if a tile has a wall entity.
    /// </summary>
    public bool HasWall(Vector2i tile)
    {
        var anchored = Maps.GetAnchoredEntitiesEnumerator(GridUid, Grid, tile);

        while (anchored.MoveNext(out var uid))
        {
            if (Tags.HasTag(uid.Value, WallTag))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a tile has a hard physics entity that isn't a door.
    /// </summary>
    public bool HasHardPhysicsNonDoor(Vector2i tile)
    {
        var anchored = Maps.GetAnchoredEntitiesEnumerator(GridUid, Grid, tile);

        while (anchored.MoveNext(out var ent))
        {
            if (!PhysicsQuery.TryGetComponent(ent, out var physics) ||
                !physics.CanCollide ||
                !physics.Hard ||
                DoorQuery.HasComponent(ent.Value))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Clears door-blocking entities around a tile.
    /// </summary>
    public void ClearDoor(Dungeon dungeon, Vector2i indices, bool strict = false)
    {
        var flags = strict
            ? LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.StaticSundries
            : LookupFlags.Dynamic | LookupFlags.Static;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x != 0 && y != 0)
                    continue;

                var neighbor = new Vector2i(indices.X + x, indices.Y + y);

                if (!dungeon.RoomTiles.Contains(neighbor))
                    continue;

                var tilePos = Maps.GridTileToLocal(GridUid, Grid, neighbor);

                foreach (var ent in Lookup.GetEntitiesIntersecting(tilePos, flags))
                {
                    if (!PhysicsQuery.TryGetComponent(ent, out var physics) ||
                        !physics.CanCollide ||
                        !physics.Hard)
                    {
                        continue;
                    }

                    EntityManager.QueueDeleteEntity(ent);
                }
            }
        }
    }

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

/// <summary>
/// Command to spawn a room from a prototype template. Executed on main thread.
/// </summary>
public readonly record struct RoomSpawnCommand(DungeonRoomPrototype Room, Matrix3x2 Transform, HashSet<Vector2i>? ReservedTiles = null);

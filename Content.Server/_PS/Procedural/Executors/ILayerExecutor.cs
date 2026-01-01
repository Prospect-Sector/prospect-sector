using System.Numerics;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Shared.EntityTable;
using Content.Shared.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Interface for layer executors that process IDunGenLayer instances.
/// Executors separate compute-heavy operations (parallelizable) from ECS operations (queued for main thread).
/// </summary>
public interface ILayerExecutor
{
    /// <summary>
    /// Executes the layer, computing what needs to be done and queuing commands.
    /// </summary>
    /// <param name="layer">The layer configuration to execute.</param>
    /// <param name="dungeon">The dungeon being generated.</param>
    /// <param name="position">The position offset for this dungeon.</param>
    /// <param name="random">Random number generator for this operation.</param>
    Task ExecuteAsync(IDunGenLayer layer, Dungeon dungeon, Vector2i position, Random random);
}

/// <summary>
/// Base class for layer executors providing common functionality.
/// </summary>
public abstract class LayerExecutorBase<TLayer> : ILayerExecutor where TLayer : IDunGenLayer
{
    protected DungeonGenerationContext Context { get; }

    protected LayerExecutorBase(DungeonGenerationContext context)
    {
        Context = context;
    }

    public Task ExecuteAsync(IDunGenLayer layer, Dungeon dungeon, Vector2i position, Random random)
    {
        if (layer is not TLayer typedLayer)
            throw new ArgumentException($"Expected layer of type {typeof(TLayer).Name}", nameof(layer));

        return ExecuteAsync(typedLayer, dungeon, position, random);
    }

    protected abstract Task ExecuteAsync(TLayer layer, Dungeon dungeon, Vector2i position, Random random);

    /// <summary>
    /// Queues a tile to be set on the grid.
    /// </summary>
    protected void QueueTile(Vector2i position, Tile tile)
    {
        Context.TileCommands.Enqueue(new TileCommand(position, tile));
    }

    /// <summary>
    /// Queues multiple tiles to be set on the grid.
    /// </summary>
    protected void QueueTiles(IEnumerable<(Vector2i Position, Tile Tile)> tiles)
    {
        foreach (var (pos, tile) in tiles)
        {
            Context.TileCommands.Enqueue(new TileCommand(pos, tile));
        }
    }

    /// <summary>
    /// Queues an entity to be spawned.
    /// </summary>
    protected void QueueEntity(string prototype, Vector2i position, Angle rotation = default)
    {
        Context.EntityCommands.Enqueue(new EntitySpawnCommand(prototype, position, rotation));
    }

    /// <summary>
    /// Queues a decal to be placed.
    /// </summary>
    protected void QueueDecal(string decalId, Vector2 position, Angle rotation = default, Color? color = null)
    {
        Context.DecalCommands.Enqueue(new DecalCommand(decalId, position, rotation, color));
    }

    /// <summary>
    /// Queues entities from an entity table to be spawned.
    /// </summary>
    protected void QueueEntityTable(ProtoId<EntityTablePrototype> tableId, Vector2i position, Angle rotation = default)
    {
        Context.EntityTableCommands.Enqueue(new EntityTableSpawnCommand(tableId, position, rotation));
    }

    /// <summary>
    /// Checks if a tile is available for use.
    /// </summary>
    protected bool IsTileAvailable(Vector2i tile) => Context.IsTileAvailable(tile);
}

using Content.Server._PS.Procedural.Generation;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Content.Shared.Procedural.DungeonLayers;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.Log;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Registry that maps IDunGenLayer types to their executor implementations.
/// </summary>
public sealed class LayerExecutorRegistry
{
    private readonly Dictionary<Type, ILayerExecutor> _executors = new();
    private readonly DungeonGenerationContext _context;
    private readonly ISawmill _log;

    public LayerExecutorRegistry(DungeonGenerationContext context, ISawmill log)
    {
        _context = context;
        _log = log;

        RegisterExecutors();
    }

    private void RegisterExecutors()
    {
        // Dungeon Generators
        Register<PrefabDunGen>(new PrefabDunGenExecutor(_context, _log));
        Register<NoiseDunGen>(new NoiseDunGenExecutor(_context));
        Register<NoiseDistanceDunGen>(new NoiseDistanceDunGenExecutor(_context));
        Register<PrototypeDunGen>(new PrototypeDunGenExecutor(_context, _log));
        Register<ExteriorDunGen>(new ExteriorDunGenExecutor(_context));
        Register<ReplaceTileDunGen>(new ReplaceTileDunGenExecutor(_context));

        // Post Generation Layers
        Register<CorridorDunGen>(new CorridorDunGenExecutor(_context));
        Register<WormCorridorDunGen>(new WormCorridorDunGenExecutor(_context));
        Register<BoundaryWallDunGen>(new BoundaryWallDunGenExecutor(_context));
        Register<DungeonEntranceDunGen>(new DungeonEntranceDunGenExecutor(_context));
        Register<RoomEntranceDunGen>(new RoomEntranceDunGenExecutor(_context));
        Register<EntranceFlankDunGen>(new EntranceFlankDunGenExecutor(_context));
        Register<ExternalWindowDunGen>(new ExternalWindowDunGenExecutor(_context));
        Register<InternalWindowDunGen>(new InternalWindowDunGenExecutor(_context));
        Register<JunctionDunGen>(new JunctionDunGenExecutor(_context));
        Register<WallMountDunGen>(new WallMountDunGenExecutor(_context));
        Register<CornerClutterDunGen>(new CornerClutterDunGenExecutor(_context));
        Register<CorridorClutterDunGen>(new CorridorClutterDunGenExecutor(_context));
        Register<CorridorDecalSkirtingDunGen>(new CorridorDecalSkirtingDunGenExecutor(_context));
        Register<AutoCablingDunGen>(new AutoCablingDunGenExecutor(_context));
        Register<MiddleConnectionDunGen>(new MiddleConnectionDunGenExecutor(_context));
        Register<SplineDungeonConnectorDunGen>(new SplineDungeonConnectorDunGenExecutor(_context));

        // Dungeon Layers
        Register<EntityTableDunGen>(new EntityTableDunGenExecutor(_context));
        Register<FillGridDunGen>(new FillGridDunGenExecutor(_context));
        Register<MobsDunGen>(new MobsDunGenExecutor(_context));
        Register<OreDunGen>(new OreDunGenExecutor(_context));

        // Biome layers
        Register<BiomeDunGen>(new BiomeDunGenExecutor(_context));
        Register<BiomeMarkerLayerDunGen>(new BiomeMarkerLayerDunGenExecutor(_context));
    }

    private void Register<TLayer>(ILayerExecutor executor) where TLayer : IDunGenLayer
    {
        _executors[typeof(TLayer)] = executor;
    }

    /// <summary>
    /// Gets the executor for a given layer, or null if none is registered.
    /// </summary>
    public ILayerExecutor? GetExecutor(IDunGenLayer layer)
    {
        return _executors.TryGetValue(layer.GetType(), out var executor) ? executor : null;
    }
}

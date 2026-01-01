using System.Threading;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Server.Decals;
using Content.Shared.CCVar;
using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Threading;

namespace Content.Server._PS.Procedural;

/// <summary>
/// Prospect's parallel dungeon generation system.
/// Provides high-performance dungeon generation using parallel processing.
/// </summary>
public sealed class ProspectDungeonSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IParallelManager _parallel = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityTableSystem _entityTable = default!;

    private bool _enabled;
    private int _workerCount;

    private readonly Dictionary<int, CancellationTokenSource> _activeJobs = new();
    private int _nextJobId;

    public bool Enabled => _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(CCVars.ProspectParallelDungeons, OnEnabledChanged, true);
        _cfg.OnValueChanged(CCVars.ProspectDungeonWorkers, OnWorkerCountChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var cts in _activeJobs.Values)
        {
            cts.Cancel();
        }
        _activeJobs.Clear();

        _cfg.UnsubValueChanged(CCVars.ProspectParallelDungeons, OnEnabledChanged);
        _cfg.UnsubValueChanged(CCVars.ProspectDungeonWorkers, OnWorkerCountChanged);
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;
        Log.Info($"Prospect parallel dungeon generation {(value ? "enabled" : "disabled")}");
    }

    private void OnWorkerCountChanged(int value)
    {
        _workerCount = value <= 0 ? Environment.ProcessorCount : value;
        Log.Debug($"Prospect dungeon workers set to {_workerCount}");
    }

    /// <summary>
    /// Generates a dungeon asynchronously using parallel processing.
    /// </summary>
    public async Task<List<Dungeon>> GenerateAsync(
        DungeonConfig config,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i position,
        int seed,
        CancellationToken cancellation = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var jobId = Interlocked.Increment(ref _nextJobId);
        _activeJobs[jobId] = cts;

        try
        {
            var context = new DungeonGenerationContext(
                EntityManager,
                _prototype,
                _tileDef,
                _maps,
                _decals,
                _transform,
                _parallel,
                _entityTable,
                gridUid,
                grid,
                position,
                seed,
                _workerCount,
                cts.Token);

            var generator = new ParallelDungeonGenerator(context, Log);
            return await generator.GenerateAsync(config);
        }
        finally
        {
            _activeJobs.Remove(jobId);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Fire-and-forget dungeon generation.
    /// </summary>
    public void Generate(
        DungeonConfig config,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i position,
        int seed,
        EntityCoordinates? targetCoordinates = null)
    {
        _ = GenerateAsync(config, gridUid, grid, position, seed);
    }

    /// <summary>
    /// Cancels all active dungeon generation jobs.
    /// </summary>
    public void CancelAll()
    {
        foreach (var cts in _activeJobs.Values)
        {
            cts.Cancel();
        }
        _activeJobs.Clear();
    }
}

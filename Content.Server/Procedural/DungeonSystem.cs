using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Construction;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Content.Server.Decals;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using Content.Server._PS.Procedural; // Prospect

namespace Content.Server.Procedural;

public sealed partial class DungeonSystem : SharedDungeonSystem
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TileSystem _tile = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IMapManager _mapManager = default!; // Prospect
    [Dependency] private readonly ProspectDungeonSystem _prospectDungeon = default!; // Prospect

    private readonly List<(Vector2i, Tile)> _tiles = new();

    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private const double DungeonJobTime = 0.005;

    public const int CollisionMask = (int) CollisionGroup.Impassable;
    public const int CollisionLayer = (int) CollisionGroup.Impassable;

    private readonly JobQueue _dungeonJobQueue = new(DungeonJobTime);
    private readonly Dictionary<DungeonJob.DungeonJob, CancellationTokenSource> _dungeonJobs = new();

    public static readonly ProtoId<ContentTileDefinition> FallbackTileId = "FloorSteel";

    public override void Initialize()
    {
        base.Initialize();

        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _console.RegisterCommand("dungen", Loc.GetString("cmd-dungen-desc"), Loc.GetString("cmd-dungen-help"), GenerateDungeon, CompletionCallback);
        _console.RegisterCommand("dungen_preset_vis", Loc.GetString("cmd-dungen_preset_vis-desc"), Loc.GetString("cmd-dungen_preset_vis-help"), DungeonPresetVis, PresetCallback);
        _console.RegisterCommand("dungen_pack_vis", Loc.GetString("cmd-dungen_pack_vis-desc"), Loc.GetString("cmd-dungen_pack_vis-help"), DungeonPackVis, PackCallback);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(PrototypeReload);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _dungeonJobQueue.Process();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var token in _dungeonJobs.Values)
        {
            token.Cancel();
        }

        _dungeonJobs.Clear();
    }

    private void OnRoundStart(RoundStartingEvent ev)
    {
        var query = AllEntityQuery<DungeonAtlasTemplateComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }

        // Prospect: benchmark dungeon generation
        var benchmarkCount = _configManager.GetCVar(CCVars.ProspectDungeonBenchmark);
        if (benchmarkCount > 0)
        {
            RunBenchmark(benchmarkCount);
        }
        // Prospect: End

        if (!_configManager.GetCVar(CCVars.ProcgenPreload))
            return;

        // Force all templates to be setup.
        foreach (var room in _prototype.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            GetOrCreateTemplate(room);
        }
    }

    // Prospect: benchmark dungeon generation
    private async void RunBenchmark(int count)
    {
        Log.Info($"[Benchmark] Starting dungeon generation benchmark ({count} dungeons)...");

        // Get a dungeon config to test with
        if (!_prototype.TryIndex<DungeonConfigPrototype>("Experiment", out var config))
        {
            Log.Warning("[Benchmark] Could not find 'Experiment' dungeon config for benchmark");
            return;
        }

        // Create a temporary map for benchmarking
        _maps.CreateMap(out var mapId);
        var gridEnt = _mapManager.CreateGridEntity(mapId);
        var grid = Comp<MapGridComponent>(gridEnt);

        var totalSw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var seed = i + 1;
            var position = new Vector2i(i * 100, 0); // Offset each dungeon

            try
            {
                await GenerateDungeonAsync(config, gridEnt, grid, position, seed);
            }
            catch (Exception ex)
            {
                Log.Error($"[Benchmark] Dungeon {i + 1} failed: {ex.Message}");
            }
        }

        totalSw.Stop();
        Log.Info($"[Benchmark] Completed {count} dungeons in {totalSw.ElapsedMilliseconds}ms (avg: {totalSw.ElapsedMilliseconds / count}ms per dungeon)");

        // Clean up the benchmark map
        _maps.DeleteMap(mapId);
    }
    // Prospect: End

    public override void Shutdown()
    {
        base.Shutdown();
        foreach (var token in _dungeonJobs.Values)
        {
            token.Cancel();
        }

        _dungeonJobs.Clear();
    }

    private void PrototypeReload(PrototypesReloadedEventArgs obj)
    {
        if (!obj.ByType.TryGetValue(typeof(DungeonRoomPrototype), out var rooms))
        {
            return;
        }

        foreach (var proto in rooms.Modified.Values)
        {
            var roomProto = (DungeonRoomPrototype) proto;
            var query = AllEntityQuery<DungeonAtlasTemplateComponent>();

            while (query.MoveNext(out var uid, out var comp))
            {
                if (!roomProto.AtlasPath.Equals(comp.Path))
                    continue;

                QueueDel(uid);
                break;
            }
        }

        if (!_configManager.GetCVar(CCVars.ProcgenPreload))
            return;

        foreach (var proto in rooms.Modified.Values)
        {
            var roomProto = (DungeonRoomPrototype) proto;
            var query = AllEntityQuery<DungeonAtlasTemplateComponent>();
            var found = false;

            while (query.MoveNext(out var comp))
            {
                if (!roomProto.AtlasPath.Equals(comp.Path))
                    continue;

                found = true;
                break;
            }

            if (!found)
            {
                GetOrCreateTemplate(roomProto);
            }
        }
    }

    public MapId GetOrCreateTemplate(DungeonRoomPrototype proto)
    {
        var query = AllEntityQuery<DungeonAtlasTemplateComponent>();
        DungeonAtlasTemplateComponent? comp;

        while (query.MoveNext(out var uid, out comp))
        {
            // Exists
            if (comp.Path.Equals(proto.AtlasPath))
                return Transform(uid).MapID;
        }

        var opts = new MapLoadOptions
        {
            DeserializationOptions = DeserializationOptions.Default with {PauseMaps = true},
            ExpectedCategory = FileCategory.Map
        };

        if (!_loader.TryLoadGeneric(proto.AtlasPath, out var res, opts) || !res.Maps.TryFirstOrNull(out var map))
            throw new Exception($"Failed to load dungeon template.");

        comp = AddComp<DungeonAtlasTemplateComponent>(map.Value.Owner);
        comp.Path = proto.AtlasPath;
        return map.Value.Comp.MapId;
    }

    /// <summary>
    /// Generates a dungeon in the background with the specified config.
    /// </summary>
    /// <param name="coordinates">Coordinates to move the dungeon to afterwards. Will delete the original map</param>
    public void GenerateDungeon(DungeonConfig gen,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i position,
        int seed,
        EntityCoordinates? coordinates = null)
    {
        // Prospect: parallel dungeon generation
        if (_prospectDungeon.Enabled)
        {
            _prospectDungeon.Generate(gen, gridUid, grid, position, seed, coordinates);
            return;
        }
        // Prospect: End

        var cancelToken = new CancellationTokenSource();
        var job = new DungeonJob.DungeonJob(
            Log,
            DungeonJobTime,
            EntityManager,
            _prototype,
            _tileDefManager,
            _anchorable,
            _decals,
            this,
            _lookup,
            _tile,
            _turf,
            _transform,
            gen,
            grid,
            gridUid,
            seed,
            position,
            coordinates,
            cancelToken.Token);

        _dungeonJobs.Add(job, cancelToken);
        _dungeonJobQueue.EnqueueJob(job);
    }

    public async Task<List<Dungeon>> GenerateDungeonAsync(
        DungeonConfig gen,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i position,
        int seed)
    {
        // Prospect: parallel dungeon generation
        if (_prospectDungeon.Enabled)
        {
            return await _prospectDungeon.GenerateAsync(gen, gridUid, grid, position, seed);
        }
        // Prospect: End

        // Prospect: stats logging
        var sw = Stopwatch.StartNew();
        // Prospect: End

        var cancelToken = new CancellationTokenSource();
        var job = new DungeonJob.DungeonJob(
            Log,
            DungeonJobTime,
            EntityManager,
            _prototype,
            _tileDefManager,
            _anchorable,
            _decals,
            this,
            _lookup,
            _tile,
            _turf,
            _transform,
            gen,
            grid,
            gridUid,
            seed,
            position,
            null,
            cancelToken.Token);

        _dungeonJobs.Add(job, cancelToken);
        _dungeonJobQueue.EnqueueJob(job);
        await job.AsTask;

        if (job.Exception != null)
        {
            throw job.Exception;
        }

        // Prospect: stats logging
        sw.Stop();
        var dungeons = job.Result!;
        var totalTiles = 0;
        foreach (var dungeon in dungeons)
            totalTiles += dungeon.AllTiles.Count;

        var line = $"{DateTime.Now:O}|{sw.ElapsedMilliseconds}ms|dungeons:{dungeons.Count}|tiles:{totalTiles}|seed:{seed}|system:upstream";
        File.AppendAllText("dungeon_stats.log", line + Environment.NewLine);
        Log.Info($"[Upstream] Dungeon generated in {sw.ElapsedMilliseconds}ms ({dungeons.Count} dungeons, {totalTiles} tiles)");
        // Prospect: End

        return dungeons;
    }

    public Angle GetDungeonRotation(int seed)
    {
        // Mask 0 | 1 for rotation seed
        var dungeonRotationSeed = 3 & seed;
        return Math.PI / 2 * dungeonRotationSeed;
    }
}

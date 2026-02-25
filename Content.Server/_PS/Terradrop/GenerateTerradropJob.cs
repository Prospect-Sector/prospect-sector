using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Parallax;
using Content.Server.Procedural;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Shared._PS.Terradrop;
using Content.Shared.Atmos;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.Loot;
using Content.Shared.Random;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Robust.Shared.Collections;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._PS.Terradrop;

public sealed class GenerateTerradropJob : Job<bool>
{
    private readonly IEntityManager _entManager;
    private readonly IGameTiming _timing;
    private readonly IPrototypeManager _prototypeManager;
    private readonly AnchorableSystem _anchorable;
    private readonly BiomeSystem _biome;
    private readonly DungeonSystem _dungeon;
    private readonly MetaDataSystem _metaData;
    private readonly SharedMapSystem _map;

    public readonly EntityUid Station;
    public readonly EntityUid TargetPad;
    public readonly TerradropMapPrototype MapPrototype;
    public MapId MapId;
    public EntityUid MapUid;
    private readonly SalvageMissionParams _missionParams;
    private readonly int _level;

    private readonly ISawmill _sawmill;

    private readonly Tile _padTile;

    public GenerateTerradropJob(
        double maxTime,
        IEntityManager entManager,
        IGameTiming timing,
        ILogManager logManager,
        IPrototypeManager protoManager,
        AnchorableSystem anchorable,
        BiomeSystem biome,
        DungeonSystem dungeon,
        MetaDataSystem metaData,
        SharedMapSystem map,
        EntityUid station,
        EntityUid targetPad,
        TerradropMapPrototype terradropMapPrototype,
        SalvageMissionParams missionParams,
        Tile padTile,
        int level,
        CancellationToken cancellation = default) : base(maxTime, cancellation)
    {
        _entManager = entManager;
        _timing = timing;
        _prototypeManager = protoManager;
        _anchorable = anchorable;
        _biome = biome;
        _dungeon = dungeon;
        _metaData = metaData;
        _map = map;
        Station = station;
        TargetPad = targetPad;
        MapPrototype = terradropMapPrototype;
        _missionParams = missionParams;
        _padTile = padTile;
        _level = level;
        _sawmill = logManager.GetSawmill("salvage_job");
#if !DEBUG
        _sawmill.Level = LogLevel.Info;
#endif
    }

    protected override async Task<bool> Process()
    {
        _sawmill.Debug("terradrop", $"Spawning terradrop mission with seed {_missionParams.Seed}");
        MapUid = _map.CreateMap(out var mapId, runMapInit: false);
        MapId = mapId;
        MetaDataComponent? metadata = null;
        var grid = _entManager.EnsureComponent<MapGridComponent>(MapUid);
        var random = new Random(_missionParams.Seed);

        _metaData.SetEntityName(
            MapUid,
            _entManager.System<SharedSalvageSystem>()
                .GetFTLName(_prototypeManager.Index(SalvageSystem.PlanetNames), _missionParams.Seed));
        _entManager.AddComponent<FTLBeaconComponent>(MapUid);

        // Setup mission configs
        // As we go through the config the rating will deplete so we'll go for most important to least important.
        var difficultyProto = _prototypeManager.Index(MapPrototype.SalvageDifficulty);

        var mission = _entManager.System<SharedSalvageSystem>()
            .GetMission(difficultyProto, _missionParams.Seed);

        var missionBiome = _prototypeManager.Index<SalvageBiomeModPrototype>(mission.Biome);

        if (missionBiome.BiomePrototype != null)
        {
            var biome = _entManager.AddComponent<BiomeComponent>(MapUid);
            var biomeSystem = _entManager.System<BiomeSystem>();
            biomeSystem.SetTemplate(MapUid,
                biome,
                _prototypeManager.Index<BiomeTemplatePrototype>(MapPrototype.Biome));
            biomeSystem.SetSeed(MapUid, biome, mission.Seed);
            _entManager.Dirty(MapUid, biome);

            // Gravity
            var gravity = _entManager.EnsureComponent<GravityComponent>(MapUid);
            gravity.Enabled = true;
            _entManager.Dirty(MapUid, gravity, metadata);

            // Atmos
            var atmosphere = _prototypeManager.Index(MapPrototype.Atmosphere);
            // copy into a new array since the yml deserialization discards the fixed length
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            atmosphere.Gases.CopyTo(moles, 0);
            var atmos = _entManager.EnsureComponent<MapAtmosphereComponent>(MapUid);

            // If the proto cannot be found use RoomTemp as a fallback.
            var temperature = _prototypeManager.TryIndex(MapPrototype.Temperature, out var tempProto)
                ? tempProto
                : _prototypeManager.Index<SalvageTemperatureMod>("RoomTemp");
            _entManager.System<AtmosphereSystem>().SetMapSpace(MapUid, atmosphere.Space, atmos);
            _entManager.System<AtmosphereSystem>()
                .SetMapGasMixture(MapUid, new GasMixture(moles, temperature.Temperature), atmos);

            if (mission.Color != null)
            {
                var lighting = _entManager.EnsureComponent<MapLightComponent>(MapUid);
                lighting.AmbientLightColor = mission.Color.Value;
                _entManager.Dirty(MapUid, lighting);
            }
        }

        _map.InitializeMap(mapId);
        //_map.SetPaused(MapUid, true);

        // Setup the map info for terradrop
        var terradropMapComponent = _entManager.EnsureComponent<TerradropMapComponent>(MapUid);
        terradropMapComponent.StationUid = Station;
        terradropMapComponent.MapPrototype = MapPrototype;
        terradropMapComponent.Level = _level;

        // Setup expedition
        var expedition = _entManager.AddComponent<SalvageExpeditionComponent>(MapUid);
        expedition.Station = Station;
        expedition.EndTime = _timing.CurTime + mission.Duration;
        expedition.MissionParams = _missionParams;

        var landingPadRadius = 6;
        var minDungeonOffset = landingPadRadius + 4;

        // We'll use the dungeon rotation as the spawn angle
        var dungeonRotation = _dungeon.GetDungeonRotation(_missionParams.Seed);

        var maxDungeonOffset = minDungeonOffset + 12;
        var dungeonOffsetDistance = minDungeonOffset + (maxDungeonOffset - minDungeonOffset) * random.NextFloat();
        var dungeonOffset = new Vector2(0f, dungeonOffsetDistance);
        dungeonOffset = dungeonRotation.RotateVec(dungeonOffset);

        // Use PS-specific dungeon configs instead of upstream ones with mapped items
        // This avoids spawning illegal weapons/armor from prefab rooms
        var biomeId = MapPrototype.Biome.Id;
        var psDungeonId = biomeId switch
        {
            "Caves" => "PSDungeonCaves",
            "Lava" => "PSDungeonLava",
            "Snow" => "PSDungeonSnow",
            "Grasslands" => "PSDungeonGeneric",
            "LowDesert" => "PSDungeonCaves",
            "Volcanic" => "PSDungeonLava",
            "Continental" => "PSDungeonLab",
            "Shadow" => "PSDungeonHaunted",
            _ => null
        };

        if (psDungeonId == null)
        {
            _sawmill.Warning($"Unknown biome '{biomeId}' for terradrop dungeon, falling back to PSDungeonGeneric");
            psDungeonId = "PSDungeonGeneric";
        }
        var dungeonConfig = _prototypeManager.Index<DungeonConfigPrototype>(psDungeonId);
        var dungeons = await WaitAsyncTask(
            _dungeon.GenerateDungeonAsync(
                dungeonConfig,
                MapUid,
                grid,
                (Vector2i)dungeonOffset,
                _missionParams.Seed
            )
        );

        var dungeon = dungeons.First();

        // PS dungeons use procedural generation without rooms, so we check for tiles instead
        if (dungeon.Rooms.Count == 0 && dungeon.AllTiles.Count == 0)
        {
            _sawmill.Warning("Dungeon generation produced no rooms or tiles, aborting");
            return false;
        }

        // If we have tiles but no rooms, create a pseudo-room from corridor tiles for loot spawning
        if (dungeon.Rooms.Count == 0 && dungeon.AllTiles.Count > 0)
        {
            _sawmill.Debug($"Dungeon has {dungeon.AllTiles.Count} tiles but no rooms, creating pseudo-room for spawning");

            // Use corridor tiles, or all tiles if no corridors
            var spawnTiles = dungeon.CorridorTiles.Count > 0
                ? dungeon.CorridorTiles.ToHashSet()
                : dungeon.AllTiles.ToHashSet();

            // Calculate bounds and center
            var minX = spawnTiles.Min(t => t.X);
            var maxX = spawnTiles.Max(t => t.X);
            var minY = spawnTiles.Min(t => t.Y);
            var maxY = spawnTiles.Max(t => t.Y);
            var bounds = new Box2i(minX, minY, maxX, maxY);
            var center = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);

            var pseudoRoom = new DungeonRoom(spawnTiles, center, bounds, new HashSet<Vector2i>());
            dungeon.AddRoom(pseudoRoom);
        }

        expedition.DungeonLocation = dungeonOffset;

        List<Vector2i> reservedTiles = new();

        // Setup the landing pad
        var landingPadExtents = new Vector2i(landingPadRadius, landingPadRadius);
        var tiles = new List<(Vector2i Indices, Tile Tile)>(landingPadExtents.X * landingPadExtents.Y * 2);

        // Set the tiles themselves
        var landingTile = _padTile;

        foreach (var tile in _map.GetTilesIntersecting(MapUid, grid, new Circle(Vector2.Zero, landingPadRadius), false))
        {
            if (!_biome.TryGetBiomeTile(MapUid, grid, tile.GridIndices, out _))
                continue;

            tiles.Add((tile.GridIndices, landingTile));
            reservedTiles.Add(tile.GridIndices);
        }

        grid.SetTiles(tiles);

        var budgetEntries = new List<IBudgetEntry>();

        /*
         * GUARANTEED LOOT
         */

        // We'll always add this loot if possible
        // mainly used for ore layers.
        foreach (var lootProto in _prototypeManager.EnumeratePrototypes<SalvageLootPrototype>())
        {
            if (!lootProto.Guaranteed)
                continue;

            try
            {
                await SpawnDungeonLoot(lootProto, MapUid);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to spawn guaranteed loot {lootProto.ID}: {e}");
            }
        }

        // Mob spawns

        var mobBudget = difficultyProto.MobBudget;
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var randomSystem = _entManager.System<RandomSystem>();

        foreach (var entry in faction.MobGroups)
        {
            budgetEntries.Add(entry);
        }

        var probSum = budgetEntries.Sum(x => x.Prob);

        while (mobBudget > 0f)
        {
            var entry = randomSystem.GetBudgetEntry(ref mobBudget, ref probSum, budgetEntries, random);
            if (entry == null)
                break;

            try
            {
                await SpawnRandomEntry((MapUid, grid), entry, dungeon, random);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to spawn mobs for {entry.Proto}: {e}");
            }
        }

        // Spawn PS equipment loot with level-scaled stats
        var psLootBudget = difficultyProto.LootBudget;
        if (_prototypeManager.TryIndex<SalvageLootPrototype>("PSSalvageLoot", out var psLoot))
        {
            foreach (var rule in psLoot.LootRules)
            {
                switch (rule)
                {
                    case RandomSpawnsLoot randomLoot:
                        budgetEntries.Clear();

                        foreach (var entry in randomLoot.Entries)
                        {
                            budgetEntries.Add(entry);
                        }

                        probSum = budgetEntries.Sum(x => x.Prob);

                        while (psLootBudget > 0f)
                        {
                            var entry = randomSystem.GetBudgetEntry(ref psLootBudget, ref probSum, budgetEntries, random);
                            if (entry == null)
                                break;

                            _sawmill.Debug($"Spawning PS loot {entry.Proto} (level {_level})");
                            await SpawnRandomEntry((MapUid, grid), entry, dungeon, random);
                        }

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        return true;
    }

    private async Task SpawnRandomEntry(Entity<MapGridComponent> grid,
        IBudgetEntry entry,
        Dungeon dungeon,
        Random random)
    {
        await SuspendIfOutOfTime();

        var availableRooms = new ValueList<DungeonRoom>(dungeon.Rooms);
        var availableTiles = new List<Vector2i>();

        while (availableRooms.Count > 0)
        {
            availableTiles.Clear();
            var roomIndex = random.Next(availableRooms.Count);
            var room = availableRooms.RemoveSwap(roomIndex);
            availableTiles.AddRange(room.Tiles);

            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree(grid,
                        tile,
                        (int)CollisionGroup.MachineLayer,
                        (int)CollisionGroup.MachineLayer))
                {
                    continue;
                }

                var uid = _entManager.SpawnAtPosition(entry.Proto, _map.GridTileToLocal(grid, grid, tile));
                _entManager.RemoveComponent<GhostRoleComponent>(uid);
                _entManager.RemoveComponent<GhostTakeoverAvailableComponent>(uid);

                // Scale mob stats based on terradrop level
                _entManager.EnsureComponent<TerradropMobComponent>(uid);

                return;
            }
        }

        // oh noooooooooooo
    }

    private async Task SpawnDungeonLoot(SalvageLootPrototype loot, EntityUid gridUid)
    {
        for (var i = 0; i < loot.LootRules.Count; i++)
        {
            var rule = loot.LootRules[i];

            switch (rule)
            {
                case BiomeMarkerLoot biomeLoot:
                {
                    if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
                    {
                        _biome.AddMarkerLayer(gridUid, biome, biomeLoot.Prototype);
                    }
                }
                    break;
                case BiomeTemplateLoot biomeLoot:
                {
                    if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
                    {
                        _biome.AddTemplate(gridUid,
                            biome,
                            "Loot",
                            _prototypeManager.Index<BiomeTemplatePrototype>(biomeLoot.Prototype),
                            i);
                    }
                }
                    break;
            }
        }
    }
}

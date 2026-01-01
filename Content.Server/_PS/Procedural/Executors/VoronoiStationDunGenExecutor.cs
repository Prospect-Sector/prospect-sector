using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._PS.Procedural.Generation;
using Content.Server._PS.Procedural.StationGeneration;
using Content.Shared._PS.Procedural.StationGeneration;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.Executors;

/// <summary>
/// Executor for VoronoiStationDunGen - generates stations using Voronoi zones.
/// </summary>
public sealed class VoronoiStationDunGenExecutor : LayerExecutorBase<VoronoiStationDunGen>
{
    private readonly ISawmill _log;

    public VoronoiStationDunGenExecutor(DungeonGenerationContext context, ISawmill log) : base(context)
    {
        _log = log;
    }

    protected override async Task ExecuteAsync(
        VoronoiStationDunGen layer,
        Dungeon dungeon,
        Vector2i position,
        Random random)
    {
        if (layer.Departments.Count == 0)
        {
            _log.Warning("VoronoiStationDunGen has no departments configured");
            return;
        }

        _log.Debug($"Starting Voronoi station generation with {layer.Departments.Count} departments");

        // Phase 1: Generate Voronoi zones
        var zoneGenerator = new VoronoiZoneGenerator();
        var zones = zoneGenerator.GenerateZones(
            layer.Departments.Count,
            layer.StationRadius,
            position,
            layer.MinDepartmentSpacing,
            random);

        _log.Debug($"Generated {zones.Count} zones");

        // Smooth zone boundaries for cleaner room placement
        zoneGenerator.SmoothZoneBoundaries(zones, 2);

        Context.Cancellation.ThrowIfCancellationRequested();

        // Phase 2: Assign departments to zones
        AssignDepartments(zones, layer, random);

        // Phase 3: Place rooms in each zone
        var roomPlacer = new ZoneRoomPlacer(Context.Prototype);
        foreach (var zone in zones)
        {
            Context.Cancellation.ThrowIfCancellationRequested();

            if (zone.Department == null)
                continue;

            var deptProto = Context.Prototype.Index(zone.Department.Value);
            roomPlacer.PlaceRoomsInZone(zone, deptProto, layer.RoomGap, random);
            _log.Debug($"Placed {zone.Rooms.Count} rooms in zone {zone.Id} ({deptProto.ID})");
        }

        // Phase 4: Generate hallways between rooms
        // Only reserve room INTERIOR tiles - walls can be adjacent to hallways
        var reservedTiles = ZoneRoomPlacer.GetAllRoomTiles(zones);

        var hallwayGenerator = new HallwayGenerator();
        var hallwayResult = hallwayGenerator.GenerateHallways(
            zones,
            reservedTiles,
            layer.HallwayWidth,
            layer.AddRedundantHallways,
            layer.RedundantHallwayChance,
            random);

        _log.Debug($"Generated {hallwayResult.Segments.Count} hallway segments with {hallwayResult.Tiles.Count} tiles");

        Context.Cancellation.ThrowIfCancellationRequested();

        // Phase 5: Maints tunnels (disabled for now - hallways connect rooms directly)
        // TODO: Re-enable maints for back-of-room maintenance access
        // var allTunnelResult = new MaintsResult();
        // var shaftGenerator = new MaintsGenerator();
        // foreach (var zone in zones)
        // {
        //     Context.Cancellation.ThrowIfCancellationRequested();
        //     var tunnelResult = shaftGenerator.GenerateTunnels(zone, hallwayResult.Tiles, random);
        //     allTunnelResult.TunnelTiles.UnionWith(tunnelResult.TunnelTiles);
        //     allTunnelResult.DoorPlacements.AddRange(tunnelResult.DoorPlacements);
        // }
        // _log.Debug($"Generated {allTunnelResult.TunnelTiles.Count} maints tiles");

        // Add door positions to dungeon.Entrances so wall generation skips them
        foreach (var door in hallwayResult.DoorPlacements)
        {
            dungeon.Entrances.Add(door.Position);
        }

        // Phase 6: Queue all tile and entity commands
        await QueueTileCommands(zones, hallwayResult, layer);
        await QueueEntityCommands(zones, hallwayResult, layer);

        // Add dungeon data
        foreach (var zone in zones)
        {
            foreach (var room in zone.Rooms)
            {
                dungeon.RoomTiles.UnionWith(room.Tiles);
                dungeon.RoomExteriorTiles.UnionWith(room.WallTiles);
            }
        }

        dungeon.CorridorTiles.UnionWith(hallwayResult.Tiles);

        _log.Info($"Voronoi station generation complete: {dungeon.RoomTiles.Count} room tiles, " +
                  $"{dungeon.CorridorTiles.Count} corridor tiles");
    }

    /// <summary>
    /// Assigns department prototypes to zones based on priority.
    /// </summary>
    private void AssignDepartments(List<StationZone> zones, VoronoiStationDunGen layer, Random random)
    {
        // Get department prototypes
        var departments = new List<StationDepartmentPrototype>();
        foreach (var deptId in layer.Departments)
        {
            if (Context.Prototype.TryIndex(deptId, out var dept))
            {
                departments.Add(dept);
            }
        }

        // Sort zones by distance from center (closest first for high priority departments)
        var sortedZones = zones
            .OrderBy(z => z.Center.LengthSquared())
            .ToList();

        // Sort departments by priority (highest first)
        var sortedDepts = departments
            .OrderByDescending(d => d.Priority)
            .ToList();

        // Assign departments to zones
        for (var i = 0; i < sortedZones.Count && i < sortedDepts.Count; i++)
        {
            sortedZones[i].Department = sortedDepts[i].ID;
        }
    }

    /// <summary>
    /// Queues tile placement commands.
    /// </summary>
    private Task QueueTileCommands(
        List<StationZone> zones,
        HallwayResult hallways,
        VoronoiStationDunGen layer)
    {
        var floorTileDef = (ContentTileDefinition)Context.TileDef[layer.FloorTile];

        // Queue room tiles and spawn room contents
        foreach (var zone in zones)
        {
            foreach (var room in zone.Rooms)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                // Queue room spawn command to load room from atlas
                Context.RoomSpawnCommands.Enqueue(new RoomSpawnCommand(room.Prototype, room.Transform));
            }
        }

        // Queue hallway floor tiles
        var floorTile = new Tile(floorTileDef.TileId);
        foreach (var tile in hallways.Tiles)
        {
            Context.Cancellation.ThrowIfCancellationRequested();
            QueueTile(tile, floorTile);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Queues entity spawn commands (walls, doors).
    /// </summary>
    private Task QueueEntityCommands(
        List<StationZone> zones,
        HallwayResult hallways,
        VoronoiStationDunGen layer)
    {
        // Collect all door positions so we don't place walls there
        var doorPositions = new HashSet<Vector2i>();
        foreach (var door in hallways.DoorPlacements)
        {
            doorPositions.Add(door.Position);
        }

        // Queue hallway walls (excluding door positions)
        foreach (var segment in hallways.Segments)
        {
            foreach (var wallTile in segment.WallTiles)
            {
                Context.Cancellation.ThrowIfCancellationRequested();

                // Don't place walls where doors go
                if (doorPositions.Contains(wallTile))
                    continue;

                QueueEntity(layer.WallPrototype, wallTile);
            }
        }

        // Queue hallway doors and schedule wall clearing at door positions
        var processedDoors = new HashSet<Vector2i>();
        foreach (var doorPlacement in hallways.DoorPlacements)
        {
            // Avoid duplicate doors at same position
            if (!processedDoors.Add(doorPlacement.Position))
                continue;

            // Queue position for clearing after room spawns (removes walls from room prototypes)
            Context.DoorClearPositions.Enqueue(doorPlacement.Position);

            QueueEntity(layer.HallwayDoorPrototype, doorPlacement.Position, doorPlacement.Rotation);
        }

        return Task.CompletedTask;
    }
}

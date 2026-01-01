using System.Numerics;
using Content.Shared._PS.Procedural.StationGeneration;
using Content.Shared.Procedural;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Procedural.StationGeneration;

/// <summary>
/// Places rooms within Voronoi zones with gaps for maints tunnels.
/// </summary>
public sealed class ZoneRoomPlacer
{
    private readonly IPrototypeManager _prototype;

    public ZoneRoomPlacer(IPrototypeManager prototype)
    {
        _prototype = prototype;
    }

    /// <summary>
    /// Places rooms in a zone based on its department configuration.
    /// </summary>
    /// <param name="zone">The zone to place rooms in</param>
    /// <param name="department">Department prototype for room selection</param>
    /// <param name="roomGap">Gap between room walls for maints tunnels</param>
    /// <param name="random">Random number generator</param>
    public void PlaceRoomsInZone(
        StationZone zone,
        StationDepartmentPrototype department,
        int roomGap,
        Random random)
    {
        // Get available rooms for this department
        var availableRooms = GetRoomsForDepartment(department);
        if (availableRooms.Count == 0)
            return;

        // Determine how many rooms to place
        var roomCount = random.Next(department.MinRooms, department.MaxRooms + 1);

        // Calculate zone bounding box for placement attempts
        var zoneBounds = CalculateZoneBounds(zone);

        for (var i = 0; i < roomCount; i++)
        {
            // Select a random room prototype
            var roomProto = availableRooms[random.Next(availableRooms.Count)];

            // Try to find a valid placement
            if (TryPlaceRoom(zone, roomProto, zoneBounds, roomGap, random, out var placement))
            {
                zone.Rooms.Add(placement);
                ReserveRoomTiles(zone, placement, roomGap);
            }
        }
    }

    /// <summary>
    /// Gets all room prototypes that match the department's criteria.
    /// </summary>
    private List<DungeonRoomPrototype> GetRoomsForDepartment(StationDepartmentPrototype department)
    {
        var rooms = new List<DungeonRoomPrototype>();

        // If specific presets are defined, use those
        if (department.RoomPresets.Count > 0)
        {
            foreach (var presetId in department.RoomPresets)
            {
                if (_prototype.TryIndex(presetId, out var preset))
                {
                    rooms.Add(preset);
                }
            }
            return rooms;
        }

        // Otherwise, filter by tags
        foreach (var proto in _prototype.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            if (department.RoomTags.Count == 0)
            {
                rooms.Add(proto);
                continue;
            }

            // Check if room has any of the department's tags
            foreach (var tag in department.RoomTags)
            {
                if (proto.Tags.Contains(tag))
                {
                    rooms.Add(proto);
                    break;
                }
            }
        }

        return rooms;
    }

    /// <summary>
    /// Calculates the bounding box of a zone's tiles.
    /// </summary>
    private Box2i CalculateZoneBounds(StationZone zone)
    {
        if (zone.Tiles.Count == 0)
            return Box2i.Empty;

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var tile in zone.Tiles)
        {
            minX = Math.Min(minX, tile.X);
            minY = Math.Min(minY, tile.Y);
            maxX = Math.Max(maxX, tile.X);
            maxY = Math.Max(maxY, tile.Y);
        }

        return new Box2i(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Attempts to place a room within the zone.
    /// </summary>
    private bool TryPlaceRoom(
        StationZone zone,
        DungeonRoomPrototype roomProto,
        Box2i zoneBounds,
        int roomGap,
        Random random,
        out PlacedRoom placement)
    {
        placement = new PlacedRoom { Prototype = roomProto };

        // Room dimensions including buffer
        var roomWidth = roomProto.Size.X;
        var roomHeight = roomProto.Size.Y;
        var totalWidth = roomWidth + roomGap * 2;
        var totalHeight = roomHeight + roomGap * 2;

        // Try positions near zone center first, then expand outward
        const int maxAttempts = 100;
        var zoneCenter = zone.Center;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Start near center and expand search radius with each attempt
            var searchRadius = 5 + (attempt * 2); // Starts small, grows larger

            // Random offset from zone center
            var offsetX = (random.NextDouble() - 0.5) * 2 * searchRadius;
            var offsetY = (random.NextDouble() - 0.5) * 2 * searchRadius;

            var x = (int)(zoneCenter.X + offsetX) - totalWidth / 2;
            var y = (int)(zoneCenter.Y + offsetY) - totalHeight / 2;

            // Clamp to zone bounds
            x = Math.Clamp(x, zoneBounds.Left, Math.Max(zoneBounds.Left, zoneBounds.Right - totalWidth));
            y = Math.Clamp(y, zoneBounds.Bottom, Math.Max(zoneBounds.Bottom, zoneBounds.Top - totalHeight));

            // Room tiles (excluding buffer)
            var roomTiles = new HashSet<Vector2i>();
            var wallTiles = new HashSet<Vector2i>();
            var bufferTiles = new HashSet<Vector2i>();

            var valid = true;

            // Check room tiles
            for (var rx = 0; rx < roomWidth && valid; rx++)
            {
                for (var ry = 0; ry < roomHeight && valid; ry++)
                {
                    var tile = new Vector2i(x + roomGap + rx, y + roomGap + ry);

                    if (!zone.AvailableTiles.Contains(tile))
                    {
                        valid = false;
                        break;
                    }

                    roomTiles.Add(tile);
                }
            }

            if (!valid)
                continue;

            // Calculate wall tiles (perimeter of room)
            for (var rx = -1; rx <= roomWidth; rx++)
            {
                for (var ry = -1; ry <= roomHeight; ry++)
                {
                    // Skip interior
                    if (rx >= 0 && rx < roomWidth && ry >= 0 && ry < roomHeight)
                        continue;

                    var tile = new Vector2i(x + roomGap + rx, y + roomGap + ry);
                    wallTiles.Add(tile);
                }
            }

            // Calculate buffer tiles (gap area for maints tunnels)
            for (var bx = -roomGap - 1; bx < roomWidth + roomGap + 1; bx++)
            {
                for (var by = -roomGap - 1; by < roomHeight + roomGap + 1; by++)
                {
                    // Skip room interior and immediate wall
                    if (bx >= -1 && bx <= roomWidth && by >= -1 && by <= roomHeight)
                        continue;

                    var tile = new Vector2i(x + roomGap + bx, y + roomGap + by);

                    // Only include if it's in the zone
                    if (zone.Tiles.Contains(tile))
                    {
                        bufferTiles.Add(tile);
                    }
                }
            }

            // Check that buffer tiles are available (not occupied by other rooms)
            foreach (var tile in wallTiles)
            {
                if (!zone.AvailableTiles.Contains(tile) && zone.Tiles.Contains(tile))
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            // Success - create placement
            placement.Tiles = roomTiles;
            placement.WallTiles = wallTiles;
            placement.BufferTiles = bufferTiles;
            placement.Bounds = new Box2i(
                x + roomGap,
                y + roomGap,
                x + roomGap + roomWidth,
                y + roomGap + roomHeight);

            // Transform needs to position room center at target center
            // The room's center in its local space (accounting for atlas offset)
            var roomCenter = new Vector2(
                (roomProto.Offset.X + roomProto.Size.X / 2f),
                (roomProto.Offset.Y + roomProto.Size.Y / 2f));

            // Target center in world space
            var targetCenter = new Vector2(
                x + roomGap + roomWidth / 2f,
                y + roomGap + roomHeight / 2f);

            // Create transform that moves room center to target center
            placement.Transform = Matrix3Helpers.CreateTransform(targetCenter, Angle.Zero);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Reserves tiles used by a placed room (removes from available tiles).
    /// </summary>
    private void ReserveRoomTiles(StationZone zone, PlacedRoom placement, int roomGap)
    {
        // Reserve room tiles
        foreach (var tile in placement.Tiles)
        {
            zone.AvailableTiles.Remove(tile);
        }

        // Reserve wall tiles
        foreach (var tile in placement.WallTiles)
        {
            zone.AvailableTiles.Remove(tile);
        }

        // Buffer tiles remain available for maints tunnels, but mark them
        // Note: We don't remove buffer tiles from AvailableTiles since maints tunnels can use them
    }

    /// <summary>
    /// Gets all floor tiles from all rooms in a zone.
    /// </summary>
    public static HashSet<Vector2i> GetAllRoomTiles(StationZone zone)
    {
        var tiles = new HashSet<Vector2i>();
        foreach (var room in zone.Rooms)
        {
            tiles.UnionWith(room.Tiles);
        }
        return tiles;
    }

    /// <summary>
    /// Gets all wall tiles from all rooms in a zone.
    /// </summary>
    public static HashSet<Vector2i> GetAllWallTiles(StationZone zone)
    {
        var tiles = new HashSet<Vector2i>();
        foreach (var room in zone.Rooms)
        {
            tiles.UnionWith(room.WallTiles);
        }
        return tiles;
    }

    /// <summary>
    /// Gets all floor tiles from all zones.
    /// </summary>
    public static HashSet<Vector2i> GetAllRoomTiles(IEnumerable<StationZone> zones)
    {
        var tiles = new HashSet<Vector2i>();
        foreach (var zone in zones)
        {
            tiles.UnionWith(GetAllRoomTiles(zone));
        }
        return tiles;
    }

    /// <summary>
    /// Gets all wall tiles from all zones.
    /// </summary>
    public static HashSet<Vector2i> GetAllWallTiles(IEnumerable<StationZone> zones)
    {
        var tiles = new HashSet<Vector2i>();
        foreach (var zone in zones)
        {
            tiles.UnionWith(GetAllWallTiles(zone));
        }
        return tiles;
    }
}

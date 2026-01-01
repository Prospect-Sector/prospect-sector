using Content.Shared._PS.Procedural.StationGeneration;
using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.StationGeneration;

/// <summary>
/// Generates 1-wide maints tunnels between rooms within a zone.
/// Tunnels stop when they reach hallway tiles.
/// </summary>
public sealed class MaintsGenerator
{
    private static readonly Vector2i[] CardinalDirections =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
    };

    /// <summary>
    /// Generates maints tunnels within a zone.
    /// </summary>
    /// <param name="zone">Zone containing rooms to connect</param>
    /// <param name="hallwayTiles">Hallway tiles that stop tunnel expansion</param>
    /// <param name="random">Random number generator</param>
    /// <returns>Result containing tunnel tiles and door placements</returns>
    public MaintsResult GenerateTunnels(
        StationZone zone,
        HashSet<Vector2i> hallwayTiles,
        Random random)
    {
        var result = new MaintsResult();

        if (zone.Rooms.Count == 0)
            return result;

        // Get gap tiles (tiles between room walls, not in hallways)
        var gapTiles = FindGapTiles(zone, hallwayTiles);

        // First: Connect rooms to nearby hallways for accessibility
        foreach (var room in zone.Rooms)
        {
            var hallwayConnection = FindRoomToHallwayPath(room, gapTiles, hallwayTiles);
            if (hallwayConnection != null && hallwayConnection.Count > 0)
            {
                result.TunnelTiles.UnionWith(hallwayConnection);
                AddRoomDoorPlacements(result, hallwayConnection, room);
            }
        }

        // Second: Connect adjacent room pairs
        if (zone.Rooms.Count >= 2)
        {
            var adjacentPairs = GetAdjacentRoomPairs(zone.Rooms, gapTiles);

            foreach (var (room1, room2) in adjacentPairs)
            {
                var tunnel = FindTunnelPath(room1, room2, gapTiles, hallwayTiles);
                if (tunnel != null && tunnel.Count > 0)
                {
                    result.TunnelTiles.UnionWith(tunnel);
                    AddDoorPlacements(result, tunnel, room1, room2);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Finds a path from a room to the nearest hallway tile.
    /// </summary>
    private HashSet<Vector2i>? FindRoomToHallwayPath(
        PlacedRoom room,
        HashSet<Vector2i> gapTiles,
        HashSet<Vector2i> hallwayTiles)
    {
        if (hallwayTiles.Count == 0)
            return null;

        // Find the closest hallway tile to the room
        var roomCenter = new Vector2i((int)room.Bounds.Center.X, (int)room.Bounds.Center.Y);
        Vector2i? closestHallway = null;
        var closestDist = float.MaxValue;

        foreach (var hallway in hallwayTiles)
        {
            var dist = DistanceVec2i(roomCenter, hallway);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestHallway = hallway;
            }
        }

        if (closestHallway == null || closestDist > 50) // Don't try to connect very far rooms
            return null;

        // Find closest wall tile that has a gap neighbor
        var startWall = FindClosestWallTile(room.WallTiles, closestHallway.Value, gapTiles);
        if (startWall == null)
            return null;

        // A* from wall to hallway through gap tiles
        return AStarToHallway(startWall.Value, closestHallway.Value, gapTiles, hallwayTiles);
    }

    /// <summary>
    /// A* pathfinding from a wall tile to a hallway tile.
    /// </summary>
    private HashSet<Vector2i>? AStarToHallway(
        Vector2i start,
        Vector2i goal,
        HashSet<Vector2i> gapTiles,
        HashSet<Vector2i> hallwayTiles)
    {
        var openSet = new PriorityQueue<Vector2i, float>();
        var cameFrom = new Dictionary<Vector2i, Vector2i>();
        var gScore = new Dictionary<Vector2i, float>();
        var fScore = new Dictionary<Vector2i, float>();

        // Start from the gap tile adjacent to start wall
        Vector2i? startGap = null;
        foreach (var dir in CardinalDirections)
        {
            var neighbor = start + dir;
            if (gapTiles.Contains(neighbor))
            {
                startGap = neighbor;
                break;
            }
        }

        if (startGap == null)
            return null;

        gScore[startGap.Value] = 0;
        fScore[startGap.Value] = Heuristic(startGap.Value, goal);
        openSet.Enqueue(startGap.Value, fScore[startGap.Value]);

        const int maxIterations = 2000;
        var iterations = 0;

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var current = openSet.Dequeue();

            // Check if we've reached a hallway tile
            if (hallwayTiles.Contains(current))
            {
                // Reconstruct path (excluding the hallway tile itself)
                var path = new HashSet<Vector2i>();
                var node = current;
                while (cameFrom.ContainsKey(node))
                {
                    node = cameFrom[node];
                    path.Add(node);
                }
                return path;
            }

            // Check if adjacent to hallway
            foreach (var dir in CardinalDirections)
            {
                if (hallwayTiles.Contains(current + dir))
                {
                    // Reconstruct path
                    var path = new HashSet<Vector2i> { current };
                    var node = current;
                    while (cameFrom.ContainsKey(node))
                    {
                        node = cameFrom[node];
                        path.Add(node);
                    }
                    return path;
                }
            }

            foreach (var dir in CardinalDirections)
            {
                var neighbor = current + dir;

                // Can move through gap tiles or to hallway tiles
                if (!gapTiles.Contains(neighbor) && !hallwayTiles.Contains(neighbor))
                    continue;

                var tentativeG = gScore[current] + 1f;

                if (!gScore.TryGetValue(neighbor, out var neighborG) || tentativeG < neighborG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(neighbor, fScore[neighbor]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Adds door placements for a single room connection.
    /// </summary>
    private void AddRoomDoorPlacements(MaintsResult result, HashSet<Vector2i> tunnel, PlacedRoom room)
    {
        foreach (var tunnelTile in tunnel)
        {
            foreach (var dir in CardinalDirections)
            {
                var neighbor = tunnelTile + dir;
                if (room.WallTiles.Contains(neighbor))
                {
                    result.DoorPlacements.Add(new DoorPlacement
                    {
                        Position = neighbor,
                        Rotation = GetDoorRotation(dir),
                        ConnectedRoom = room
                    });
                }
            }
        }
    }

    /// <summary>
    /// Finds gap tiles between room walls that can be used for tunnels.
    /// </summary>
    private HashSet<Vector2i> FindGapTiles(StationZone zone, HashSet<Vector2i> hallwayTiles)
    {
        var gapTiles = new HashSet<Vector2i>();

        // Get all room and wall tiles
        var roomTiles = new HashSet<Vector2i>();
        var wallTiles = new HashSet<Vector2i>();

        foreach (var room in zone.Rooms)
        {
            roomTiles.UnionWith(room.Tiles);
            wallTiles.UnionWith(room.WallTiles);
        }

        // Gap tiles are zone tiles that are:
        // - Not room interior tiles
        // - Not wall tiles
        // - Not hallway tiles
        foreach (var tile in zone.Tiles)
        {
            if (roomTiles.Contains(tile))
                continue;
            if (wallTiles.Contains(tile))
                continue;
            if (hallwayTiles.Contains(tile))
                continue;

            gapTiles.Add(tile);
        }

        return gapTiles;
    }

    /// <summary>
    /// Finds pairs of rooms that are adjacent (have gap tiles between them).
    /// </summary>
    private List<(PlacedRoom, PlacedRoom)> GetAdjacentRoomPairs(
        List<PlacedRoom> rooms,
        HashSet<Vector2i> gapTiles)
    {
        var pairs = new List<(PlacedRoom, PlacedRoom)>();
        var seen = new HashSet<(int, int)>();

        for (var i = 0; i < rooms.Count; i++)
        {
            for (var j = i + 1; j < rooms.Count; j++)
            {
                // Check if rooms are adjacent via gap tiles
                if (AreRoomsAdjacent(rooms[i], rooms[j], gapTiles))
                {
                    var pairKey = (Math.Min(i, j), Math.Max(i, j));
                    if (!seen.Contains(pairKey))
                    {
                        pairs.Add((rooms[i], rooms[j]));
                        seen.Add(pairKey);
                    }
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// Checks if two rooms are adjacent (can be connected via gap tiles).
    /// </summary>
    private bool AreRoomsAdjacent(PlacedRoom room1, PlacedRoom room2, HashSet<Vector2i> gapTiles)
    {
        // Check if any wall tile of room1 is adjacent to a gap tile
        // that is adjacent to a wall tile of room2
        foreach (var wall1 in room1.WallTiles)
        {
            foreach (var dir in CardinalDirections)
            {
                var neighbor = wall1 + dir;
                if (!gapTiles.Contains(neighbor))
                    continue;

                // Check if this gap tile leads to room2
                foreach (var dir2 in CardinalDirections)
                {
                    var neighbor2 = neighbor + dir2;
                    if (room2.WallTiles.Contains(neighbor2))
                        return true;
                }

                // Also check if there's a path through multiple gap tiles
                // For simplicity, check distance between room bounds
                var dist = GetRoomDistance(room1, room2);
                if (dist <= 3) // Close enough to potentially connect
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the minimum distance between two room bounds.
    /// </summary>
    private float GetRoomDistance(PlacedRoom room1, PlacedRoom room2)
    {
        var center1 = room1.Bounds.Center;
        var center2 = room2.Bounds.Center;

        // Simple center-to-center distance for now
        var dx = center1.X - center2.X;
        var dy = center1.Y - center2.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Finds a 1-wide tunnel path between two rooms.
    /// Path stops if it reaches a hallway tile.
    /// </summary>
    private HashSet<Vector2i>? FindTunnelPath(
        PlacedRoom from,
        PlacedRoom to,
        HashSet<Vector2i> gapTiles,
        HashSet<Vector2i> hallwayTiles)
    {
        // Find start and end points on room walls
        var toCenter = new Vector2i((int)to.Bounds.Center.X, (int)to.Bounds.Center.Y);
        var fromCenter = new Vector2i((int)from.Bounds.Center.X, (int)from.Bounds.Center.Y);
        var start = FindClosestWallTile(from.WallTiles, toCenter, gapTiles);
        var end = FindClosestWallTile(to.WallTiles, fromCenter, gapTiles);

        if (start == null || end == null)
            return null;

        // A* through gap tiles only, stopping at hallways
        return AStarTunnel(start.Value, end.Value, gapTiles, hallwayTiles);
    }

    /// <summary>
    /// Finds the wall tile closest to a target that has an adjacent gap tile.
    /// </summary>
    private Vector2i? FindClosestWallTile(
        HashSet<Vector2i> wallTiles,
        Vector2i target,
        HashSet<Vector2i> gapTiles)
    {
        Vector2i? best = null;
        var bestDist = float.MaxValue;

        foreach (var wall in wallTiles)
        {
            // Check if this wall has an adjacent gap tile
            var hasGapNeighbor = false;
            foreach (var dir in CardinalDirections)
            {
                if (gapTiles.Contains(wall + dir))
                {
                    hasGapNeighbor = true;
                    break;
                }
            }

            if (!hasGapNeighbor)
                continue;

            var dist = DistanceVec2i(wall, target);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = wall;
            }
        }

        return best;
    }

    /// <summary>
    /// A* pathfinding through gap tiles, stopping at hallways.
    /// </summary>
    private HashSet<Vector2i>? AStarTunnel(
        Vector2i start,
        Vector2i goal,
        HashSet<Vector2i> gapTiles,
        HashSet<Vector2i> hallwayTiles)
    {
        var openSet = new PriorityQueue<Vector2i, float>();
        var cameFrom = new Dictionary<Vector2i, Vector2i>();
        var gScore = new Dictionary<Vector2i, float>();
        var fScore = new Dictionary<Vector2i, float>();

        // Start from the gap tile adjacent to start wall
        Vector2i? startGap = null;
        foreach (var dir in CardinalDirections)
        {
            var neighbor = start + dir;
            if (gapTiles.Contains(neighbor))
            {
                startGap = neighbor;
                break;
            }
        }

        if (startGap == null)
            return null;

        gScore[startGap.Value] = 0;
        fScore[startGap.Value] = Heuristic(startGap.Value, goal);
        openSet.Enqueue(startGap.Value, fScore[startGap.Value]);

        const int maxIterations = 1000;
        var iterations = 0;

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var current = openSet.Dequeue();

            // Check if we've reached the goal (adjacent to goal wall)
            foreach (var dir in CardinalDirections)
            {
                if (current + dir == goal)
                {
                    // Reconstruct path
                    var path = new HashSet<Vector2i> { current };
                    var node = current;
                    while (cameFrom.ContainsKey(node))
                    {
                        node = cameFrom[node];
                        path.Add(node);
                    }
                    return path;
                }
            }

            // Stop if we hit a hallway (tunnel terminates)
            if (hallwayTiles.Contains(current))
            {
                // Reconstruct path up to this point
                var path = new HashSet<Vector2i> { current };
                var node = current;
                while (cameFrom.ContainsKey(node))
                {
                    node = cameFrom[node];
                    path.Add(node);
                }
                return path;
            }

            foreach (var dir in CardinalDirections)
            {
                var neighbor = current + dir;

                // Only move through gap tiles
                if (!gapTiles.Contains(neighbor) && !hallwayTiles.Contains(neighbor))
                    continue;

                // Prefer straight lines (penalize turns)
                var turnPenalty = 0f;
                if (cameFrom.TryGetValue(current, out var prev))
                {
                    var prevDir = current - prev;
                    if (prevDir != dir)
                        turnPenalty = 0.5f;
                }

                var tentativeG = gScore[current] + 1f + turnPenalty;

                if (!gScore.TryGetValue(neighbor, out var neighborG) || tentativeG < neighborG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue(neighbor, fScore[neighbor]);
                }
            }
        }

        return null; // No path found
    }

    private static float Heuristic(Vector2i a, Vector2i b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static float DistanceVec2i(Vector2i a, Vector2i b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Adds door placements where tunnels meet room walls.
    /// </summary>
    private void AddDoorPlacements(
        MaintsResult result,
        HashSet<Vector2i> tunnel,
        PlacedRoom room1,
        PlacedRoom room2)
    {
        // Find where tunnel meets room1 wall
        foreach (var tunnelTile in tunnel)
        {
            foreach (var dir in CardinalDirections)
            {
                var neighbor = tunnelTile + dir;

                if (room1.WallTiles.Contains(neighbor))
                {
                    result.DoorPlacements.Add(new DoorPlacement
                    {
                        Position = neighbor,
                        Rotation = GetDoorRotation(dir),
                        ConnectedRoom = room1
                    });
                }

                if (room2.WallTiles.Contains(neighbor))
                {
                    result.DoorPlacements.Add(new DoorPlacement
                    {
                        Position = neighbor,
                        Rotation = GetDoorRotation(dir),
                        ConnectedRoom = room2
                    });
                }
            }
        }
    }

    /// <summary>
    /// Gets the rotation for a door based on the direction it faces.
    /// </summary>
    private static Angle GetDoorRotation(Vector2i direction)
    {
        if (direction.X > 0) return Angle.FromDegrees(0);   // East
        if (direction.X < 0) return Angle.FromDegrees(180); // West
        if (direction.Y > 0) return Angle.FromDegrees(90);  // North
        return Angle.FromDegrees(270); // South
    }
}

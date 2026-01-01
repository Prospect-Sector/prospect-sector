using System.Linq;
using System.Numerics;
using Content.Shared._PS.Procedural.StationGeneration;
using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.StationGeneration;

/// <summary>
/// Generates hallways between department zones using MST and A* pathfinding.
/// Routes to 2 tiles in front of doors, avoids room intersections, prefers orthogonal paths.
/// </summary>
public sealed class HallwayGenerator
{
    private const int DoorOffset = 2; // How far from door the hallway path starts

    /// <summary>
    /// Generates hallways connecting all rooms across zones.
    /// Uses incremental routing - each room connects to the nearest point on the existing network.
    /// </summary>
    public HallwayResult GenerateHallways(
        List<StationZone> zones,
        HashSet<Vector2i> reservedTiles,
        int hallwayWidth,
        bool addRedundant,
        float redundantChance,
        Random random)
    {
        var result = new HallwayResult();

        // Collect all rooms from all zones
        var allRooms = new List<PlacedRoom>();
        foreach (var zone in zones)
        {
            allRooms.AddRange(zone.Rooms);
        }

        if (allRooms.Count < 2)
            return result;

        // Build complete set of blocked tiles (room interiors + walls)
        var blockedTiles = new HashSet<Vector2i>(reservedTiles);
        foreach (var room in allRooms)
        {
            blockedTiles.UnionWith(room.WallTiles);
        }

        // Sort rooms by distance from origin for consistent ordering
        allRooms.Sort((a, b) =>
            a.Bounds.Center.LengthSquared().CompareTo(b.Bounds.Center.LengthSquared()));

        // Track which rooms are connected to the hallway network
        var connectedRooms = new HashSet<PlacedRoom>();
        var unconnectedRooms = new List<PlacedRoom>();

        // Start with the first room - it's the seed of our network
        connectedRooms.Add(allRooms[0]);

        // Connect each remaining room to the nearest point on the network
        for (var i = 1; i < allRooms.Count; i++)
        {
            var room = allRooms[i];

            // Find the best connection point: nearest existing hallway tile OR nearest connected room
            var segment = RouteToNetwork(room, connectedRooms, result.Tiles, blockedTiles, hallwayWidth, result, allRooms);

            if (segment != null)
            {
                result.Segments.Add(segment);
                result.Tiles.UnionWith(segment.Tiles);
                connectedRooms.Add(room);
            }
            else
            {
                // Failed to connect - try again later
                unconnectedRooms.Add(room);
            }
        }

        // Second pass: try to connect unconnected rooms with relaxed constraints
        // Try connecting each unconnected room to ALL connected rooms until one works
        var stillUnconnected = new List<PlacedRoom>();
        foreach (var room in unconnectedRooms)
        {
            var connected = TryConnectToAnyRoom(room, connectedRooms, blockedTiles, result, hallwayWidth, allRooms);
            if (connected)
            {
                connectedRooms.Add(room);
            }
            else
            {
                stillUnconnected.Add(room);
            }
        }

        // Third pass: connect any remaining isolated rooms to each other, then bridge to main network
        if (stillUnconnected.Count > 0 && connectedRooms.Count > 0)
        {
            // Try to form a chain among unconnected rooms
            var isolatedCluster = new HashSet<PlacedRoom>();
            if (stillUnconnected.Count > 0)
            {
                isolatedCluster.Add(stillUnconnected[0]);

                for (var i = 1; i < stillUnconnected.Count; i++)
                {
                    var room = stillUnconnected[i];
                    var segment = TryConnectToAnyRoomInSet(room, isolatedCluster, blockedTiles, result, hallwayWidth, allRooms);
                    if (segment)
                    {
                        isolatedCluster.Add(room);
                    }
                }

                // Now try to bridge the isolated cluster to the main network
                foreach (var isolatedRoom in isolatedCluster)
                {
                    if (TryConnectToAnyRoom(isolatedRoom, connectedRooms, blockedTiles, result, hallwayWidth, allRooms))
                    {
                        // Successfully bridged - add all isolated rooms to connected set
                        foreach (var r in isolatedCluster)
                        {
                            connectedRooms.Add(r);
                        }
                        break;
                    }
                }
            }
        }

        // Optionally add redundant connections for loop paths
        if (addRedundant && allRooms.Count > 2)
        {
            var roomList = allRooms.ToList();
            for (var i = 0; i < roomList.Count; i++)
            {
                for (var j = i + 2; j < roomList.Count; j++) // Skip adjacent in order
                {
                    var room1 = roomList[i];
                    var room2 = roomList[j];
                    var dist = Vector2.Distance(room1.Bounds.Center, room2.Bounds.Center);

                    // Only consider nearby rooms not already well-connected
                    if (dist > 30)
                        continue;

                    if (random.NextDouble() > redundantChance)
                        continue;

                    // Route through existing network if possible
                    var segment = RouteRoomHallway(room1, room2, blockedTiles, result.Tiles, hallwayWidth, result, allRooms);
                    if (segment != null)
                    {
                        result.Segments.Add(segment);
                        result.Tiles.UnionWith(segment.Tiles);
                    }
                }
            }
        }

        // Final pass: remove walls that block hallway intersections
        CleanupIntersectionWalls(result);

        return result;
    }

    /// <summary>
    /// Removes walls from all segments where they would block hallway intersections.
    /// Only removes walls that block passage (hallway tiles on opposite sides), preserves corners.
    /// </summary>
    private void CleanupIntersectionWalls(HallwayResult result)
    {
        foreach (var segment in result.Segments)
        {
            var wallsToRemove = new List<Vector2i>();

            foreach (var wall in segment.WallTiles)
            {
                // Remove if the wall position is now a hallway tile
                if (result.Tiles.Contains(wall))
                {
                    wallsToRemove.Add(wall);
                    continue;
                }

                // Check if wall blocks passage: hallway tiles on OPPOSITE cardinal sides
                // This preserves corner walls while removing blocking walls
                var north = wall + new Vector2i(0, 1);
                var south = wall + new Vector2i(0, -1);
                var east = wall + new Vector2i(1, 0);
                var west = wall + new Vector2i(-1, 0);

                var hasNorth = result.Tiles.Contains(north);
                var hasSouth = result.Tiles.Contains(south);
                var hasEast = result.Tiles.Contains(east);
                var hasWest = result.Tiles.Contains(west);

                // Wall blocks if it has hallway tiles on opposite sides (N-S or E-W)
                // AND at least one side is from a different segment (intersection)
                var blocksNorthSouth = hasNorth && hasSouth;
                var blocksEastWest = hasEast && hasWest;

                if (blocksNorthSouth || blocksEastWest)
                {
                    // Verify it's actually at an intersection (tiles from different sources)
                    var northFromOther = hasNorth && !segment.Tiles.Contains(north);
                    var southFromOther = hasSouth && !segment.Tiles.Contains(south);
                    var eastFromOther = hasEast && !segment.Tiles.Contains(east);
                    var westFromOther = hasWest && !segment.Tiles.Contains(west);

                    if ((blocksNorthSouth && (northFromOther || southFromOther)) ||
                        (blocksEastWest && (eastFromOther || westFromOther)))
                    {
                        wallsToRemove.Add(wall);
                    }
                }
            }

            foreach (var wall in wallsToRemove)
            {
                segment.WallTiles.Remove(wall);
            }
        }
    }

    /// <summary>
    /// Tries to connect a room to any room in the connected set.
    /// Iterates through all connected rooms sorted by distance until one succeeds.
    /// </summary>
    private bool TryConnectToAnyRoom(
        PlacedRoom room,
        HashSet<PlacedRoom> connectedRooms,
        HashSet<Vector2i> blockedTiles,
        HallwayResult result,
        int hallwayWidth,
        List<PlacedRoom> allRooms)
    {
        // Sort connected rooms by distance to this room
        var sortedTargets = connectedRooms
            .OrderBy(r => Vector2.Distance(room.Bounds.Center, r.Bounds.Center))
            .ToList();

        foreach (var target in sortedTargets)
        {
            var segment = RouteRoomHallway(room, target, blockedTiles, result.Tiles, hallwayWidth, result, allRooms);
            if (segment != null)
            {
                result.Segments.Add(segment);
                result.Tiles.UnionWith(segment.Tiles);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries to connect a room to any room in a specific set (for isolated cluster building).
    /// </summary>
    private bool TryConnectToAnyRoomInSet(
        PlacedRoom room,
        HashSet<PlacedRoom> targetRooms,
        HashSet<Vector2i> blockedTiles,
        HallwayResult result,
        int hallwayWidth,
        List<PlacedRoom> allRooms)
    {
        var sortedTargets = targetRooms
            .OrderBy(r => Vector2.Distance(room.Bounds.Center, r.Bounds.Center))
            .ToList();

        foreach (var target in sortedTargets)
        {
            var segment = RouteRoomHallway(room, target, blockedTiles, result.Tiles, hallwayWidth, result, allRooms);
            if (segment != null)
            {
                result.Segments.Add(segment);
                result.Tiles.UnionWith(segment.Tiles);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Routes a room to the nearest point on the existing hallway network.
    /// Prefers connecting to existing hallways over creating new parallel paths.
    /// </summary>
    private HallwaySegment? RouteToNetwork(
        PlacedRoom room,
        HashSet<PlacedRoom> connectedRooms,
        HashSet<Vector2i> existingHallways,
        HashSet<Vector2i> blockedTiles,
        int width,
        HallwayResult result,
        List<PlacedRoom> allRooms)
    {
        // Find the best target: nearest existing hallway tile or nearest connected room
        var roomCenter = new Vector2i((int)room.Bounds.Center.X, (int)room.Bounds.Center.Y);

        // Option 1: Connect to nearest existing hallway tile
        Vector2i? nearestHallway = null;
        var nearestHallwayDist = float.MaxValue;

        foreach (var tile in existingHallways)
        {
            var dist = DistanceVec2i(roomCenter, tile);
            if (dist < nearestHallwayDist)
            {
                nearestHallwayDist = dist;
                nearestHallway = tile;
            }
        }

        // Option 2: Connect to nearest connected room
        PlacedRoom? nearestRoom = null;
        var nearestRoomDist = float.MaxValue;

        foreach (var connected in connectedRooms)
        {
            var dist = Vector2.Distance(room.Bounds.Center, connected.Bounds.Center);
            if (dist < nearestRoomDist)
            {
                nearestRoomDist = dist;
                nearestRoom = connected;
            }
        }

        // If we have existing hallways nearby, route to them instead of the room
        if (nearestHallway != null && nearestHallwayDist < nearestRoomDist * 0.7f)
        {
            return RouteRoomToHallway(room, nearestHallway.Value, blockedTiles, existingHallways, width, result, allRooms);
        }

        // Otherwise, route to the nearest connected room
        if (nearestRoom != null)
        {
            return RouteRoomHallway(room, nearestRoom, blockedTiles, existingHallways, width, result, allRooms);
        }

        return null;
    }

    /// <summary>
    /// Routes from a room to a specific hallway tile.
    /// Starts 3 tiles in front of the door position.
    /// </summary>
    private HallwaySegment? RouteRoomToHallway(
        PlacedRoom room,
        Vector2i hallwayTarget,
        HashSet<Vector2i> blockedTiles,
        HashSet<Vector2i> existingHallways,
        int width,
        HallwayResult result,
        List<PlacedRoom> allRooms)
    {
        // Find the door position and direction
        var doorInfo = GetDoorPositionToward(room, hallwayTarget);
        if (doorInfo == null)
            return null;

        var (doorPos, doorDir) = doorInfo.Value;

        // Path starts 3 tiles in front of the door
        var pathStart = doorPos + doorDir * DoorOffset;

        // A* to the hallway tile
        var path = AStarPath(pathStart, hallwayTarget, blockedTiles, existingHallways, width, allRooms);
        if (path == null || path.Count == 0)
            return null;

        var segment = new HallwaySegment();

        // Add the connector from door to path start
        AddDoorConnector(segment, doorPos, doorDir, DoorOffset, blockedTiles);

        // Expand path to hallway width
        ExpandPathToHallway(segment, path, width, blockedTiles);

        // Calculate walls
        CalculateHallwayWalls(segment, blockedTiles, existingHallways, allRooms);

        // Add door
        result.DoorPlacements.Add(new DoorPlacement
        {
            Position = doorPos,
            Rotation = GetDoorRotationForSide(GetCardinalSideFromDir(doorDir)),
            ConnectedRoom = room
        });

        return segment;
    }

    /// <summary>
    /// Routes a hallway between two rooms, creating doors where the hallway meets room walls.
    /// Paths between points 3 tiles in front of each door.
    /// </summary>
    private HallwaySegment? RouteRoomHallway(
        PlacedRoom from,
        PlacedRoom to,
        HashSet<Vector2i> blockedTiles,
        HashSet<Vector2i> existingHallways,
        int width,
        HallwayResult result,
        List<PlacedRoom> allRooms)
    {
        var toCenter = new Vector2i((int)to.Bounds.Center.X, (int)to.Bounds.Center.Y);
        var fromCenter = new Vector2i((int)from.Bounds.Center.X, (int)from.Bounds.Center.Y);

        // Get door positions facing toward each other
        var fromDoorInfo = GetDoorPositionToward(from, toCenter);
        var toDoorInfo = GetDoorPositionToward(to, fromCenter);

        if (fromDoorInfo == null || toDoorInfo == null)
            return null;

        var (fromDoorPos, fromDoorDir) = fromDoorInfo.Value;
        var (toDoorPos, toDoorDir) = toDoorInfo.Value;

        // Path starts/ends 3 tiles in front of each door
        var fromPathStart = fromDoorPos + fromDoorDir * DoorOffset;
        var toPathEnd = toDoorPos + toDoorDir * DoorOffset;

        // A* pathfinding between the offset points
        var path = AStarPath(fromPathStart, toPathEnd, blockedTiles, existingHallways, width, allRooms);
        if (path == null || path.Count == 0)
            return null;

        var segment = new HallwaySegment();

        // Add connectors from doors to path
        AddDoorConnector(segment, fromDoorPos, fromDoorDir, DoorOffset, blockedTiles);
        AddDoorConnector(segment, toDoorPos, toDoorDir, DoorOffset, blockedTiles);

        // Expand path to hallway width
        ExpandPathToHallway(segment, path, width, blockedTiles);

        // Calculate walls
        CalculateHallwayWalls(segment, blockedTiles, existingHallways, allRooms);

        // Add doors
        result.DoorPlacements.Add(new DoorPlacement
        {
            Position = fromDoorPos,
            Rotation = GetDoorRotationForSide(GetCardinalSideFromDir(fromDoorDir)),
            ConnectedRoom = from
        });

        result.DoorPlacements.Add(new DoorPlacement
        {
            Position = toDoorPos,
            Rotation = GetDoorRotationForSide(GetCardinalSideFromDir(toDoorDir)),
            ConnectedRoom = to
        });

        return segment;
    }

    /// <summary>
    /// Gets the door position and outward direction for a room facing toward a target.
    /// Returns the center wall tile of the best valid side and the direction facing outward.
    /// </summary>
    private (Vector2i Position, Vector2i Direction)? GetDoorPositionToward(PlacedRoom room, Vector2i target)
    {
        var bounds = room.Bounds;
        var width = bounds.Width;
        var height = bounds.Height;
        var validSides = GetValidDoorSides(width, height);

        // Find which valid side faces most toward the target
        var roomCenter = new Vector2i((int)bounds.Center.X, (int)bounds.Center.Y);
        var toTarget = target - roomCenter;

        CardinalSide? bestSide = null;
        var bestDot = float.MinValue;

        foreach (var side in validSides)
        {
            var dir = GetDirectionForSide(side);
            var dot = toTarget.X * dir.X + toTarget.Y * dir.Y;
            if (dot > bestDot)
            {
                bestDot = dot;
                bestSide = side;
            }
        }

        if (bestSide == null)
            return null;

        var doorPos = GetSideCenterWallTile(room, bestSide.Value);
        if (doorPos == null)
            return null;

        return (doorPos.Value, GetDirectionForSide(bestSide.Value));
    }

    /// <summary>
    /// Gets the outward direction vector for a cardinal side.
    /// </summary>
    private static Vector2i GetDirectionForSide(CardinalSide side)
    {
        return side switch
        {
            CardinalSide.North => new Vector2i(0, 1),
            CardinalSide.South => new Vector2i(0, -1),
            CardinalSide.East => new Vector2i(1, 0),
            CardinalSide.West => new Vector2i(-1, 0),
            _ => Vector2i.Zero
        };
    }

    /// <summary>
    /// Gets the cardinal side from a direction vector.
    /// </summary>
    private static CardinalSide GetCardinalSideFromDir(Vector2i dir)
    {
        if (dir.Y > 0) return CardinalSide.North;
        if (dir.Y < 0) return CardinalSide.South;
        if (dir.X > 0) return CardinalSide.East;
        return CardinalSide.West;
    }

    /// <summary>
    /// Adds hallway tiles connecting a door to the main path.
    /// </summary>
    private void AddDoorConnector(
        HallwaySegment segment,
        Vector2i doorPos,
        Vector2i direction,
        int length,
        HashSet<Vector2i> blockedTiles)
    {
        for (var i = 1; i <= length; i++)
        {
            var tile = doorPos + direction * i;
            if (!blockedTiles.Contains(tile))
            {
                segment.Tiles.Add(tile);
            }
        }
    }

    /// <summary>
    /// Expands a path centerline to full hallway width.
    /// </summary>
    private void ExpandPathToHallway(
        HallwaySegment segment,
        List<Vector2i> path,
        int width,
        HashSet<Vector2i> blockedTiles)
    {
        var halfWidth = width / 2;
        foreach (var tile in path)
        {
            for (var dx = -halfWidth; dx <= halfWidth; dx++)
            {
                for (var dy = -halfWidth; dy <= halfWidth; dy++)
                {
                    var hallwayTile = new Vector2i(tile.X + dx, tile.Y + dy);
                    if (!blockedTiles.Contains(hallwayTile))
                    {
                        segment.Tiles.Add(hallwayTile);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calculates wall tiles around the hallway.
    /// Avoids placing walls that would block connections to existing hallways.
    /// </summary>
    private void CalculateHallwayWalls(
        HallwaySegment segment,
        HashSet<Vector2i> blockedTiles,
        HashSet<Vector2i> existingHallways,
        List<PlacedRoom> allRooms)
    {
        // Collect all room walls to avoid
        var allRoomWalls = new HashSet<Vector2i>();
        foreach (var room in allRooms)
        {
            allRoomWalls.UnionWith(room.WallTiles);
        }

        // Cardinal directions for adjacency check
        var cardinalDirs = new[]
        {
            new Vector2i(1, 0), new Vector2i(-1, 0),
            new Vector2i(0, 1), new Vector2i(0, -1)
        };

        foreach (var tile in segment.Tiles)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    var neighbor = new Vector2i(tile.X + dx, tile.Y + dy);

                    if (segment.Tiles.Contains(neighbor))
                        continue;
                    if (blockedTiles.Contains(neighbor))
                        continue;
                    if (allRoomWalls.Contains(neighbor))
                        continue;
                    if (existingHallways.Contains(neighbor))
                        continue;

                    // Check if placing a wall here would block connection to existing hallway
                    // A wall blocks if it's adjacent to both new hallway AND existing hallway
                    var wouldBlockConnection = false;
                    foreach (var dir in cardinalDirs)
                    {
                        var adj = neighbor + dir;
                        if (existingHallways.Contains(adj))
                        {
                            // This potential wall is adjacent to an existing hallway
                            // Don't place it - it would block the intersection
                            wouldBlockConnection = true;
                            break;
                        }
                    }

                    if (wouldBlockConnection)
                        continue;

                    segment.WallTiles.Add(neighbor);
                }
            }
        }
    }

    /// <summary>
    /// Gets valid door sides based on room dimensions.
    /// </summary>
    private static HashSet<CardinalSide> GetValidDoorSides(int width, int height)
    {
        var sides = new HashSet<CardinalSide>();

        if (height > width)
        {
            // Taller than wide: doors on north/south only
            sides.Add(CardinalSide.North);
            sides.Add(CardinalSide.South);
        }
        else if (width > height)
        {
            // Wider than tall: doors on east/west only
            sides.Add(CardinalSide.East);
            sides.Add(CardinalSide.West);
        }
        else
        {
            // Square: any side is valid
            sides.Add(CardinalSide.North);
            sides.Add(CardinalSide.South);
            sides.Add(CardinalSide.East);
            sides.Add(CardinalSide.West);
        }

        return sides;
    }

    /// <summary>
    /// Determines which cardinal side a wall tile is on relative to room bounds.
    /// </summary>
    private static CardinalSide? GetCardinalSide(Vector2i tile, Box2i bounds)
    {
        // Check if tile is on a cardinal edge (not a corner)
        var onLeft = tile.X == bounds.Left - 1;
        var onRight = tile.X == bounds.Right;
        var onBottom = tile.Y == bounds.Bottom - 1;
        var onTop = tile.Y == bounds.Top;

        // Cardinal sides only (not corners)
        if (onTop && !onLeft && !onRight && tile.X >= bounds.Left && tile.X < bounds.Right)
            return CardinalSide.North;
        if (onBottom && !onLeft && !onRight && tile.X >= bounds.Left && tile.X < bounds.Right)
            return CardinalSide.South;
        if (onRight && !onTop && !onBottom && tile.Y >= bounds.Bottom && tile.Y < bounds.Top)
            return CardinalSide.East;
        if (onLeft && !onTop && !onBottom && tile.Y >= bounds.Bottom && tile.Y < bounds.Top)
            return CardinalSide.West;

        return null;
    }

    /// <summary>
    /// Gets the center wall tile for a given side of the room.
    /// </summary>
    private static Vector2i? GetSideCenterWallTile(PlacedRoom room, CardinalSide side)
    {
        var bounds = room.Bounds;
        var centerX = (bounds.Left + bounds.Right) / 2;
        var centerY = (bounds.Bottom + bounds.Top) / 2;

        Vector2i target = side switch
        {
            CardinalSide.North => new Vector2i(centerX, bounds.Top),
            CardinalSide.South => new Vector2i(centerX, bounds.Bottom - 1),
            CardinalSide.East => new Vector2i(bounds.Right, centerY),
            CardinalSide.West => new Vector2i(bounds.Left - 1, centerY),
            _ => default
        };

        // Find the actual wall tile closest to this target
        Vector2i? closest = null;
        var closestDist = float.MaxValue;

        foreach (var wall in room.WallTiles)
        {
            var wallSide = GetCardinalSide(wall, bounds);
            if (wallSide != side)
                continue;

            var dist = DistanceVec2i(wall, target);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = wall;
            }
        }

        return closest;
    }

    /// <summary>
    /// Gets door rotation for a cardinal side.
    /// </summary>
    private static Angle GetDoorRotationForSide(CardinalSide side)
    {
        return side switch
        {
            CardinalSide.North => Angle.FromDegrees(90),
            CardinalSide.South => Angle.FromDegrees(270),
            CardinalSide.East => Angle.FromDegrees(0),
            CardinalSide.West => Angle.FromDegrees(180),
            _ => Angle.Zero
        };
    }

    private enum CardinalSide
    {
        North,
        South,
        East,
        West
    }

    /// <summary>
    /// A* pathfinding between two points.
    /// Avoids all room tiles, prefers existing hallways, penalizes turns for orthogonal paths.
    /// </summary>
    private List<Vector2i>? AStarPath(
        Vector2i start,
        Vector2i goal,
        HashSet<Vector2i> blockedTiles,
        HashSet<Vector2i> existingHallways,
        int hallwayWidth,
        List<PlacedRoom> allRooms)
    {
        var openSet = new PriorityQueue<(Vector2i Pos, Vector2i Dir), float>();
        var cameFrom = new Dictionary<Vector2i, Vector2i>();
        var gScore = new Dictionary<Vector2i, float> { [start] = 0 };
        var fScore = new Dictionary<Vector2i, float> { [start] = Heuristic(start, goal) };

        // Start with no direction (first move has no turn penalty)
        openSet.Enqueue((start, Vector2i.Zero), fScore[start]);

        var directions = new[]
        {
            new Vector2i(1, 0), new Vector2i(-1, 0),
            new Vector2i(0, 1), new Vector2i(0, -1)
        };

        var halfWidth = hallwayWidth / 2;
        const int maxIterations = 15000;
        var iterations = 0;

        // Precompute all room tiles (interior + walls) for fast blocking checks
        var allRoomTiles = new HashSet<Vector2i>(blockedTiles);
        foreach (var room in allRooms)
        {
            allRoomTiles.UnionWith(room.WallTiles);
            allRoomTiles.UnionWith(room.Tiles);
        }

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var (current, lastDir) = openSet.Dequeue();

            if (current == goal || DistanceVec2i(current, goal) <= halfWidth)
            {
                // Reconstruct path
                var path = new List<Vector2i> { current };
                while (cameFrom.ContainsKey(current))
                {
                    current = cameFrom[current];
                    path.Add(current);
                }
                path.Reverse();
                return path;
            }

            foreach (var dir in directions)
            {
                var neighbor = current + dir;

                // Check if hallway at this position would overlap any room tiles
                var blocked = false;
                for (var dx = -halfWidth; dx <= halfWidth && !blocked; dx++)
                {
                    for (var dy = -halfWidth; dy <= halfWidth && !blocked; dy++)
                    {
                        var check = new Vector2i(neighbor.X + dx, neighbor.Y + dy);
                        if (allRoomTiles.Contains(check))
                        {
                            blocked = true;
                        }
                    }
                }

                if (blocked)
                    continue;

                // Base movement cost
                var moveCost = 1f;

                // Prefer existing hallways (much cheaper)
                if (existingHallways.Contains(neighbor))
                {
                    moveCost = 0.1f;
                }

                // Turn penalty: changing direction costs extra to encourage straight paths
                if (lastDir != Vector2i.Zero && dir != lastDir)
                {
                    moveCost += 2f; // Penalty for turning
                }

                var tentativeG = gScore[current] + moveCost;

                if (!gScore.TryGetValue(neighbor, out var neighborG) || tentativeG < neighborG)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);
                    openSet.Enqueue((neighbor, dir), fScore[neighbor]);
                }
            }
        }

        return null; // No path found
    }

    private static float Heuristic(Vector2i a, Vector2i b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y); // Manhattan distance
    }

    private static float DistanceVec2i(Vector2i a, Vector2i b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Result from hallway generation.
/// </summary>
public sealed class HallwayResult
{
    /// <summary>
    /// All hallway floor tiles.
    /// </summary>
    public HashSet<Vector2i> Tiles { get; set; } = new();

    /// <summary>
    /// Individual hallway segments between zones.
    /// </summary>
    public List<HallwaySegment> Segments { get; set; } = new();

    /// <summary>
    /// Door placements where hallways connect to rooms.
    /// </summary>
    public List<DoorPlacement> DoorPlacements { get; set; } = new();
}

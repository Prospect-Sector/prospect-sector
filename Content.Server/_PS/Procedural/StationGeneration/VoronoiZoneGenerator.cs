using System.Numerics;
using Content.Shared._PS.Procedural.StationGeneration;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._PS.Procedural.StationGeneration;

/// <summary>
/// Generates Voronoi zones for station departments using Poisson disk sampling.
/// </summary>
public sealed class VoronoiZoneGenerator
{
    /// <summary>
    /// Generates zones for the given number of departments.
    /// </summary>
    /// <param name="departmentCount">Number of zones to create</param>
    /// <param name="stationRadius">Radius of the station in tiles</param>
    /// <param name="center">Center position of the station</param>
    /// <param name="minSpacing">Minimum spacing between department centers</param>
    /// <param name="random">Random number generator</param>
    /// <returns>List of generated zones</returns>
    public List<StationZone> GenerateZones(
        int departmentCount,
        int stationRadius,
        Vector2i center,
        float minSpacing,
        Random random)
    {
        // Generate department centers using Poisson disk sampling
        var centers = PoissonDiskSampling(departmentCount, stationRadius, center, minSpacing, random);

        // Create zones from centers
        var zones = new List<StationZone>();
        for (var i = 0; i < centers.Count; i++)
        {
            zones.Add(new StationZone
            {
                Id = i,
                Center = centers[i]
            });
        }

        // Assign tiles to nearest zone (Voronoi)
        AssignTilesToZones(zones, stationRadius, center);

        // Initialize available tiles from zone tiles
        foreach (var zone in zones)
        {
            zone.AvailableTiles = new HashSet<Vector2i>(zone.Tiles);
        }

        return zones;
    }

    /// <summary>
    /// Places points using Poisson disk sampling within a circular area.
    /// Points are placed with priority weights (higher priority = closer to center).
    /// </summary>
    private List<Vector2> PoissonDiskSampling(
        int targetCount,
        int radius,
        Vector2i center,
        float minSpacing,
        Random random)
    {
        var points = new List<Vector2>();
        var activeList = new List<Vector2>();
        var cellSize = minSpacing / MathF.Sqrt(2);
        var gridSize = (int)MathF.Ceiling(radius * 2 / cellSize) + 1;
        var grid = new int[gridSize, gridSize];

        // Initialize grid with -1 (empty)
        for (var x = 0; x < gridSize; x++)
        for (var y = 0; y < gridSize; y++)
            grid[x, y] = -1;

        // Helper to convert world pos to grid pos
        Vector2i ToGrid(Vector2 pos)
        {
            var gx = (int)((pos.X - center.X + radius) / cellSize);
            var gy = (int)((pos.Y - center.Y + radius) / cellSize);
            return new Vector2i(
                Math.Clamp(gx, 0, gridSize - 1),
                Math.Clamp(gy, 0, gridSize - 1));
        }

        // Helper to check if point is within station bounds
        bool InBounds(Vector2 pos)
        {
            var dx = pos.X - center.X;
            var dy = pos.Y - center.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        // Helper to check if point is too close to existing points
        bool IsTooClose(Vector2 pos)
        {
            var gridPos = ToGrid(pos);

            // Check 5x5 neighborhood
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    var nx = gridPos.X + dx;
                    var ny = gridPos.Y + dy;

                    if (nx < 0 || nx >= gridSize || ny < 0 || ny >= gridSize)
                        continue;

                    var idx = grid[nx, ny];
                    if (idx == -1)
                        continue;

                    var other = points[idx];
                    var dist = Vector2.Distance(pos, other);
                    if (dist < minSpacing)
                        return true;
                }
            }

            return false;
        }

        // Start with a point near center (but not exactly at center for variety)
        var initialOffset = new Vector2(
            (float)(random.NextDouble() - 0.5) * minSpacing * 0.5f,
            (float)(random.NextDouble() - 0.5) * minSpacing * 0.5f);
        var firstPoint = new Vector2(center.X, center.Y) + initialOffset;

        points.Add(firstPoint);
        activeList.Add(firstPoint);
        var firstGrid = ToGrid(firstPoint);
        grid[firstGrid.X, firstGrid.Y] = 0;

        const int maxAttempts = 30;

        while (activeList.Count > 0 && points.Count < targetCount)
        {
            // Pick random active point
            var activeIdx = random.Next(activeList.Count);
            var activePoint = activeList[activeIdx];
            var found = false;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Generate random point in annulus [minSpacing, 2*minSpacing]
                var angle = random.NextDouble() * Math.PI * 2;
                var dist = minSpacing + random.NextDouble() * minSpacing;
                var newPoint = new Vector2(
                    activePoint.X + (float)(Math.Cos(angle) * dist),
                    activePoint.Y + (float)(Math.Sin(angle) * dist));

                if (!InBounds(newPoint) || IsTooClose(newPoint))
                    continue;

                points.Add(newPoint);
                activeList.Add(newPoint);
                var newGrid = ToGrid(newPoint);
                grid[newGrid.X, newGrid.Y] = points.Count - 1;
                found = true;
                break;
            }

            if (!found)
            {
                activeList.RemoveAt(activeIdx);
            }
        }

        // If we didn't get enough points, try random placement as fallback
        var fallbackAttempts = 0;
        while (points.Count < targetCount && fallbackAttempts < 1000)
        {
            fallbackAttempts++;
            var angle = random.NextDouble() * Math.PI * 2;
            var dist = random.NextDouble() * radius * 0.8; // Stay away from edges
            var newPoint = new Vector2(
                center.X + (float)(Math.Cos(angle) * dist),
                center.Y + (float)(Math.Sin(angle) * dist));

            if (IsTooClose(newPoint))
                continue;

            points.Add(newPoint);
            var newGrid = ToGrid(newPoint);
            if (newGrid.X >= 0 && newGrid.X < gridSize && newGrid.Y >= 0 && newGrid.Y < gridSize)
                grid[newGrid.X, newGrid.Y] = points.Count - 1;
        }

        return points;
    }

    /// <summary>
    /// Assigns tiles to zones based on nearest center (Voronoi diagram).
    /// </summary>
    private void AssignTilesToZones(List<StationZone> zones, int radius, Vector2i center)
    {
        // Iterate over all tiles in the bounding box
        for (var x = center.X - radius; x <= center.X + radius; x++)
        {
            for (var y = center.Y - radius; y <= center.Y + radius; y++)
            {
                var tile = new Vector2i(x, y);

                // Check if tile is within circular station bounds
                var dx = x - center.X;
                var dy = y - center.Y;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                // Find nearest zone center
                var nearestZone = FindNearestZone(zones, tile);
                if (nearestZone != null)
                {
                    nearestZone.Tiles.Add(tile);
                }
            }
        }
    }

    /// <summary>
    /// Finds the zone with the nearest center to the given tile.
    /// </summary>
    private StationZone? FindNearestZone(List<StationZone> zones, Vector2i tile)
    {
        StationZone? nearest = null;
        var minDistSq = float.MaxValue;

        foreach (var zone in zones)
        {
            var dx = tile.X - zone.Center.X;
            var dy = tile.Y - zone.Center.Y;
            var distSq = dx * dx + dy * dy;

            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = zone;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Smooths zone boundaries to reduce jagged edges.
    /// Uses a simple voting algorithm - if a tile has more neighbors in another zone, switch it.
    /// </summary>
    public void SmoothZoneBoundaries(List<StationZone> zones, int iterations = 2)
    {
        var tileToZone = new Dictionary<Vector2i, StationZone>();

        // Build lookup
        foreach (var zone in zones)
        {
            foreach (var tile in zone.Tiles)
            {
                tileToZone[tile] = zone;
            }
        }

        var directions = new[]
        {
            new Vector2i(1, 0), new Vector2i(-1, 0),
            new Vector2i(0, 1), new Vector2i(0, -1)
        };

        for (var iter = 0; iter < iterations; iter++)
        {
            var changes = new List<(Vector2i Tile, StationZone From, StationZone To)>();

            foreach (var (tile, zone) in tileToZone)
            {
                var neighborCounts = new Dictionary<StationZone, int>();

                foreach (var dir in directions)
                {
                    var neighbor = tile + dir;
                    if (tileToZone.TryGetValue(neighbor, out var neighborZone))
                    {
                        neighborCounts.TryGetValue(neighborZone, out var count);
                        neighborCounts[neighborZone] = count + 1;
                    }
                }

                // Find dominant neighbor zone
                StationZone? dominant = null;
                var maxCount = 0;
                foreach (var (neighborZone, count) in neighborCounts)
                {
                    if (count > maxCount)
                    {
                        maxCount = count;
                        dominant = neighborZone;
                    }
                }

                // If more neighbors are in a different zone, schedule a switch
                if (dominant != null && dominant != zone && maxCount >= 3)
                {
                    changes.Add((tile, zone, dominant));
                }
            }

            // Apply changes
            foreach (var (tile, from, to) in changes)
            {
                from.Tiles.Remove(tile);
                to.Tiles.Add(tile);
                tileToZone[tile] = to;
            }
        }

        // Update available tiles
        foreach (var zone in zones)
        {
            zone.AvailableTiles = new HashSet<Vector2i>(zone.Tiles);
        }
    }
}

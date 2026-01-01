using System.Numerics;
using Content.Shared.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Procedural.StationGeneration;

/// <summary>
/// Represents a department zone in the station, containing tiles and placed rooms.
/// </summary>
public sealed class StationZone
{
    /// <summary>
    /// Unique identifier for this zone.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Center point of this zone (used for hallway routing).
    /// </summary>
    public Vector2 Center { get; set; }

    /// <summary>
    /// All tiles belonging to this zone (before room placement).
    /// </summary>
    public HashSet<Vector2i> Tiles { get; set; } = new();

    /// <summary>
    /// The department prototype assigned to this zone.
    /// </summary>
    public ProtoId<StationDepartmentPrototype>? Department { get; set; }

    /// <summary>
    /// Rooms placed within this zone.
    /// </summary>
    public List<PlacedRoom> Rooms { get; set; } = new();

    /// <summary>
    /// IDs of zones this zone is connected to via hallways.
    /// </summary>
    public HashSet<int> ConnectedZones { get; set; } = new();

    /// <summary>
    /// Tiles available for room placement (zone tiles minus placed rooms and buffers).
    /// </summary>
    public HashSet<Vector2i> AvailableTiles { get; set; } = new();
}

/// <summary>
/// Represents a room that has been placed within a zone.
/// </summary>
public sealed class PlacedRoom
{
    /// <summary>
    /// The room prototype that was placed.
    /// </summary>
    public required DungeonRoomPrototype Prototype { get; set; }

    /// <summary>
    /// Axis-aligned bounding box of the room in world coordinates.
    /// </summary>
    public Box2i Bounds { get; set; }

    /// <summary>
    /// Transform matrix for the room (includes position and rotation).
    /// </summary>
    public Matrix3x2 Transform { get; set; }

    /// <summary>
    /// All floor tiles of this room (interior).
    /// </summary>
    public HashSet<Vector2i> Tiles { get; set; } = new();

    /// <summary>
    /// Wall tiles surrounding this room.
    /// </summary>
    public HashSet<Vector2i> WallTiles { get; set; } = new();

    /// <summary>
    /// Buffer tiles around the room (1-tile gap for shaft tunnels).
    /// </summary>
    public HashSet<Vector2i> BufferTiles { get; set; } = new();
}

/// <summary>
/// Result from shaft tunnel generation.
/// </summary>
public sealed class MaintsResult
{
    /// <summary>
    /// All tiles that should be shaft tunnel floor.
    /// </summary>
    public HashSet<Vector2i> TunnelTiles { get; set; } = new();

    /// <summary>
    /// Positions where doors should be placed (tunnel-to-room connections).
    /// </summary>
    public List<DoorPlacement> DoorPlacements { get; set; } = new();
}

/// <summary>
/// Specifies where and how to place a door.
/// </summary>
public sealed class DoorPlacement
{
    /// <summary>
    /// Tile position for the door.
    /// </summary>
    public Vector2i Position { get; set; }

    /// <summary>
    /// Rotation of the door (for proper orientation).
    /// </summary>
    public Angle Rotation { get; set; }

    /// <summary>
    /// The room this door connects to.
    /// </summary>
    public PlacedRoom? ConnectedRoom { get; set; }
}

/// <summary>
/// Edge between two zones for MST calculation.
/// </summary>
public readonly record struct ZoneEdge(int FromId, int ToId, float Distance);

/// <summary>
/// Represents a generated hallway segment.
/// </summary>
public sealed class HallwaySegment
{
    /// <summary>
    /// All floor tiles of this hallway.
    /// </summary>
    public HashSet<Vector2i> Tiles { get; set; } = new();

    /// <summary>
    /// Wall tiles along the hallway edges.
    /// </summary>
    public HashSet<Vector2i> WallTiles { get; set; } = new();

    /// <summary>
    /// The zones this hallway connects.
    /// </summary>
    public int FromZoneId { get; set; }
    public int ToZoneId { get; set; }
}

using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.Generation;

/// <summary>
/// One node of a BSP partition tree. Leaves hold a room; internal nodes hold two children split
/// along <see cref="Axis"/>.
/// </summary>
public sealed class BspNode
{
    public Box2i Bounds;
    public BspNode? Parent;
    public BspNode? Left;
    public BspNode? Right;
    public BspSplitAxis Axis;

    public bool IsLeaf => Left == null && Right == null;

    public BspNode(Box2i bounds)
    {
        Bounds = bounds;
        Axis = BspSplitAxis.None;
    }

    /// <summary>
    /// Enumerates every leaf under this node (inclusive if this node is itself a leaf).
    /// </summary>
    public IEnumerable<BspNode> Leaves()
    {
        if (IsLeaf)
        {
            yield return this;
            yield break;
        }

        foreach (var leaf in Left!.Leaves())
            yield return leaf;

        foreach (var leaf in Right!.Leaves())
            yield return leaf;
    }
}

public enum BspSplitAxis : byte
{
    None = 0,
    /// <summary>Vertical cut — children stacked left/right.</summary>
    Vertical = 1,
    /// <summary>Horizontal cut — children stacked bottom/top.</summary>
    Horizontal = 2,
}

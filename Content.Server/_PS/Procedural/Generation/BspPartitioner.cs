using Robust.Shared.Maths;

namespace Content.Server._PS.Procedural.Generation;

/// <summary>
/// Pure recursive partitioning of a rectangular region into leaves that satisfy a min/max-size
/// constraint. No knowledge of rooms, prefabs, or tiles — just geometry.
/// </summary>
public static class BspPartitioner
{
    public static BspNode Partition(
        Box2i rootBounds,
        Vector2i minLeafSize,
        Vector2i maxLeafSize,
        float splitRatioMin,
        float splitRatioMax,
        Random random)
    {
        var root = new BspNode(rootBounds);
        Split(root, minLeafSize, maxLeafSize, splitRatioMin, splitRatioMax, random);
        return root;
    }

    private static void Split(
        BspNode node,
        Vector2i minLeafSize,
        Vector2i maxLeafSize,
        float splitRatioMin,
        float splitRatioMax,
        Random random)
    {
        var width = node.Bounds.Width;
        var height = node.Bounds.Height;

        var canSplitVertical = width >= 2 * minLeafSize.X;
        var canSplitHorizontal = height >= 2 * minLeafSize.Y;

        if (!canSplitVertical && !canSplitHorizontal)
            return;

        var mustSplit = width > maxLeafSize.X || height > maxLeafSize.Y;

        // If both axes are within max bounds, split with a coin flip so the dungeon doesn't
        // always collapse to a specific shape at the leaves.
        if (!mustSplit && random.Next(4) == 0)
            return;

        BspSplitAxis axis;
        if (canSplitVertical && canSplitHorizontal)
        {
            if (width > height * 1.25f)
                axis = BspSplitAxis.Vertical;
            else if (height > width * 1.25f)
                axis = BspSplitAxis.Horizontal;
            else
                axis = random.Next(2) == 0 ? BspSplitAxis.Vertical : BspSplitAxis.Horizontal;
        }
        else if (canSplitVertical)
        {
            axis = BspSplitAxis.Vertical;
        }
        else
        {
            axis = BspSplitAxis.Horizontal;
        }

        var ratio = splitRatioMin + (float)random.NextDouble() * (splitRatioMax - splitRatioMin);

        Box2i leftBounds;
        Box2i rightBounds;

        if (axis == BspSplitAxis.Vertical)
        {
            var splitX = node.Bounds.Left + (int)(width * ratio);
            splitX = Math.Clamp(splitX, node.Bounds.Left + minLeafSize.X, node.Bounds.Right - minLeafSize.X);
            leftBounds = new Box2i(node.Bounds.Left, node.Bounds.Bottom, splitX, node.Bounds.Top);
            rightBounds = new Box2i(splitX, node.Bounds.Bottom, node.Bounds.Right, node.Bounds.Top);
        }
        else
        {
            var splitY = node.Bounds.Bottom + (int)(height * ratio);
            splitY = Math.Clamp(splitY, node.Bounds.Bottom + minLeafSize.Y, node.Bounds.Top - minLeafSize.Y);
            leftBounds = new Box2i(node.Bounds.Left, node.Bounds.Bottom, node.Bounds.Right, splitY);
            rightBounds = new Box2i(node.Bounds.Left, splitY, node.Bounds.Right, node.Bounds.Top);
        }

        node.Axis = axis;
        node.Left = new BspNode(leftBounds) { Parent = node };
        node.Right = new BspNode(rightBounds) { Parent = node };

        Split(node.Left, minLeafSize, maxLeafSize, splitRatioMin, splitRatioMax, random);
        Split(node.Right, minLeafSize, maxLeafSize, splitRatioMin, splitRatioMax, random);
    }
}

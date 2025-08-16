namespace Content.Shared._PS.Terradrop;

/// <summary>
/// Based on ResearchColorScheme, this class provides a color scheme for the Terradrop map items.
/// </summary>
public static class TerradropColorScheme
{
    public struct MapItemColors
    {
        public Color Background { get; set; }
        public Color Border { get; set; }
        public Color Hover { get; set; }
        public Color Selected { get; set; }
        public Color Connection { get; set; }
        public Color InfoText { get; set; }

        public MapItemColors(Color background, Color border, Color hover, Color selected, Color connection, Color infoText)
        {
            Background = background;
            Border = border;
            Hover = hover;
            Selected = selected;
            Connection = connection;
            InfoText = infoText;
        }
    }

    public static class UIColors
    {
        public static Color DefaultMapBackground { get; set; } = Color.FromHex("#141F2F");
        public static Color DefaultMapBorder { get; set; } = Color.FromHex("#4972A1");
        public static Color DefaultMapHover { get; set; } = Color.FromHex("#4972A1");

        public static class Scrollbar
        {
            public static Color Normal { get; set; } = Color.FromHex("#80808059");
            public static Color Hovered { get; set; } = Color.FromHex("#8C8C8C59");
            public static Color Grabbed { get; set; } = Color.FromHex("#8C8C8C59");
        }

        public static class InterpolationFactors
        {
            public static float Explored { get; set; } = 0.2f;
            public static float Unexplored { get; set; } = 0.0f;
            public static float InProgress { get; set; } = 0.0f;
            public static float Unavailable { get; set; } = 0.5f;
            public static float Default { get; set; } = 0.5f;
        }

        public static class MixingFactors
        {
            public static float Hover { get; set; } = 0.3f;
            public static float Selected { get; set; } = 0.5f;
        }
    }

    private static readonly Dictionary<TerradropMapAvailability, MapItemColors> MapItemColorCache = new();
    private static bool _cacheInvalidated = true;

    private static readonly Dictionary<TerradropMapAvailability, MapItemColors> BaseMapItemColors = new()
    {
        [TerradropMapAvailability.Explored] = new MapItemColors(
            background: Color.LimeGreen,
            border: Color.LimeGreen,
            hover: Color.LimeGreen,
            selected: Color.LimeGreen,
            connection: Color.LimeGreen,
            infoText: Color.LimeGreen
        ),
        [TerradropMapAvailability.Unexplored] = new MapItemColors(
            background: Color.FromHex("#e8fa25"),
            border: Color.FromHex("#e8fa25"),
            hover: Color.FromHex("#e8fa25"),
            selected: Color.FromHex("#e8fa25"),
            connection: Color.FromHex("#e8fa25"),
            infoText: Color.FromHex("#e8fa25")
        ),
        [TerradropMapAvailability.InProgress] = new MapItemColors(
            background: Color.FromHex("#cca031"),
            border: Color.FromHex("#cca031"),
            hover: Color.FromHex("#cca031"),
            selected: Color.FromHex("#cca031"),
            connection: Color.FromHex("#cca031"),
            infoText: Color.Crimson
        ),
        [TerradropMapAvailability.Unavailable] = new MapItemColors(
            background: Color.Crimson,
            border: Color.Crimson,
            hover: Color.Crimson,
            selected: Color.Crimson,
            connection: Color.Crimson,
            infoText: Color.Crimson
        )
    };

    public static MapItemColors GetMapItemColors(TerradropMapAvailability availability)
    {
        if (_cacheInvalidated)
        {
            RebuildCache();
        }

        return MapItemColorCache.TryGetValue(availability, out var colors)
            ? colors
            : MapItemColorCache[TerradropMapAvailability.Unavailable];
    }

    public static void SetTechItemColors(TerradropMapAvailability availability, Color background, Color border,
        Color? hover = null, Color? selected = null, Color? connection = null, Color? infoText = null)
    {
        BaseMapItemColors[availability] = new MapItemColors(
            background: background,
            border: border,
            hover: hover ?? border,
            selected: selected ?? border,
            connection: connection ?? border,
            infoText: infoText ?? border
        );
        _cacheInvalidated = true;
    }

    public static MapItemColors GetTechItemColors(TerradropMapAvailability availability)
    {
        if (_cacheInvalidated)
        {
            RebuildCache();
        }

        return MapItemColorCache.TryGetValue(availability, out var colors)
            ? colors
            : MapItemColorCache[TerradropMapAvailability.Unavailable];
    }

    public static Color GetConnectionColor(TerradropMapAvailability availability)
    {
        return GetTechItemColors(availability).Connection;
    }

    public static Color GetTechBorderColor(TerradropMapAvailability availability)
    {
        return GetTechItemColors(availability).Border;
    }

    public static Color? GetInfoPanelColor(TerradropMapAvailability availability)
    {
        var colors = GetTechItemColors(availability);
        return availability == TerradropMapAvailability.Unexplored ? null : colors.InfoText;
    }

    public static float GetBackgroundInterpolationFactor(TerradropMapAvailability availability)
    {
        return availability switch
        {
            TerradropMapAvailability.Explored => UIColors.InterpolationFactors.Explored,
            TerradropMapAvailability.Unexplored => UIColors.InterpolationFactors.Unexplored,
            TerradropMapAvailability.InProgress => UIColors.InterpolationFactors.InProgress,
            TerradropMapAvailability.Unavailable => UIColors.InterpolationFactors.Unavailable,
            _ => UIColors.InterpolationFactors.Default
        };
    }

    public static float GetHoverMixingFactor() => UIColors.MixingFactors.Hover;
    public static float GetSelectionMixingFactor() => UIColors.MixingFactors.Selected;

    private static void RebuildCache()
    {
        MapItemColorCache.Clear();
        foreach (var kvp in BaseMapItemColors)
        {
            MapItemColorCache[kvp.Key] = kvp.Value;
        }
        _cacheInvalidated = false;
    }
}

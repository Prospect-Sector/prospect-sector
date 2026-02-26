using System.Linq;
using Content.Shared.Mind.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Terradrop;

public abstract class SharedTerradropSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;

    protected const int MissionLimit = 3;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerradropPortalComponent, PreventCollideEvent>(OnPortalPreventCollide);
    }

    private void OnPortalPreventCollide(EntityUid uid, TerradropPortalComponent component, ref PreventCollideEvent args)
    {
        if (!HasComp<MindContainerComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    public FormattedMessage GetMapDescription(TerradropMapPrototype map)
    {
        var description = new FormattedMessage();

        if (map.MapPrerequisites.Any())
        {
            description.AddMarkupOrThrow(Loc.GetString("terradrop-console-prereqs-list-start"));
            foreach (var prerequisiteMap in map.MapPrerequisites)
            {
                var mapProto = PrototypeManager.Index(prerequisiteMap);
                description.PushNewline();
                description.AddMarkupOrThrow(Loc.GetString("terradrop-console-prereqs-list-entry",
                    ("text", Loc.GetString(mapProto.Name))));
            }
            description.PushNewline();
        }

        description.AddMarkupOrThrow(Loc.GetString("terradrop-console-unlocks-list-start"));
        foreach (var unlockMap in map.MapUnlocks)
        {
            var mapProto = PrototypeManager.Index(unlockMap);
            description.PushNewline();
            description.AddMarkupOrThrow(Loc.GetString("terradrop-console-unlocks-list-entry",
                ("name", Loc.GetString(mapProto.Name))));
        }
        return description;
    }
}

[NetSerializable] [Serializable]
public enum TerradropConsoleUiKey : byte
{
    Default,
}

[Serializable, NetSerializable]
public sealed class TerradropConsoleBoundInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// All terradrop map nodes and their availabilities
    /// </summary>
    public Dictionary<string, TerradropMapAvailability> MapNodes;

    /// <summary>
    /// Active instance names per map ID. Used for the reconnect popup.
    /// </summary>
    public Dictionary<string, List<string>> ActiveInstances;

    /// <summary>
    /// The highest completed level per planet map ID.
    /// </summary>
    public Dictionary<string, int> HighestCompletedLevels;

    /// <summary>
    /// The global max level any planet can be started at.
    /// Equal to (max of all highest completed levels) + 1, or 0 if nothing completed.
    /// </summary>
    public int GlobalMaxLevel;

    public TerradropConsoleBoundInterfaceState(
        Dictionary<string, TerradropMapAvailability> mapNodes,
        Dictionary<string, List<string>> activeInstances,
        Dictionary<string, int> highestCompletedLevels,
        int globalMaxLevel)
    {
        MapNodes = mapNodes;
        ActiveInstances = activeInstances;
        HighestCompletedLevels = highestCompletedLevels;
        GlobalMaxLevel = globalMaxLevel;
    }
}

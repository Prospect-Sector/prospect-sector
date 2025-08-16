using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._PS.Terradrop;

public abstract class SharedTerradropSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;

    protected const int MissionLimit = 3;

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

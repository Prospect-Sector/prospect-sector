using System.Linq;
using Content.Shared._PS.Terradrop;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server._PS.Terradrop;

public sealed partial class TerradropSystem
{
    private void InitializeConsole()
    {
        SubscribeLocalEvent<TerradropConsoleComponent, StartTerradropMessage>(OnStartTerradropMessage);
        SubscribeLocalEvent<TerradropConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<TerradropMapComponent, ComponentShutdown>(OnTerradropMapShutdown);
    }

    private void OnTerradropMapShutdown(EntityUid mapUid, TerradropMapComponent component, ComponentShutdown args)
    {
        if (component.StationUid is not { Valid: true } || component.MapPrototype == null)
            return;

        // Delete the active mission data so a new map may be generated.
        var data = EnsureComp<TerradropStationComponent>(component.StationUid.Value);
        data.ActiveMissions.Remove(component.MapPrototype.ID);
    }

    private void OnConsoleUiOpened(EntityUid uid, TerradropConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true })
            return;

        UpdateConsoleInterface(uid, component);
    }

    private void OnStartTerradropMessage(EntityUid consoleUid,
        TerradropConsoleComponent consoleComponent,
        ref StartTerradropMessage message)
    {
        if (_station.GetOwningStation(consoleUid) is not { } station)
            return;
        var data = EnsureComp<TerradropStationComponent>(station);
        var consoleTransform = Transform(consoleUid);

        // Generate missions if there are none generated yet.
        if (data.Missions.Count == 0)
            GenerateMissionParams(data);

        var mapProto = _prototypeManager.Index<TerradropMapPrototype>(message.TerradropMapId);

        var missionParams = data.Missions[message.TerradropMapId];
        var landingPadTile = new Tile(_tileDefinitionManager[consoleComponent.LandingPadTileName].TileId);

        // Find the nearest available pad.
        var padQuery = EntityQueryEnumerator<TransformComponent, TerradropPadComponent>();
        while (padQuery.MoveNext(out var uid, out var transform, out var component))
        {
            var isOnSameGrid = transform.GridUid == consoleTransform.GridUid;
            var isAvailable = _timing.CurTime > component.ActivatedAt + component.ClearPortalDelay;
            if (isOnSameGrid && isAvailable)
            {
                _audio.PlayPredicted(consoleComponent.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

                if (data.ActiveMissions.TryGetValue(message.TerradropMapId, out var mission))
                {
                    // Mission already active, just make a new portal to it.
                    OpenPortalToMap(uid, mission);
                    return;
                }

                // Validate level is at least the map's minimum
                var selectedLevel = Math.Max(message.Level, mapProto.MinLevel);

                // Found a pad to use.
                CreateNewTerradropJob(
                    mapPrototype: mapProto,
                    missionParams: missionParams,
                    station: station,
                    targetPad: uid,
                    landingPadTile: landingPadTile,
                    level: selectedLevel
                );
                return;
            }
        }

        // No portals found.
        _audio.PlayPredicted(consoleComponent.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));
        _popup.PopupEntity(Loc.GetString("terradrop-console-no-portal"), consoleUid);
    }

    private void UpdateConsoleInterface(EntityUid uid, TerradropConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (_station.GetOwningStation(uid) is not { } station)
            return;
        var data = EnsureComp<TerradropStationComponent>(station);

        var allTechs = PrototypeManager.EnumeratePrototypes<TerradropMapPrototype>();
        Dictionary<string, TerradropMapAvailability> mapList;

        var unlockedMaps = new HashSet<string>(data.UnlockedMapNodes);
        mapList = allTechs.ToDictionary(
            proto => proto.ID,
            proto =>
            {
                if (data.ActiveMissions.ContainsKey(proto.ID))
                    return TerradropMapAvailability.InProgress;

                // First map is always available.
                if (proto.UnlockedByDefault)
                    return TerradropMapAvailability.Unexplored;

                if (unlockedMaps.Contains(proto.ID))
                    return TerradropMapAvailability.Unexplored;

                var prereqsMet = proto.MapPrerequisites.All(p => unlockedMaps.Contains(p));

                return prereqsMet ? TerradropMapAvailability.Unexplored : TerradropMapAvailability.Unavailable;
            });

        _uiSystem.SetUiState(uid, TerradropConsoleUiKey.Default,
            new TerradropConsoleBoundInterfaceState(mapList));
    }
}

using System.Linq;
using Content.Shared._PS.Terradrop;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server._PS.Terradrop;

public sealed partial class TerradropSystem
{
    private void InitializeConsole()
    {
        SubscribeLocalEvent<TerradropConsoleComponent, StartTerradropMessage>(OnStartTerradropMessage);
        SubscribeLocalEvent<TerradropConsoleComponent, DisconnectPortalMessage>(OnDisconnectPortalMessage);
        SubscribeLocalEvent<TerradropConsoleComponent, ReconnectPortalMessage>(OnReconnectPortalMessage);
        SubscribeLocalEvent<TerradropConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<TerradropMapComponent, ComponentShutdown>(OnTerradropMapShutdown);
    }

    private void OnTerradropMapShutdown(EntityUid mapUid, TerradropMapComponent component, ComponentShutdown args)
    {
        if (component.StationUid is not { Valid: true } || component.MapPrototype == null)
            return;

        var data = EnsureComp<TerradropStationComponent>(component.StationUid.Value);

        if (!data.ActiveMissions.TryGetValue(component.MapPrototype.ID, out var instances))
            return;

        // Remove the specific instance matching this map entity.
        instances.RemoveAll(m => m.MapUid == mapUid);

        if (instances.Count == 0)
            data.ActiveMissions.Remove(component.MapPrototype.ID);

        UpdateAllConsolesForStation(component.StationUid.Value);
    }

    /// <summary>
    /// Pushes a UI state update to every open terradrop console on the given station.
    /// </summary>
    private void UpdateAllConsolesForStation(EntityUid station)
    {
        var query = EntityQueryEnumerator<TerradropConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_station.GetOwningStation(uid) == station)
                UpdateConsoleInterface(uid, comp);
        }
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

        var baseMissionParams = data.Missions[message.TerradropMapId];
        var landingPadTile = new Tile(_tileDefinitionManager[consoleComponent.LandingPadTileName].TileId);

        // Find the nearest available pad (portal is null or deleted).
        var padQuery = EntityQueryEnumerator<TransformComponent, TerradropPadComponent>();
        while (padQuery.MoveNext(out var uid, out var transform, out var component))
        {
            var isOnSameGrid = transform.GridUid == consoleTransform.GridUid;
            var isAvailable = component.Portal == null || Deleted(component.Portal);
            if (isOnSameGrid && isAvailable)
            {
                _audio.PlayPredicted(consoleComponent.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

                // Always create a NEW instance with a fresh seed so each map is unique.
                var selectedLevel = Math.Max(message.Level, mapProto.MinLevel);
                var instanceParams = new SalvageMissionParams
                {
                    Index = baseMissionParams.Index,
                    Seed = _random.Next(),
                    Difficulty = baseMissionParams.Difficulty,
                };

                CreateNewTerradropJob(
                    mapPrototype: mapProto,
                    missionParams: instanceParams,
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

    private void OnDisconnectPortalMessage(EntityUid consoleUid,
        TerradropConsoleComponent consoleComponent,
        ref DisconnectPortalMessage message)
    {
        var consoleTransform = Transform(consoleUid);

        // Find a pad on the same grid that has an active portal.
        var padQuery = EntityQueryEnumerator<TransformComponent, TerradropPadComponent>();
        while (padQuery.MoveNext(out _, out var transform, out var pad))
        {
            if (transform.GridUid != consoleTransform.GridUid)
                continue;
            if (pad.Portal == null || Deleted(pad.Portal))
                continue;

            QueueDel(pad.Portal.Value);
            pad.Portal = null;
            _audio.PlayPvs(pad.ClearPortalSound, transform.Coordinates);
            return;
        }
    }

    private void OnReconnectPortalMessage(EntityUid consoleUid,
        TerradropConsoleComponent consoleComponent,
        ref ReconnectPortalMessage message)
    {
        if (_station.GetOwningStation(consoleUid) is not { } station)
            return;
        var data = EnsureComp<TerradropStationComponent>(station);
        var consoleTransform = Transform(consoleUid);

        if (!data.ActiveMissions.TryGetValue(message.TerradropMapId, out var instances))
            return;
        if (message.InstanceIndex < 0 || message.InstanceIndex >= instances.Count)
            return;

        var mission = instances[message.InstanceIndex];

        // Find an available pad on the same grid.
        var padQuery = EntityQueryEnumerator<TransformComponent, TerradropPadComponent>();
        while (padQuery.MoveNext(out var uid, out var transform, out var pad))
        {
            var isOnSameGrid = transform.GridUid == consoleTransform.GridUid;
            var isAvailable = pad.Portal == null || Deleted(pad.Portal);
            if (isOnSameGrid && isAvailable)
            {
                _audio.PlayPredicted(consoleComponent.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));
                OpenPortalToMap(uid, mission);
                return;
            }
        }

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

        // Build active instances dictionary for the reconnect popup.
        var activeInstances = new Dictionary<string, List<string>>();
        foreach (var (mapId, instances) in data.ActiveMissions)
        {
            activeInstances[mapId] = instances.Select(i =>
            {
                var level = TryComp<TerradropMapComponent>(i.MapUid, out var mc) ? mc.Level : 0;
                return Loc.GetString("terradrop-instance-entry", ("name", i.InstanceName), ("level", level));
            }).ToList();
        }

        _uiSystem.SetUiState(uid, TerradropConsoleUiKey.Default,
            new TerradropConsoleBoundInterfaceState(mapList, activeInstances));
    }
}

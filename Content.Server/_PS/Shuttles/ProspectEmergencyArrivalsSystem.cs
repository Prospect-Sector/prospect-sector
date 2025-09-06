using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._PS.Shuttles;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Tiles;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._PS.Shuttles;

public sealed class ProspectEmergencyArrivalsSystem : SharedProspectEmergencyArrivalsSystem
{
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>
    ///     The first arrival is a little early, to save everyone 10s
    /// </summary>
    private const float RoundStartFTLDuration = 10f;

    /// <summary>
    /// If enabled then spawns players on an alternate map so they can take a shuttle to the station.
    /// </summary>
    public bool Enabled { get; private set; }


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawningEvent>(HandlePlayerSpawning,
            before: [typeof(SpawnPointSystem)],
            after: [typeof(ContainerSpawnPointSystem)]
        );
        SubscribeLocalEvent<ProspectEmergencyArrivalsShuttleTargetComponent, ComponentInit>(OnStationPostInit);
        Enabled = _cfgManager.GetCVar(CCVars.EmergencyArrivalsShuttle);
    }

    private void OnStationPostInit(EntityUid uid, ProspectEmergencyArrivalsShuttleTargetComponent component, ref ComponentInit args)
    {
        if (!Enabled)
            return;
        SetupShuttle(uid, component);
    }

    private void SetupShuttle(EntityUid uid, ProspectEmergencyArrivalsShuttleTargetComponent component)
    {
        if (!Deleted(component.Shuttle))
            return;

        var dummpMapEntity = _mapSystem.CreateMap(out var dummyMapId);

        if (_loader.TryLoadGrid(dummyMapId, component.ShuttlePath, out var shuttle))
        {
            component.Shuttle = shuttle.Value;
            var shuttleComp = Comp<ShuttleComponent>(component.Shuttle);
            var arrivalsComp = EnsureComp<ProspectEmergencyShuttleComponent>(component.Shuttle);
            arrivalsComp.Station = uid;
            EnsureComp<ProtectedGridComponent>(component.Shuttle);
            _shuttles.FTLToDock(component.Shuttle, shuttleComp, uid, hyperspaceTime: RoundStartFTLDuration);
        }

        // Don't start the arrivals shuttle immediately docked so power has a time to stabilise?
        var timer = AddComp<TimedDespawnComponent>(dummpMapEntity);
        timer.Lifetime = 15f;
    }

    public void HandlePlayerSpawning(PlayerSpawningEvent ev)
    {
        if (ev.SpawnResult != null)
            return;

        if (!Enabled)
            return;

        if (!TryGetShuttleTarget(out var station))
            return;
        if (!TryComp<ProspectEmergencyArrivalsShuttleTargetComponent>(station, out var shuttleTargetComp))
            return;
        if (!TryComp(shuttleTargetComp.Shuttle, out TransformComponent? shuttleXform))
            return;

        var mapId = shuttleXform.MapID;
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();

        var possiblePositions = new List<EntityCoordinates>();
        while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            possiblePositions.Add(xform.Coordinates);
        }

        if (possiblePositions.Count <= 0)
            return;

        var spawnLoc = _random.Pick(possiblePositions);
        ev.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            ev.Job,
            ev.HumanoidCharacterProfile,
            ev.Station);

        EnsureComp<AutoOrientComponent>(ev.SpawnResult.Value);
    }

    private bool TryGetShuttleTarget(out EntityUid uid)
    {
        var arrivalsQuery = EntityQueryEnumerator<ProspectEmergencyArrivalsShuttleTargetComponent>();

        while (arrivalsQuery.MoveNext(out uid, out _))
        {
            return true;
        }

        return false;
    }
}

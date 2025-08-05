using System.Linq;
using System.Threading;
using Content.Shared._PS.Expadition;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Audio;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._PS.Expadition;

public sealed partial class ExpaditionSystem: SharedExpaditionSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly LinkedEntitySystem _link = default!;
    //[Dependency] private readonly SalvageSystem _salvage = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;

    private readonly JobQueue _salvageQueue = new();
    private readonly List<(SpawnExpaditionJob Job, CancellationTokenSource CancelToken)> _salvageJobs = new();
    private const double SalvageJobTime = 0.002;

    public void InitializeConsole()
    {
        SubscribeLocalEvent<ExpaditionConsoleComponent, StartExpaditionMessage>(OnStartExpaditionMessage);
    }

    private void OnStartExpaditionMessage(EntityUid consoleUid,
        ExpaditionConsoleComponent component,
        ref StartExpaditionMessage message)
    {
        // Do not allow creating a new portal until current ones are cleared.
        if (_salvageJobs.Count != 0)
            return;


        var data = EnsureComp<ExpaditionDataComponent>(consoleUid);
        if (data.Missions.Count == 0)
        {
            GenerateMissions(data);
        }

        _audio.PlayPredicted(component.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

        var missionParams = new SalvageMissionParams
        {
            Index = 0,
            Seed = _random.Next(),
            Difficulty = "Moderate",
        };

        //var difficulty = _prototypeManager.Index<SalvageDifficultyPrototype>(missionParams.Difficulty);
        //var mission = _salvage.GetMission(difficulty, missionParams.Seed);
        SpawnMission(missionParams, consoleUid);

    }

    private void SpawnMission(SalvageMissionParams missionParams, EntityUid station)
    {
        var cancelToken = new CancellationTokenSource();
        var job = new SpawnExpaditionJob(
            SalvageJobTime,
            EntityManager,
            _timing,
            _logManager,
            _prototypeManager,
            _anchorable,
            _biome,
            _dungeon,
            _metaData,
            _mapSystem,
            station,
            missionParams,
            new Tile(_tileDefinitionManager["FloorSteel"].TileId),
            cancelToken.Token);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);
    }

    private void UpdateExpaditions()
    {
        _salvageQueue.Process();

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            switch (job.Status)
            {
                case JobStatus.Finished:
                    _salvageJobs.Remove((job, cancelToken));

                    var dataComponent = EntityManager.GetComponent<ExpaditionDataComponent>(job.Station);
                    var mapId = dataComponent.ActiveMissions.Last().Key;

                    var roomMarker = Spawn("MaintsRoomMarker", new MapCoordinates(4f, 0f, mapId));
                    var mapPortal = Spawn("PortalRed", new MapCoordinates(4f, 0f, mapId));
                    if(TryComp<PortalComponent>(mapPortal, out var mapPortalComponent))
                        mapPortalComponent.CanTeleportToOtherMaps = true;

                    var returnMarker = _entityManager.AllEntities<ExpadReturnMarkerComponent>().FirstOrNull();

                    // Activate all expedition pads to teleport to the new map.
                    var enumerator = EntityManager.AllEntityQueryEnumerator<TransformComponent, ExpaditionPadComponent>();
                    while (enumerator.MoveNext(out var uid, out var transform, out var pad))
                    {
                        pad.TeleportMapId = mapId;
                        pad.ActivatedAt = _timing.CurTime;
                        pad.Portal = Spawn(pad.PortalPrototype, transform.Coordinates);

                        if(TryComp<PortalComponent>(pad.Portal, out var portal))
                            portal.CanTeleportToOtherMaps = true;

                        _link.OneWayLink(pad.Portal!.Value, mapPortal);
                        _audio.PlayPvs(pad.NewPortalSound, transform.Coordinates);

                        // Ensure that if no return marker is found we can still go back to the station.
                        if (returnMarker != null)
                            _link.OneWayLink(mapPortal, returnMarker.Value);
                        else
                            _link.OneWayLink(mapPortal, uid);
                    }
                    break;
            }
        }

        // Check for pads to clear.
        ClearPadsIfNeeded();
    }

    private void ClearPadsIfNeeded()
    {
        var enumerator = EntityManager.AllEntityQueryEnumerator<TransformComponent, ExpaditionPadComponent>();
        while (enumerator.MoveNext(out var transform, out var pad))
        {

            if (_timing.CurTime < pad.ActivatedAt + pad.ClearPortalDelay)
                continue;
            if (pad.Portal == null || Deleted(pad.Portal))
                continue;
            QueueDel(pad.Portal.Value);
            _audio.PlayPvs(pad.ClearPortalSound, transform.Coordinates);
        }
    }

    private void ClearAllStationPortalsNow()
    {
        var enumerator = EntityManager.AllEntityQueryEnumerator<TransformComponent, ExpaditionPadComponent>();
        while (enumerator.MoveNext(out var transform, out var pad))
        {
            if (pad.Portal != null && !Deleted(pad.Portal))
                QueueDel(pad.Portal.Value);

            _audio.PlayPvs(pad.ClearPortalSound, transform.Coordinates);
        }
    }

    private void GenerateMissions(ExpaditionDataComponent component)
    {
        component.Missions.Clear();

        for (var i = 0; i < MissionLimit; i++)
        {
            var mission = new SalvageMissionParams
            {
                Index = component.NextIndex,
                Seed = _random.Next(),
                Difficulty = "Moderate",
            };

            component.Missions[component.NextIndex++] = mission;
        }
    }
}

using System.Linq;
using System.Threading;
using Content.Server.Salvage.JobBoard;
using Content.Shared._PS.Terradrop;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Teleportation.Components;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._PS.Terradrop;

public sealed partial class TerradropSystem
{
    private const double SalvageJobTime = 0.002;
    private readonly List<(GenerateTerradropJob Job, CancellationTokenSource CancelToken)> _jobs = [];
    private readonly JobQueue _jobQueue = new();

    private void CreateNewTerradropJob(
        SalvageMissionParams missionParams,
        EntityUid station,
        EntityUid targetPad,
        Tile landingPadTile
    )
    {
        var cancelToken = new CancellationTokenSource();
        var job = new GenerateTerradropJob(
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
            targetPad,
            missionParams,
            landingPadTile,
            cancelToken.Token);

        _jobs.Add((job, cancelToken));
        _jobQueue.EnqueueJob(job);
    }

    private void UpdateTerradropJobs()
    {
        _jobQueue.Process();

        foreach (var (job, cancelToken) in _jobs.ToArray())
        {
            switch (job.Status)
            {
                case JobStatus.Finished:
                    _jobs.Remove((job, cancelToken));
                    OnJobCompleted(job);
                    break;
            }
        }

        // Check for pads to clear.
        ClearPadsIfNeeded();
    }

    private void OnJobCompleted(GenerateTerradropJob job)
    {
        var dataComponent = EntityManager.GetComponent<TerradropStationComponent>(job.Station);
        var mapId = dataComponent.ActiveMissions.Last().Key;

        // Spawn the room marker to make a new room where the portal will be.
        Spawn("TerradropRoomMarker", new MapCoordinates(4f, 0f, mapId));
        var mapPortal = Spawn("PortalRed", new MapCoordinates(4f, 0f, mapId));
        if (TryComp<PortalComponent>(mapPortal, out var mapPortalComponent))
            mapPortalComponent.CanTeleportToOtherMaps = true;

        var returnMarker = _entityManager.AllEntities<TerradropReturnMarkerComponent>().FirstOrNull();

        // Activate the target pad to teleport to the new map.
        if (!TryComp<TerradropPadComponent>(job.TargetPad, out var padComponent))
            return;
        var padTransform = Transform(job.TargetPad);

        padComponent.TeleportMapId = mapId;
        padComponent.ActivatedAt = _timing.CurTime;
        padComponent.Portal = Spawn(padComponent.PortalPrototype, padTransform.Coordinates);

        if (TryComp<PortalComponent>(padComponent.Portal, out var portal))
            portal.CanTeleportToOtherMaps = true;

        _link.OneWayLink(padComponent.Portal!.Value, mapPortal);
        _audio.PlayPvs(padComponent.NewPortalSound, padTransform.Coordinates);

        // Ensure that if no return marker is found we can still go back to the station.
        if (returnMarker != null)
            _link.OneWayLink(mapPortal, returnMarker.Value);
        else
            _link.OneWayLink(mapPortal, job.TargetPad);

    }

    private void ClearPadsIfNeeded()
    {
        var pads = _entityManager.EntityQuery<TransformComponent, TerradropPadComponent>();
        foreach (var (transform, pad) in pads.ToArray())
        {
            if (_timing.CurTime < pad.ActivatedAt + pad.ClearPortalDelay)
                continue;
            if (pad.Portal == null || Deleted(pad.Portal))
                continue;
            QueueDel(pad.Portal.Value);
            _audio.PlayPvs(pad.ClearPortalSound, transform.Coordinates);
        }
    }

    private void GenerateMissions(TerradropStationComponent component)
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

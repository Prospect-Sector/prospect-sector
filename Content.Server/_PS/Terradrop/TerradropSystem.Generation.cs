using System.Linq;
using System.Threading;
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
        TerradropMapPrototype mapPrototype,
        SalvageMissionParams missionParams,
        EntityUid station,
        EntityUid targetPad,
        Tile landingPadTile,
        int level = 0
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
            mapPrototype,
            missionParams,
            landingPadTile,
            level,
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
    }

    private void OnJobCompleted(GenerateTerradropJob job)
    {
        var dataComponent = EntityManager.GetComponent<TerradropStationComponent>(job.Station);

        // Spawn the room marker to make a new room where the portal will be.
        Spawn("TerradropRoomMarker", new MapCoordinates(4f, 0f, job.MapId));
        var mapPortal = Spawn("PortalRed", new MapCoordinates(4f, 0f, job.MapId));

        // Generate instance name: "{MapName} #{n}"
        var mapName = Loc.GetString(job.MapPrototype.Name);
        var existingCount = 0;
        if (dataComponent.ActiveMissions.TryGetValue(job.MapPrototype.ID, out var existingInstances))
            existingCount = existingInstances.Count;
        var instanceName = $"{mapName} #{existingCount + 1}";

        var missionData = new TerradropActiveMissionData(
            job.MapUid,
            job.MapId,
            mapPortal,
            instanceName
        );

        if (!dataComponent.ActiveMissions.ContainsKey(job.MapPrototype.ID))
            dataComponent.ActiveMissions[job.MapPrototype.ID] = new List<TerradropActiveMissionData>();
        dataComponent.ActiveMissions[job.MapPrototype.ID].Add(missionData);

        if (TryComp<PortalComponent>(mapPortal, out var mapPortalComponent))
            mapPortalComponent.CanTeleportToOtherMaps = true;

        dataComponent.ReturnMarker ??= _entityManager.AllEntities<TerradropReturnMarkerComponent>().FirstOrNull();

        // Activate the target pad to teleport to the new map.
        OpenPortalToMap(job.TargetPad, missionData);

        if (TryComp<TerradropMapComponent>(job.MapUid, out var mapComponent))
        {
            mapComponent.ReturnMarker = dataComponent.ReturnMarker;
            mapComponent.InstanceName = instanceName;
        }

        // Ensure that if no return marker is found we can still go back to the station.
        if (dataComponent.ReturnMarker != null)
            _link.OneWayLink(mapPortal, dataComponent.ReturnMarker.Value);
        else
            _link.OneWayLink(mapPortal, job.TargetPad);

        // Push UI update so open consoles show the new instance immediately.
        UpdateAllConsolesForStation(job.Station);
    }

    /// <summary>
    /// Only opens the portal TO the map, not the return portal.
    /// This is used when the player wants to open a portal to a existing map.
    /// </summary>
    private void OpenPortalToMap(EntityUid stationPadUid, TerradropActiveMissionData data)
    {
        if (!TryComp<TerradropPadComponent>(stationPadUid, out var padComponent))
            return;
        var padTransform = Transform(stationPadUid);

        padComponent.TeleportMapId = data.MapId;
        padComponent.Portal = Spawn(padComponent.PortalPrototype, padTransform.Coordinates);

        if (TryComp<PortalComponent>(padComponent.Portal, out var portal))
            portal.CanTeleportToOtherMaps = true;

        _link.OneWayLink(padComponent.Portal!.Value, data.MapPortalUid);
        _audio.PlayPvs(padComponent.NewPortalSound, padTransform.Coordinates);

    }

    private void GenerateMissionParams(TerradropStationComponent component)
    {
        component.Missions.Clear();

        var mapPrototypes = _prototypeManager.EnumeratePrototypes<TerradropMapPrototype>();
        ushort index = 0;
        foreach (var terradropMapPrototype in mapPrototypes)
        {
            var mission = new SalvageMissionParams
            {
                Index = index++,
                Seed = terradropMapPrototype.Seed ?? _random.Next(),
                Difficulty = terradropMapPrototype.SalvageDifficulty,
            };
            component.Missions[terradropMapPrototype.ID] = mission;
        }
    }
}

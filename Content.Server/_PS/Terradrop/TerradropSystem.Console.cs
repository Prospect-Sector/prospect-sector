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
            GenerateMissions(data);

        var missionParams = new SalvageMissionParams
        {
            Index = 0,
            Seed = _random.Next(),
            Difficulty = "Moderate",
        };
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

                // Found a pad to use.
                CreateNewTerradropJob(
                    missionParams: missionParams,
                    station: station,
                    targetPad: uid,
                    landingPadTile: landingPadTile
                );
                return;
            }
        }

        // No portals found.
        _audio.PlayPredicted(consoleComponent.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));
        _popup.PopupEntity(Loc.GetString("terradrop-console-no-portal"), consoleUid);
    }

}

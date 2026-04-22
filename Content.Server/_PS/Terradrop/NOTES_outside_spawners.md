# Spawning entities outside the generated dungeon (Terradrop)

Context for an AI adding point-of-interest spawners (or any entity) to
the *outdoor* portion of a terradrop map — the biome terrain surrounding
the BSP dungeon, not inside it.

## Map layout

`GenerateTerradropJob.Process` (`Content.Server/_PS/Terradrop/GenerateTerradropJob.cs`)
is the single entry point that builds a terradrop mission's map. Relevant
landmarks it creates on the map grid, in order:

1. `BiomeComponent` — procedural outdoor terrain (grass / caves / snow / etc.)
   seeded from `mission.Seed`. The biome is effectively infinite; tiles are
   generated lazily by `BiomeSystem` on demand when something asks for them.
2. Landing pad at world origin `(0, 0)` — a disc of radius `landingPadRadius = 6`
   tiles (`_padTile`). Tiles inside the disc are added to a local `reservedTiles`
   list so later spawn logic can avoid them.
3. BSP dungeon, placed at `dungeonOffset` which is:
   - Direction: `_dungeon.GetDungeonRotation(seed)` rotated from `(0, +1)`.
   - Distance: `minDungeonOffset + (maxDungeonOffset - minDungeonOffset) * rand01`,
     where `minDungeonOffset = landingPadRadius + 4 = 10` and
     `maxDungeonOffset = minDungeonOffset + 12 = 22`. So the dungeon centre is
     10–22 tiles from origin, in a random direction.
   - Footprint: `BspDungeonDunGen.Bounds` from the chosen biome's config in
     `Resources/Prototypes/Procedural/dungeon_configs.yml` (60×60 for most,
     65×65 for Mineshaft, 70×70 for Caves).

The **generated area** is therefore an axis-aligned `Bounds`-sized square
centred on `dungeonOffset`. Everything else on the map is biome + landing pad.

## What's inside `dungeon` after generation

`dungeon` is a `Content.Shared.Procedural.Dungeon`. After `GenerateDungeonAsync`
completes you can read:

- `dungeon.Rooms` — list of `DungeonRoom` (each with `Tiles`, `Bounds`, `Center`,
  `Exterior`, `Entrances`). Safe spawn space for prefab-interior loot.
- `dungeon.RoomTiles` / `dungeon.RoomExteriorTiles` / `dungeon.CorridorTiles` /
  `dungeon.CorridorExteriorTiles` / `dungeon.Entrances` — `HashSet<Vector2i>`
  over tile indices.
- `dungeon.AllTiles` — union of everything the BSP claims.

Treat `dungeon.AllTiles` as the **exclusion set** for "inside the dungeon".

## How existing spawners work

All inside-the-dungeon spawners in `GenerateTerradropJob` go through
`SpawnRandomEntry` (line ~380). It picks a random tile from `dungeon.AllTiles`
(falling back to `dungeon.Rooms[i].Tiles` for mobs/loot that need interior
space), checks tile freeness via `_anchorable.TileFree`, and spawns with
`_entManager.SpawnAtPosition(proto, _map.GridTileToLocal(grid, grid, tile))`.

`SpawnDungeonLoot` handles "Guaranteed" `SalvageLootPrototype`s and walks their
`LootRules` (RandomSpawns, etc.) — these are also scoped to the dungeon.

Nothing in the current code spawns on biome terrain. The landing pad is tiled
but doesn't spawn entities beyond the player portal (`PortalRed` anchored to a
`TerradropPad` inside the guaranteed landing prefab — see
`TerradropSystem.Generation.OnJobCompleted` and
`BspDungeonDunGen.GuaranteedPrefab`).

## Recommended pattern for outside spawners

Add a method alongside `SpawnRandomEntry` / `SpawnDungeonLoot` on
`GenerateTerradropJob`, for example:

```csharp
private async Task<EntityUid?> SpawnOutsideEntry(
    Entity<MapGridComponent> grid,
    IBudgetEntry entry,
    Dungeon dungeon,
    Vector2i dungeonCenter,
    Vector2i dungeonBounds,
    Random random)
{
    // Annulus: at least `innerRadius` from origin (skip landing pad) and at
    // least `clearance` from the dungeon's axis-aligned bounding box.
    const int innerRadius = 10;          // past the landing pad
    const int clearance = 4;             // breathing room from the dungeon wall
    const int outerRadius = 80;          // don't wander infinitely far

    for (var attempt = 0; attempt < 32; attempt++)
    {
        var angle = random.NextFloat() * MathF.PI * 2f;
        var radius = innerRadius + random.NextFloat() * (outerRadius - innerRadius);
        var tile = new Vector2i(
            (int)(MathF.Cos(angle) * radius),
            (int)(MathF.Sin(angle) * radius));

        // Reject anything inside the dungeon's footprint.
        if (Math.Abs(tile.X - dungeonCenter.X) <= dungeonBounds.X / 2 + clearance &&
            Math.Abs(tile.Y - dungeonCenter.Y) <= dungeonBounds.Y / 2 + clearance)
            continue;

        // Reject the landing pad.
        if (tile.LengthSquared() <= (landingPadRadius + 1) * (landingPadRadius + 1))
            continue;

        // Reject any tile the dungeon actually occupies (covers rotated/clipped cases).
        if (dungeon.AllTiles.Contains(tile))
            continue;

        // Force the biome to materialise a real tile here before spawning — biome
        // tiles are lazy; SpawnAtPosition onto a pure space tile will fall through.
        if (!_biome.TryGetBiomeTile(MapUid, grid, tile, out _))
            continue;

        if (!_anchorable.TileFree(grid, tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
            continue;

        var uid = _entManager.SpawnAtPosition(entry.Proto, _map.GridTileToLocal(grid, grid, tile));
        await SuspendIfOutOfTime();
        return uid;
    }
    return null;
}
```

## Gotchas

- **Biome tiles are lazy.** `BiomeComponent` doesn't fill the grid eagerly —
  tiles only exist after something reads them. Call `_biome.TryGetBiomeTile`
  (and/or `_biome.Preload(...)` for a region) before `SpawnAtPosition` or the
  entity lands on space and falls.
- **Dungeon rotation.** `_dungeon.GetDungeonRotation(seed)` is already applied
  inside `GenerateDungeonAsync`, so `dungeon.AllTiles` already contains the
  rotated tile set. The axis-aligned `dungeonBounds` rejection above is a
  superset check; `dungeon.AllTiles.Contains(tile)` is the precise one.
- **Don't reuse `dungeon.Rooms` for outside spawns.** Those are inside-prefab
  coordinates. Use the rejection-sampling pattern above.
- **Reserved tiles.** `GenerateTerradropJob` keeps a local `reservedTiles` list
  for the landing pad; it's not on the dungeon object. Either check against
  that list too or just use the landing-pad disc test.
- **Collision flags.** The constants `DungeonSystem.CollisionLayer` /
  `CollisionMask` are the same ones used by inside-dungeon spawners — reuse
  them so you fail the same way on blocked tiles.
- **Budget loop.** If you're hooking into `mobBudget`, mirror the
  `RandomSystem.GetBudgetEntry` drain loop that `SpawnRandomEntry` sits
  inside; otherwise just call your helper directly with a fixed count.
- **Async yielding.** Long inner loops must periodically `await
  SuspendIfOutOfTime()` — this is a `Job<T>` running on the shared job queue.

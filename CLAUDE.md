# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Prospect-Sector is a shallow copy of [space-wizards/space-station-14](https://github.com/space-wizards/space-station-14) (fork without forking). The repository is synced monthly with upstream's `stable` branch. It uses C# for game logic, YAML for prototypes/entities, and XML for UI.

## Build Commands

```bash
# Initial setup (run once after cloning)
python RUN_THIS.py

# Build the solution
dotnet build

# Run client (for local testing)
dotnet run --project Content.Client

# Run server (for local testing)
dotnet run --project Content.Server

# Run tests
dotnet test

# Run specific test project
dotnet test Content.Tests
dotnet test Content.IntegrationTests

# Run a single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Run YAML linter
dotnet run --project Content.YAMLLinter

# Run benchmarks
dotnet run --project Content.Benchmarks -c Release
```

## Architecture

### Project Structure
- **Content.Server** - Server-side game logic and systems
- **Content.Client** - Client-side rendering, UI, and input
- **Content.Shared** - Shared components/systems between client and server
- **Content.Server.Database** / **Content.Shared.Database** - Database models and migrations
- **RobustToolbox/** - Game engine submodule (do not modify)

### Resources Structure
- **Resources/Prototypes/** - YAML entity/prototype definitions
- **Resources/Prototypes/_PS/** - Prospect-specific prototypes (custom entities, maps, game rules)
- **Resources/Textures/** - Sprites in RSI (Robust Station Image) format
- **Resources/Locale/** - Localization files (.ftl Fluent format)
- **Resources/Audio/** - Sound effects and music

### UI
UI is defined in XAML files (`.xaml`) within the Client project using RobustToolbox's UI framework:
- Use `UIController` classes to manage UI elements (not entity systems directly)
- Controllers use `[Dependency]` and `[UISystemDependency]` for injection
- Use `{Loc 'key'}` markup extension for localization in XAML

### ECS Architecture
The game uses Entity Component System (ECS) via RobustToolbox. **Components contain only data; systems contain all logic.**

#### Components
- Must be `sealed partial class` with `[RegisterComponent]`
- Use `[DataField]` for YAML-configurable properties
- No logic or methods - treat as "tags with configurable data"
- No private members needed (no encapsulation since no internal logic)

```csharp
[RegisterComponent]
public sealed partial class MyComponent : Component
{
    [DataField]
    public string SomeValue = string.Empty;
}
```

#### Systems
- Singleton classes inheriting `EntitySystem` (or `SharedSystem` for shared code)
- Subscribe to events in `Initialize()`, unsubscribe in `Shutdown()`
- Use `[Dependency]` for injecting other systems
- Expose public methods for other systems to call

```csharp
public sealed class MySystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MyComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<MyComponent> ent, ref UseInHandEvent args)
    {
        // Handler logic
    }
}
```

#### Events
- **Directed events** (preferred): Raised on specific entities, matched by component type
- **Broadcast events**: System-wide notifications without entity binding
- Event class names must end in "Event"
- Types: Informative, Cancellable (`Cancelled` field), Handled (`Handled` field)

## Code Conventions

### C#
- PascalCase for classes, interfaces, public members
- camelCase for local variables
- `_underscorePrefix` for private fields
- Use `[Dependency]` for DI in systems
- Subscribe to events in `Initialize()`, handle cleanup in `Shutdown()`

### YAML
- 2-space indentation for prototype files
- 4-space indentation for RSI meta.json files
- .ftl files do not support inline comments; use line-above comments

## Prospect-Specific Guidelines

### Our Code (_PS folders)
All Prospect-owned code lives in `_PS` folders and does NOT need comment markers:
- `Content.Server/_PS/`
- `Content.Client/_PS/`
- `Content.Shared/_PS/`
- `Resources/Prototypes/_PS/`

### Upstream File Modifications (REQUIRED)
When modifying ANY upstream file, you MUST add comment markers for merge conflict resolution:

**Single line or few lines** - use inline comment:
```csharp
using Content.Server._PS.Terradrop; // Prospect
```
```yaml
moduleId: Gardening # Prospect
```

**Block of changes** - use start/end markers:
```csharp
// Prospect: signature colour consistency
if (TryComp<StampComponent>(uid, out var stamp))
{
    stamp.StampedColor = state.Color;
}
// Prospect: End
```
```yaml
# Prospect: droppable borg items
- type: DroppableBorgModule
  moduleId: Gardening
# Prospect: End
```

For large C# additions, consider using partial classes.

## Code Generation Warning

The project uses code generation for prototypes and entities. **Entity names and prototype IDs are string references that won't be validated at compile time.** Always double-check:
- Entity prototype IDs in C# code
- Component type references
- Prototype parent references in YAML

## Networking
- **Never trust client input** - validate everything server-side
- Use `[NetSerializable]` for network messages
- Use dirty flags for component state changes
- Implement client prediction where appropriate

### EntityUid vs NetEntity
- `EntityUid` is local only - use for all local entity references
- `NetEntity` is for network transmission only
- `EntityUid` is NOT `[NetSerializable]` - attempting to serialize throws an exception
- Use `GetNetEntity()` when sending, `TryGetEntity()` / `EnsureEntity<T>()` when receiving
- Never store `NetEntity` on components long-term (won't serialize to YAML)

### Appearance System (Client/Server Visuals)
For dynamic sprites, use the appearance system instead of syncing full sprite state:
- Server: `Appearance.SetData(entity, MyVisuals.State, value)`
- Client: Create `VisualizerSystem<T>` overriding `OnAppearanceChange()`
- For simple cases, use `GenericVisualizerSystem` with YAML configuration

## Adding New Content

### New Entity Pattern
1. Create YAML prototype in `Resources/Prototypes/_PS/Entities/`
2. Add components and configure properties
3. Add sprites to `Resources/Textures/` (RSI format with meta.json)
4. Add localization to `Resources/Locale/en-US/`
5. If custom behavior needed, create Component + System in `Content.Shared/_PS/` or `Content.Server/_PS/`

### Prototype Example
```yaml
- type: entity
  parent: BaseItem
  id: MyNewItem
  name: my new item
  description: A description of the item.
  components:
  - type: Sprite
    sprite: _PS/Objects/myitem.rsi
    state: icon
  - type: Item
    size: Small
```

### File Organization
Components and Systems go in folders directly under `Content.Server/_PS/`, `Content.Shared/_PS/`, or `Content.Client/_PS/` - never in the project root.
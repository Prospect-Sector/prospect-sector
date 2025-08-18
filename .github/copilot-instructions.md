# GitHub Copilot Instructions for Prospect-Sector

## Project Overview
Prospect-Sector is a Space Station 14 (SS14) game server/fork. This is a multiplayer game project built on the RobustToolbox engine using C# and YAML for content definitions.

## Code Style and Conventions

### C# Guidelines
- Follow C# naming conventions: PascalCase for classes, interfaces, and public members
- Use camelCase for private fields and local variables
- Prefix private fields with underscore (_privateField)
- Use explicit access modifiers (public, private, protected, internal)
- Favor composition over inheritance where appropriate
- Use async/await for asynchronous operations
- Follow the existing code patterns in the Content.Server and Content.Client namespaces

### YAML Content Files
- Use 2-space indentation for regular YAML prototype files
- Use 4-space indentation for RSI (Robust Station Image) meta.json YAML files
- Keep prototype definitions consistent with existing formats
- Always include proper prototype IDs and parent types
- Document complex prototypes with comments
- Follow the established prototype hierarchy

### RSI Files Structure
```yaml
version: 1
license: "CC-BY-SA-3.0"
copyright: "Author Name"
size:
    x: 32
    y: 32
states:
    - name: "icon"
      directions: 1
    - name: "equipped-OUTERCLOTHING"
      directions: 4
    - name: "inhand-left"
      directions: 4
    - name: "inhand-right"
      directions: 4
```

## Architecture Patterns

### Project Structure
- Content.Server: Server-side game logic and systems
- Content.Client: Client-side rendering and UI
- Content.Shared: Shared components and systems between client and server
- Resources/Prototypes: YAML-based entity and object definitions
- Resources/Textures: Game sprites and visual assets (RSI format)
- Resources/Audio: Sound effects and music
- Resources/Locale: Localization files (.ftl format)

### ECS (Entity Component System)
- Use the RobustToolbox ECS architecture
- Components should be data-only when possible
- Systems handle the logic and behavior
- Shared components go in Content.Shared
- Server-only systems in Content.Server
- Client-only systems in Content.Client

## Testing Requirements
- Write unit tests for new systems and components
- Integration tests for complex game mechanics
- Test both client and server implementations
- Ensure tests pass before submitting PRs
- Follow existing test patterns in Content.Tests
- Run local test server before pushing changes

## Documentation Standards
- Document all public APIs with XML comments
- Include summary, parameters, and return descriptions
- Document complex game mechanics in markdown files
- Update relevant wiki pages for user-facing features
- Comment YAML prototypes for clarity
- Maintain changelog entries for significant changes

## SS14-Specific Guidelines

### Component Development
- Inherit from Component or IComponent appropriately
- Register components properly with the component factory
- Use [RegisterComponent] attribute
- Implement proper serialization for networked components
- Use [DataField] for serializable properties
- Use [ViewVariables] for debug inspection

### System Development
- Inherit from EntitySystem or SharedSystem as appropriate
- Use proper dependency injection with [Dependency]
- Subscribe to events in Initialize(), unsubscribe in Shutdown()
- Handle prediction properly for client-side systems
- Use proper entity queries and filters
- Implement UpdateAfterAutoHandleState when needed

### Networking
- Be mindful of network traffic
- Use dirty flags for component state changes
- Implement proper client prediction where needed
- Validate all client inputs on the server
- Never trust the client
- Use [NetSerializable] for network messages

## Contributing Guidelines
- Follow the CONTRIBUTING.md guidelines
- Create feature branches from master
- Write descriptive commit messages
- Include PR descriptions with what, why, and how
- Link related issues in PRs
- Ensure CI checks pass
- Get code reviews before merging
- Test changes on local server
- Update relevant documentation

## Performance Considerations
- Avoid allocations in hot paths
- Use object pooling where appropriate
- Cache component references when used frequently
- Profile performance-critical code
- Optimize entity queries with proper filters
- Be mindful of texture atlas usage
- Limit per-frame operations

## Security Considerations
- Never trust client input
- Validate all commands and actions server-side
- Sanitize user-generated content
- Follow secure coding practices
- Report security issues privately to maintainers
- Check permissions for admin commands
- Validate prototype IDs and paths

## Git Workflow
- Branch naming: feature/description, fix/description, refactor/description
- Commit format: "Category: Brief description" (e.g., "Combat: Add stun baton charge system")
- Keep commits atomic and focused
- Rebase feature branches to keep history clean
- Squash commits when appropriate
- Sign commits when possible

## Common Patterns

### Adding New Items/Entities
1. Create YAML prototype in Resources/Prototypes
2. Add necessary components
3. Implement systems for behavior
4. Add sprites to Resources/Textures (RSI format)
5. Add localization strings to Resources/Locale
6. Test in-game functionality
7. Update admin spawn menus if applicable

### Creating New Systems
```csharp
public sealed class MySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MyComponent, MyEvent>(OnMyEvent);
        SubscribeLocalEvent<MyComponent, ComponentInit>(OnComponentInit);
    }
    
    private void OnComponentInit(EntityUid uid, MyComponent component, ComponentInit args)
    {
        // Initialization logic
    }
    
    private void OnMyEvent(EntityUid uid, MyComponent component, MyEvent args)
    {
        // Event handling logic
    }
}
```

### Creating Prototypes
```yaml
- type: entity
  parent: BaseItem
  id: MyNewItem
  name: my new item
  description: A description of the item.
  components:
  - type: Sprite
    sprite: Objects/Misc/myitem.rsi
    state: icon
  - type: Item
    size: Small
```

## Avoid
- Hardcoding magic numbers - use constants or CVars
- Blocking operations in the main thread
- Direct file I/O without proper abstraction
- Modifying core engine code when content-level solutions exist
- Creating overly complex inheritance hierarchies
- Tight coupling between systems
- Client-authoritative game state
- Forgetting to dispose of subscriptions
- Memory leaks from event handlers

## Dependencies
- Follow RobustToolbox and SS14 upstream dependencies
- Avoid adding unnecessary NuGet packages
- Discuss major dependency additions in issues first
- Keep dependencies up to date with upstream
- Use the package versions specified in Directory.Packages.props

## Resource Guidelines
- Optimize textures (use appropriate formats and sizes)
- Keep sprites at 32x32 pixels for standard items
- Use RSI format for all sprites
- Keep audio files compressed appropriately
- Follow the established directory structure
- Use descriptive filenames
- Credit asset creators properly in RSI metadata
- Include proper licensing information

## Localization
- Use localization system for all user-facing text
- Add entries to the appropriate .ftl files in Resources/Locale/en-US
- Follow Fluent syntax for localization files
- Support multiple languages where possible
- Use structured localization keys (e.g., "item-name-wrench")
- Never hardcode strings that users will see

## Current Development Focus
- Maintain compatibility with upstream SS14 changes
- Focus on stability and performance
- Enhance gameplay features specific to Prospect-Sector
- Improve player experience and reduce bugs
- Keep technical debt manageable

## Testing Checklist
Before submitting a PR, ensure:
- [ ] Code compiles without warnings
- [ ] All tests pass
- [ ] Changes tested on local server
- [ ] No obvious performance regressions
- [ ] Sprites display correctly
- [ ] Localization strings added
- [ ] Admin tools updated if needed
- [ ] Changelog entry added if applicable
<div class="header" align="center">  
<img alt="Space Station 14" width="880" height="300" src="https://github.com/Prospect-Sector/prospect-sector/blob/main/Resources/Textures/_PS/Logo/logo.png?raw=true">  
</div>

Prospect Sector is a fork of [Space Station 14](https://github.com/space-wizards/space-station-14) that runs on [Robust Toolbox](https://github.com/space-wizards/RobustToolbox), written in C#.

This is the primary repo for Prospect Sector.

If you want to host or create content for Prospect Sector, this is the repo you need. It contains both RobustToolbox and the content pack for development of new content packs.

## Links

<div class="header" align="center">  

[Discord](https://discord.prospect-sector.space/) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [Standalone Download](https://cdn.prospect-sector.space/fork/prospect-sector)  

</div>

## Documentation/Wiki

The [docs site](https://docs.spacestation14.com/) has documentation on SS14's content, engine, game design, and more.  

## Contributing

We are happy to accept contributions from anybody. Get in Discord if you want to help. We've got a [roadmap](https://discord.com/channels/1397939148731711488/1399001297147006998) that need to be done and anybody can pick them up. Don't be afraid to ask for help either!  
Just make sure your changes and pull requests are in accordance with the [contribution guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html).

## Building

1. Clone this repo:
```shell
git clone https://github.com/space-wizards/space-station-14.git
```
2. Go to the project folder and run `RUN_THIS.py` to initialize the submodules and load the engine:
```shell
cd space-station-14
python RUN_THIS.py
```
3. Compile the solution:  

Build the server using `dotnet build`.

[More detailed instructions on building the project.](https://docs.spacestation14.com/en/general-development/setup.html)

## License

All code for the content repository is licensed under the [MIT license](https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT).  

Most assets are licensed under [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) unless stated otherwise. Assets have their license and copyright specified in the metadata file. For example, see the [metadata for a crowbar](https://github.com/space-wizards/space-station-14/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).  

> [!NOTE]
> Some assets are licensed under the non-commercial [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/) or similar non-commercial licenses and will need to be removed if you wish to use this project commercially.

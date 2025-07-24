# cfGodotEngine

This project adapts the C# `cfEngine` libraries for use inside the Godot engine. It includes a logger, asset loading helpers, a flexible settings system, optional Google Drive mirroring and utilities for building state machines.

Documentation is generated with DocFX. See the `doc` folder for markdown sources.

## Setup

1. Clone this repository.
2. Obtain the base **cfEngine** libraries from [GitHub](https://github.com/cfengine/cfEngine) and place them alongside this folder or add them as a git submodule.
3. Add the C# files to your Godot project and compile the solution.
4. Enable the `CF_GOOGLE_DRIVE` symbol if you plan to use Drive Mirror features.
5. Build the project and explore the examples under `doc`.


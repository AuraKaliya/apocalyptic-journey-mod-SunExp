# ModConfig API

`Witch.Mod.ModConfig` is the setup object exposed to Lua and DLL MODs.

Source anchor:

- `开发参考资料/反编译文件夹v1.0.23715745/Witch/Mod/ModConfig.cs`

## Load-Time Role

During MOD loading, the game:

1. creates a Lua table for the MOD
2. binds `self` and `ModConfig`
3. runs `Scripts/Entry.lua` if present
4. calls `Setup`
5. loads `Scripts/Entry.dll` if present
6. invokes static methods marked `[ModInitialize]`
7. registers static methods marked with hook attributes

For DLL projects, this means `Entry.lua` is optional, while `Entry.dll` must be
named exactly `Entry.dll` in the published MOD's `Scripts/` folder.

## Data and Resource APIs

`SetDataConfig(id, newData)`

Updates a loaded config row by ID. It does not replace the `Id` field.

`ModifyDataConfig(id, key, value)`

Updates one field in a loaded config row.

`MergeDataConfig(source, target)`

Merges rows from one prefix into another prefix.

`RedirectSourcePath(originalPath, newPath)`

Redirects a resource path.

## Hook APIs

`AddMethodHookBefore(typeDotMethod, fn)`

Registers a before hook. Example target string shape: `SettingUI.OnEnable`.

`AddMethodHookAfter(typeDotMethod, fn)`

Registers an after hook.

DLL overloads can register hooks with `Action<ModHookContext>`, a type name and
method name, or a `Type` plus method name.

## DLL Attributes

`[ModInitialize]`

Marks a static method to be called during DLL setup. The method usually accepts
the current `ModConfig`.

`[HookBefore]` and `[HookAfter]`

Register static hook methods. Instance hooks receive the target instance as the
first argument when the adapter can bind it.

## Practical Rules

- Keep setup idempotent; MOD loading may happen around global config setup.
- Prefer explicit hook registration in `Hooks/*Runtime.cs` for larger systems.
- Verify hook target signatures against the decompiled snapshot before relying on them.
- For C# projects, keep the assembly name unique even though the output file is copied as `Entry.dll`.

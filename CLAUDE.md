# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

JACKDEAD is a Unity 6 game (editor `6000.3.12f1`, URP 17, new Input System 1.19) in a "Lethal Company"-style loop: the player spawns at a bus, explores a procedurally generated map, collects scrap to meet a money quota, gambles at slot machines, then pulls the departure lever to advance to the next, harder stage. There is a single scene, `Assets/Scenes/SampleScene.unity`. There is no README and no CLI build/lint setup.

## Commands

- Build/run/test is done through the Unity Editor (open the project with 6000.3.12f1; press Play on `SampleScene`). Scripts compile in `Assembly-CSharp` (runtime) and `Assembly-CSharp-Editor`; there are no `.asmdef` files and no tests under `Assets/`, although `com.unity.test-framework` is installed.
- Headless compile check (close the Editor first, since the project can't be opened twice): `Unity.exe -batchmode -quit -projectPath . -logFile -`.
- Do not edit or commit `Library/`, `Temp/`, `obj/`, `Logs/` or `UserSettings/`.

## Source encoding (important)

Most `.cs` files are **not UTF-8**. They are Shift-JIS/Windows-1252 with CRLF line endings, and Japanese comments, tooltips and log strings show as mojibake. `MapGenerator.cs` and `PlayerInteraction.cs` are UTF-8 (the latter with a BOM). The `Edit` and `Write` tools refuse to modify the non-UTF-8 files. Change those with a shell command that preserves the encoding, or ask the user before converting. Keep CRLF endings. UI strings, tooltips and logs are written in Japanese, so match that when adding text.

## Architecture

Code lives in `Assets/Scripts/`, grouped by domain: `Player/`, `Slot/`, `System/` (with `System/Map/`), `Item/`, and `ScriptableObjects/`. `Assets/Editor/MapGeneratorEditor.cs` is the custom inspector for the map generator. Persistent data assets live in `Assets/Data/` (`PlayerData.asset`, plus `Item/` and `Map/` assets).

**Wiring.** Components find each other at runtime with `FindAnyObjectByType<...>()` (for example `PlayerController`, `MapGenerator`, `DisplayTextManager`, `StageResultUIController` and `PlayerMovement`) rather than through a service locator or singletons. The map is spawned at runtime, so anything instantiated by `MapGenerator` (slot machines, scrap) must find the player this way and cannot rely on scene references.

**Stage loop.**
1. `MapGenerator.Start()` calls `GenerateMap()`. It runs a randomized DFS over a grid of `MapChipData` room chips (door flags plus optional rotation), and retries up to `maxRetryCount` until `targetRoomCount` rooms are placed. Each room is a prefab rotated about X by -90° (`RoomChipBasePitch`). Door matching happens in `FindAllFittingChips` and `GetRotatedDoors`.
2. After rooms are placed it runs, in order: `AnalyzeAllRoomsWalkableFloor` (raycasts to find each room's walkable floor Y), `SpawnBusAtOuterDoor` (places the bus and teleports the player there), `SpawnSlotMachines`, then `SpawnRandomScraps`. Placement uses physics raycasts and `Physics.SyncTransforms()`, so it depends on room prefab colliders. The floor must be a bright-colored surface (`MinFloorColorBrightness`) with an up-facing normal.
3. Scrap is spawned until its total `ItemDataAsset.Amount` reaches `currentMapQuota * quotaMultiplier`.
4. `DepartureLever` (an `IInteractable`) evaluates the quota. It calls `PlayerController.OnStageCleared` (sanity recovery, `PlayerData.TotalRepaidDebt` and `CurrentStageLevel` bookkeeping), shows `StageResultUIController`, and then calls `MapGenerator.AdvanceToNextLevel()`. That raises the stage level, multiplies the quota by `quotaGrowthRate` (rounded to 100), and regenerates the map by destroying all children of the generator.

**Player.** `PlayerController` holds stats (health, sanity, dopamine, stamina), money, a 3-slot inventory of `ItemDataAsset`, and the input `InputActionAsset` (`Assets/PlayerInputActions.inputactions`, action map `"Player"`). Sanity drains over time, and dopamine is consumed before sanity (`ConsumeSanity`). Runtime state is mirrored into the `PlayerDataAsset` ScriptableObject (`SaveMoneyToData` and `SaveInventoryToData`) so it survives map regeneration. `PlayerMovement` handles locomotion and `LockControlAndMoveTo` and `UnlockControl`, which the slot machine uses to pin the camera. `PlayerInteraction` raycasts from the camera against `IInteractable` (found on the collider or its parent), with optional hold-to-interact durations. `PlayerUIController` and `DisplayTextManager` handle HUD and text.

**Interaction.** Anything the player can use implements `IInteractable` (`DepartureLever`, `SlotButtonInteractable`). Dropped or spawned items need the `Items` physics layer (`LayerMask.NameToLayer("Items")`) and an `Item` component carrying their `ItemDataAsset`. `MapGenerator` ignores colliders under `SlotMachineController`, `Item` and `PlayerController` when it measures the floor.

**Slots.** `SlotMachineController` drives a coroutine sequence (`PlaySlotSequence`): it deducts the bet, moves the player to the front of the machine, rolls an outcome, and builds each reel strip as a runtime `Texture2D` that `SlotReelScroller` scrolls. Win rates are derived from each symbol's multiplier and the `loseRate` (`RecalculateWinRates`, also run in `OnValidate`). A `Reroll` symbol repeats the spin without a new bet. Reel sprite textures must have Read/Write enabled, or `DrawSpriteIntoVerticalCell` silently skips them. `MapGenerator` auto-orients spawned slot machines by detecting the front from the positions of their `SlotButtonInteractable` and `SlotReelScroller` children.

**Data.** `ItemDataAsset`, `PlayerDataAsset` and `MapChipData` are ScriptableObjects under `Scripts/ScriptableObjects/`. `PlayerDataAsset` is mutated at runtime (money, inventory, stage level). Note that ScriptableObject changes persist in the Editor between Play sessions.

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Language

Always reply to the user in Japanese (日本語で返答すること).

## Project

JACKDEAD is a Unity 6 game (editor `6000.3.12f1`, URP 17, new Input System 1.19) in a "Lethal Company"-style loop: the player spawns at a bus, explores a procedurally generated map, collects scrap to meet a money quota, gambles at slot machines, then pulls the departure lever to advance to the next, harder stage. There is a single scene, `Assets/Scenes/SampleScene.unity`. There is no README and no CLI build/lint setup.

## Commands

- Build/run/test is done through the Unity Editor (open the project with 6000.3.12f1; press Play on `SampleScene`). Scripts compile in `Assembly-CSharp` (runtime) and `Assembly-CSharp-Editor`; there are no `.asmdef` files and no tests under `Assets/`, although `com.unity.test-framework` is installed.
- Headless compile check (close the Editor first, since the project can't be opened twice): `Unity.exe -batchmode -quit -projectPath . -logFile -`.
- Do not edit or commit `Library/`, `Temp/`, `obj/`, `Logs/` or `UserSettings/`.

## Source encoding (important)

All `.cs` files under `Assets/` are UTF-8 (they were converted from Shift-JIS), and most have CRLF line endings. Save new or edited scripts as UTF-8 so the Japanese comments, tooltips and log strings don't turn into mojibake. UI strings, tooltips and logs are written in Japanese, so match that when adding text.

## Architecture

Code lives in `Assets/Scripts/`, grouped by domain: `Player/`, `Slot/`, `System/` (with `System/Map/`), `Item/`, and `ScriptableObjects/`. `Assets/Editor/MapGeneratorEditor.cs` is the custom inspector for the map generator. Persistent data assets live in `Assets/Data/` (`PlayerData.asset`, plus `Item/` and `Map/` assets).

**Wiring.** Components find each other at runtime with `FindAnyObjectByType<...>()` (for example `PlayerController`, `MapGenerator`, `DisplayTextManager`, `StageResultUIController` and `PlayerMovement`) rather than through a service locator or singletons. The map is spawned at runtime, so anything instantiated by `MapGenerator` (slot machines, scrap) must find the player this way and cannot rely on scene references.

**Stage loop.**
1. `MapGenerator.Start()` calls `GenerateMap()`. It runs a randomized DFS over a grid of `MapChipData` room chips (door flags plus optional rotation), and retries up to `maxRetryCount` until `targetRoomCount` rooms are placed. Each room is a prefab rotated about X by -90° (`RoomChipBasePitch`). Door matching happens in `FindAllFittingChips` and `GetRotatedDoors`.
2. After rooms are placed it runs, in order: `AnalyzeAllRoomsWalkableFloor` (raycasts to find each room's walkable floor Y), `SpawnBusAtOuterDoor` (places the bus and teleports the player there), `SpawnSlotMachines`, then `SpawnRandomScraps`. Placement uses physics raycasts and `Physics.SyncTransforms()`, so it depends on room prefab colliders. The floor must be a bright-colored surface (`MinFloorColorBrightness`) with an up-facing normal.
3. Scrap is spawned until its total `ItemDataAsset.Amount` reaches `currentMapQuota * quotaMultiplier`. Scrap sell values are not read from `ItemDataAsset.Amount` directly. Use `MapGenerator.GetScrapValue(item)` (or `Item.CurrentValue`), which applies the per-level price multiplier. Slot bets also go through `MapGenerator.ScaleToCurrentPrice`.

Difficulty is driven by `MapGenerator` settings. The room count rises linearly from `minRoomCount` (5, at level 1) to `maxRoomCount` (40, at `maxStageLevel`, 20), and stays there after that. The base quota rises exponentially from `baseQuotaLevel1` (500) to `baseQuotaFinalLevel` (280,000), with ±`quotaRandomRange` randomness rolled once per level. The price multiplier rises from 1 to `priceMultiplierFinalLevel` (150) over the same levels. `TotalDebt` is the sum of the base quotas for levels 1 to 20 (about $990,000). Clearing the debt does not end the game: past `maxStageLevel` the room count stays at the maximum, and the quota and prices keep growing by `postMaxLevelGrowthRate` per level (the result screen then shows net profit instead of remaining debt). The player dies (the game-over flow) when health or sanity reaches 0, or when falling below `PlayerController.fallDeathHeight`. Slot machines only spawn from `slotStartLevel` (5) onward. If the target room count can't be generated, `GenerateMap` lowers the target step by step, up to 5 times, instead of leaving the map without a bus.
4. `DepartureLever` (an `IInteractable`) evaluates the quota. It calls `PlayerController.OnStageCleared` (sanity recovery, `PlayerData.TotalRepaidDebt` and `CurrentStageLevel` bookkeeping), shows `ResultUIController` (which must exist in the scene). After the result animation, that controller fades to black, calls `MapGenerator.AdvanceToNextLevel()` while the screen is black, and fades back in. The same controller runs the game-over flow: `PlayerController` triggers it when health or sanity hits 0. It fades out, shows GAME OVER, calls `PlayerController.ResetForNewRun()`, and loads the scene named by `titleSceneName` (default `TitleScene`, which must be in Build Settings; it falls back to reloading the current scene). `AdvanceToNextLevel()` raises the stage level, multiplies the quota by `quotaGrowthRate` (rounded to 100), and regenerates the map by destroying all children of the generator.

**Player.** `PlayerController` holds stats (health, sanity, dopamine, stamina), money, a 3-slot inventory of `ItemDataAsset`, and the input `InputActionAsset` (`Assets/PlayerInputActions.inputactions`, action map `"Player"`). Sanity drains over time, and dopamine is consumed before sanity (`ConsumeSanity`). Runtime state is mirrored into the `PlayerDataAsset` ScriptableObject (`SaveMoneyToData` and `SaveInventoryToData`) so it survives map regeneration. `PlayerMovement` handles locomotion and `LockControlAndMoveTo` and `UnlockControl`, which the slot machine uses to pin the camera. `PlayerInteraction` raycasts from the camera against `IInteractable` (found on the collider or its parent), with optional hold-to-interact durations. `PlayerUIController` and `DisplayTextManager` handle HUD and text.

**Interaction.** Anything the player can use implements `IInteractable` (`DepartureLever`, `SlotButtonInteractable`). Dropped or spawned items need the `Items` physics layer (`LayerMask.NameToLayer("Items")`) and an `Item` component carrying their `ItemDataAsset`. `MapGenerator` ignores colliders under `SlotMachineController`, `Item` and `PlayerController` when it measures the floor.

**Slots.** `SlotMachineController` drives a coroutine sequence (`PlaySlotSequence`): it deducts the bet, moves the player to the front of the machine, rolls an outcome, and builds each reel strip as a runtime `Texture2D` that `SlotReelScroller` scrolls. Win rates are derived from each symbol's multiplier and the `loseRate` (`RecalculateWinRates`, also run in `OnValidate`). A `Reroll` symbol repeats the spin without a new bet. Reel sprite textures must have Read/Write enabled, or `DrawSpriteIntoVerticalCell` silently skips them. `MapGenerator` auto-orients spawned slot machines by detecting the front from the positions of their `SlotButtonInteractable` and `SlotReelScroller` children.

**Data.** `ItemDataAsset`, `PlayerDataAsset` and `MapChipData` are ScriptableObjects under `Scripts/ScriptableObjects/`. `PlayerDataAsset` is mutated at runtime (money, inventory, stage level). Note that ScriptableObject changes persist in the Editor between Play sessions.

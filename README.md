# Maple Field Guide

A Windows lookup panel for MapleStory Classic World. It reads your installed client and works over windowed or borderless MapleStory. It does not change game files or read game-process memory.

## Run

To build from source, clone the repository and run the launcher (see build requirements below):

```powershell
git clone https://github.com/alksandr/MapleStoryClassicUIHelper.git
cd MapleStoryClassicUIHelper
.\RunHelper.cmd
```

Double-click `dist/MapleStoryClassicUIHelper.exe`, or `RunHelper.cmd`.

The first launch imports the game archives, normally in a few seconds. The default game folder is `C:\Nexon\Library\maplestorycw\appdata`. Change it in **Settings** if needed. The helper then downloads available community drop reports; lookup remains usable during that refresh.

While MapleStory is focused, press **Ctrl+Shift+Space** to open or close the guide. Reopening with the shortcut selects the existing query so typing replaces it. Type a name or numeric ID. **Ctrl+L** focuses the search bar and selects the whole query. Use **↑ / ↓** and **Enter**, or click a result. Related records are buttons; **Back** returns to the previous record. **Esc** hides the guide and returns focus to the game.

Use **Settings** to change the shortcut or rebuild the cache. If a shortcut is already taken, choose another. The system-tray icon opens the guide, opens Settings, or quits. Double-clicking the executable again opens the existing instance.

The guide follows the game while opened with its shortcut. It hides when the game minimizes or you switch applications, and returns when you return to MapleStory. Opening from the tray also allows standalone lookup. Exclusive fullscreen is not supported.

## Boss respawn timers

Search a monster marked as a boss and choose **Add timer**. Select a linked map and enter a positive channel number. Empty duration fields are filled from your saved boss/map duration, then agreeing positive client spawn timings, then a bundled community estimate. Adjust the hours/minutes/seconds if needed and choose **Start timer**. Starting the same boss/map/channel again restarts its existing countdown.

Suggestions show their source. Bundled [NiaMeowDB field-boss estimates](https://meowdb.com/msclassic/bosses), verified on 2026-10-31, use 60 minutes for Mano, Stumpy, Mushmom, Shade, Zombie Mushmom and Rotten Mushmom, and 180 minutes for Jr. Balrog, matched to their specific maps. Community ranges use the earliest time (60 minutes for a reported 60–120-minute range); the boss may appear later. Suggestions work offline and do not add missing bosses/maps to the client catalog. Unknown timings remain blank.

The guide fills only a completely empty duration and never mixes suggestions with partial entries. Map changes preserve each map's duration draft within the dialog. **Start timer** saves the duration you actually choose for future use; opening, changing maps, or cancelling saves nothing. Editing an existing timer keeps that timer's duration first.

Open **Boss timers** from the title bar or tray menu. Each timer shows its channel, map, countdown and local due time. **Killed now** restarts it; **Edit** changes its channel/duration and restarts it; **Remove** deletes it. Editing into an existing channel merges the timers. Switching back to **Field guide** preserves your lookup; **Ctrl+L** also returns to search.

Use **Add channel** on any timer to track the same boss/map on another channel without returning to boss details. The dialog suggests the first unused positive channel, keeps your saved duration, and focuses the channel field. Press **Enter** to start it, or adjust the values first. Existing countdowns are unchanged unless you explicitly choose an already tracked channel, which restarts that channel's timer. You can repeat **Add channel** directly from the timer list, including when client data is unavailable.

Timers are manual estimates, not live spawn detection or confirmed server respawn times. Expired timers stay marked **Respawn due · estimated** until restarted or removed. Deadlines survive hiding, sleep and app restarts; no alerts can run while the helper is closed. On reopening, overdue timers generate one combined catch-up alert. Alerts do not open the guide or take focus. **Settings → Play a sound when a boss timer is due** controls sound. Windows notification settings may suppress tray alerts.

Timers and remembered durations live in `%LOCALAPPDATA%\MapleStoryClassicUIHelper\boss-timers.json`, independently of the game-data cache. Timers exclude world; use them within your current world.

## Data

- Items, equipment, monsters, maps, NPCs, quests, skills, stats, icons, and minimaps come from your local client, including its `.wz` archives and newer `.ms` packs.
- Monster/map/NPC connections, map exits, quest requirements/rewards, and skill levels are linked where the client supplies them.
- Monster details include **Best maps · estimated**. Click a ranked map to inspect its minimap. Scores weight target spawn placements (45%), target density across the full map bounds (35%), and coordinate compactness (20%). Counts/density are normalized against this monster's maps with complete geometry. Layout is `1 / (1 + horizontal spread / 1200 + vertical spread / 300)`; less spread and climbing score higher. Raw counts, target share, density, and spread are shown. Missing bounds/coordinates remain unranked; negative `mobTime` placements are excluded and positive timed placements are flagged. These are client placements, not measured live populations or kills/hour; server spawn caps, respawn speed, traversable routes, class mobility, and other players are not modeled.
- Some client records are unused or unreleased. Presence in the client is not proof that something is currently obtainable.
- Community drops come from [MapleClassic Wiki](https://mapleclassic.wiki/) under its [CC BY-NC-SA 4.0 notice](https://creativecommons.org/licenses/by-nc-sa/4.0/). Reports show their source, reported game version, and retrieval date. Rates are observations, not official drop probabilities. Unmatched or ambiguous records are skipped.
- **Refresh drop reports** updates the cached community data. Offline failures keep the previous reports. Missing reports do not mean there are no drops.

Records, artwork, settings, and the current-session error log are stored in `%LOCALAPPDATA%\MapleStoryClassicUIHelper`. Updated game-file metadata triggers a rebuild. Failed or cancelled rebuilds preserve the previous snapshot. Cache files and game assets are not included in the source distribution.

## Build and verify

Requires Windows x64, PowerShell, Git, and internet for the initial tool/dependency download. `build.ps1` obtains a repository-local .NET 10.0.402 SDK and the pinned archive reader; no machine-wide SDK installation is needed.

```powershell
.\build.ps1                 # Core checks and release build
.\build.ps1 -Publish        # Self-contained executable in dist/
.\build.ps1 -VerifyUi       # Opens the real UI and checks navigation/artwork/keyboard behavior
```

Quit an existing helper before running UI verification. Published executables include their runtime and do not require .NET to be installed.

Additional checks against your real installation and the live wiki:

```powershell
.\.tools\dotnet\dotnet.exe run --project tests/Verify.csproj -- --import 'C:\Nexon\Library\maplestorycw\appdata' artifacts/import
.\.tools\dotnet\dotnet.exe run --project tests/Verify.csproj -- --wiki artifacts/import
```

UI verification saves `artifacts/helper-preview.png`. The session log is `%LOCALAPPDATA%\MapleStoryClassicUIHelper\session.log`.

Run timer UI checks alone with `.\.tools\dotnet\dotnet.exe run --project tests/Ui/Ui.csproj -c Release -- --boss-timers`; they use temporary data and do not change your saved timers. `--timer-benchmark` on `tests/Verify.csproj` measures sparse checks and dense expiry at 1,000 and 10,000 timers. For isolated verification, `MAPLE_HELPER_DATA_ROOT` overrides the application-data folder.

Source layout: `src/Core` contains import/search/cache/wiki behavior; `src/App` contains the WPF panel and Win32 window metadata/hotkey integration; `src/Reader` compiles the pinned upstream reader. Tests use small synthetic records and temporary damaged archives; the optional import check uses your own game assets.

## Boundaries

Personal local use. No crafting calculators, market tools, gameplay automation, client modification, or injected overlay. The initial built-in integration probe did not find a supported add-on interface. This is an independent helper, not a Nexon product. Third-party and game-content notices are in `THIRD-PARTY-NOTICES.md`.

Nexon has not approved this helper. Its [Terms of Use](https://www.nexon.com/main/en/legal/tou) restrict unauthorized modification and data mining; using an external window does not establish permission to extract client data. No injection or anti-cheat detection testing was performed. An injectable build requires separate authorization from Nexon before it can be presented as permitted.

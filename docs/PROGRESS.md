# MapleStory Classic UI Helper implementation

Approved spec: the user's implementation plan in this chat.

- Bootstrap: .NET 10.0.402 installed repository-locally; WzComparerR2 pinned to 621251801cb14a423ad6737d3cdeabb616b1635c.
- Ruling: compile only the upstream WzLib sources as a .NET 10 library, retaining its MIT notice; avoid pulling in the editor and its unrelated rendering/audio dependencies.
- Tasks: (1) archive reader/import/cache/search, (2) community drops, (3) native overlay, (4) checks/package/docs.
- Pre-flight: importer produces category-qualified records consumed by search/UI; cache stores that same catalog. Community reports must not override client stats.
- No existing repository or worktree was present; implementation stays in the user's requested directory.

## Completed verification

- Actual WZ/MS import: 6,381 records: 1,206 items; 3,968 equipment; 118 monsters; 347 maps; 301 NPCs; 343 quests; 98 skills. Artwork available in every category.
- Real Slime stats (HP 115), skill level descriptions, NPC/quest links, minimaps, external image links, and UOL-cycle protection pass.
- Core suite: ranking, duplicate names/category IDs, game text, cancellation/changed-source/interrupted-pointer preservation, malformed wiki responses, identity verification/unknown rates, shortcut validation, and damaged archive cleanup pass.
- Live wiki: 112 verified reports across 19 monsters.
- WPF verification: search, arrows/Enter, artwork/stats, map/back navigation, Escape/reopen, and retained search pass.
- Fresh-context review identified four P2 issues. Reproduced them and fixed quest-stage semantics, cached reports when source validation fails, immediate Enter search, and per-report source links. Import fingerprint bumped to v2.
- Runtime regression: a reopened panel over foreground MapleStory was not topmost. Reproduced with real foreground metadata; reopening now selects game-overlay mode and stays above the game. Startup uses the same foreground-aware placement.
- The missing-game-folder check uses a deliberately nonexistent temporary path and restores settings afterward. The real game folder remains readable and is never modified.

- Actual game shortcut check opened the guide over MapleStory. It exposed automatic-placement drift; position is now saved only after a user move/resize completes. Startup/reopen/programmatic-move regression checks pass, and the visible guide has a taskbar entry. Reset test-induced drift to the default 36/54 offsets.
- Republished the self-contained executable after all core and WPF checks passed. Nexon approval is not established for injection or archive extraction; documented this boundary in README.

Pending: live recheck of the packaged placement fix. No live game restart or mixed-monitor DPI manipulation has been performed.

## Monster map ranking

- Imported per-map monster placements (ID, X, ground Y, optional mobTime) and VR bounds. Import fingerprint v3 rebuilds older caches while retaining their fallback snapshot.
- Monster details rank maps by 45% relative count, 35% relative density, and 20% coordinate compactness, showing raw inputs and links to minimaps. Unknown geometry is unranked; negative mobTime is excluded and positive timed placements are flagged. Rankings estimate client layout suitability, not live farming throughput.
- Actual installation reimport and geometry assertions pass (6,381 records). Slime's first result is Dungeon, Southern Forest I, 21/21 target placements. Core ranking and WPF ranking/minimap/back checks pass; self-contained package rebuilt. Preview: artifacts/map-ranking-preview.png.
- No server population, spawn-cap, respawn-rate, class-routing, or kills/hour measurements were performed.

## Boss respawn timers · 2026-10-31

- Added manual boss/map/channel countdowns, remembered boss/map durations, restart/edit/merge/remove actions, and a separate timer view that retains lookup state. Timer storage is independent of imported client data and uses UTC deadlines and atomic writes.
- Added combined tray alerts with native balloon sound disabled; the application controls the optional sound. Hidden expiry does not show or activate the guide. Overdue startup timers consume durable notification receipts once. Timers need the helper running to deliver alerts; Windows can suppress balloons.
- Core checks pass for independent channels, duplicate restart, edit collision, deadline boundaries/clock advancement, restart catch-up, persisted labels/durations/receipts, corrupt/missing storage, and failed state/receipt writes.
- Release builds passed with zero warnings/errors. Isolated native WPF checks passed for blank/invalid duration, add, view preservation, tray navigation, Ctrl+L, restart/edit/remove, hidden expiry with unchanged foreground, mute-setting display, and overdue startup without client data. Existing lookup UI checks passed against a copied cache with an intentionally missing game folder. User data was not modified. Preview: `artifacts/boss-timers-preview.png`.
- Measured Core checks at N=1,000 and N=10,000 with tiered compilation disabled for stable comparison: idle scan 0.0009 → 0.0121 ms/tick (13.4× for 10× N, approximately linear), 0 B/tick and zero Gen0 collections. Dense expiry including an atomic receipt write took 11.127 → 17.473 ms (1.57×; disk overhead dominates); allocations 114,000 → 1,244,608 B (10.9×), zero Gen0 collections. Both sparse/dense algorithms are O(N); the benchmark excludes WPF rendering. Reproduce with `COMPlus_TieredCompilation=0` and `tests/Verify.csproj --timer-benchmark`.
- Independent review caught an explicit tray-navigation regression; fixed and covered by a UI assertion. Physical sound/mute, visible Windows balloon delivery, Explorer restart, and actual machine suspend/resume were not manually verified; hidden expiry and simulated clock advancement were tested.

## Quick timer suggestions · 2026-10-31

- Empty duration fields now use current timer → saved boss/map duration → agreeing positive client timing → bundled community fallback. Partial entries are never mixed with suggestions; each map keeps its dialog draft. Opening/selecting/cancelling writes nothing; Start reuses the existing atomic timer/duration save.
- Bundled fallback data covers six one-hour field bosses and three-hour Jr. Balrog by exact boss/map IDs. [NiaMeowDB](https://meowdb.com/msclassic/bosses) was checked on 2026-10-31; suggestions expose the source/date and use the earliest time for reported 60–120-minute ranges. Unknown bosses/maps remain unknown. No runtime fetch or catalog/schema migration was added.
- Core tests pass for precedence, ID matching, ignored invalid values, conflicting client placements, earliest-range fallback, and provenance. Isolated WPF checks pass for client/community prefill, labels/link, partial edits, map drafts, cancellation, empty-submit reapplication, and saved overrides across restart. Existing timer/lookup regressions and Release builds pass with zero warnings/errors. Independent review found no actionable issues.
- Suggestion lookup runs only on dialog open, map selection, or submission, with O(S) work for S placements on the selected map; the recurring deadline/rendering paths are unchanged. Browser launch and physical notification/sound acceptance were not rerun. Lookup verification now supports `--isolated` with a separate `MAPLE_HELPER_DATA_ROOT`, avoiding the user's running app singleton.

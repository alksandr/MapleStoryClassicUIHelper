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

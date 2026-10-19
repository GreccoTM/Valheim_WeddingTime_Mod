# Changelog

## 0.6.0
Requires every player and the server to update (network messages changed).

- Fixed: the bond made every stamina cost (running, attacks, blocking, dodging, jumping, swimming, sneaking, tools) more expensive instead of cheaper: +100% at levels 1–6 and +75% from level 7, which hid the stamina regeneration bonus. Valheim treats these values as additions to the cost, so levels 1–6 now leave costs unchanged and level 7 makes them 25% cheaper (50% with the upgraded ring).
- Fixed: the Wedding Bond tooltip no longer repeats its effects with the game's generic stat lines.
- Fixed: with mods that allow several utility items, wearing two Wedding Rings now uses the upgraded one if either is upgraded.
- Level 10: the glow now also clears Mistlands mist around the player, like a Wisplight with 60% of its radius. It works without a Wisplight and every client sees the cleared area.
- Performance: the level 10 glow no longer scans every player each second. Each player object reads the bond state once when it appears, and the owner broadcasts an RPC only when the state changes; dedicated servers never create the light. The ring-upgrade patch on `Inventory.AddItem` exits before doing any work for other items.
- Fixed: the level 5 damage resistance now applies in multiplayer no matter who owns the attacking creature. It used to be computed on the attacker's machine, which cannot see the victim's bond.
- Fixed: other players now see your level 10 glow. Bond state (active, level, upgraded) is published on the player's network object instead of being read from a remote inventory that is never synced.
- The Wedding Bond only turns on after the server has saved the marriage. Each client keeps checking its state against the world save and clears an engagement the server never confirmed, or a marriage that no longer exists.
- A partner who was offline when a divorce became final is told the next time they join the world.
- A pending divorce is saved in the world and counts real time, so it survives a server restart and cannot be skipped by sleeping.
- The server now rejects every change to Wedding Time world keys that does not come from the mod itself (vanilla accepts global-key changes from any client), so a modified client can no longer forge a marriage, progress or a divorce. As a side effect, the vanilla `setkey`/`removekey` dev commands cannot touch these keys either, even on the host; use `wedding_admin_divorce` instead.
- `wedding_admin_divorce` is resolved by the server: it accepts any online player's name (not only nearby ones) or a player ID for offline players, and confirms to the admin.
- Shared progress is written before the previous value is removed, so a crash at the wrong moment can no longer reset it.
- All player-facing messages, the status effect name and its tooltip are localized (English, Brazilian Portuguese). Server messages are sent as text keys, so each player reads them in their own language.
- `/wedding_time` names your partner.
- The ring upgrade cost is now part of the recipe itself instead of being patched into the live ObjectDB by a background loop.
- Internal: save-key parsing and level math moved to `WeddingKeys` with unit tests; `Plugin.cs` split into ring, bond and glow classes; the version comes from a single place (`WeddingTime.csproj`) and the build checks the Thunderstore manifest against it.

## 0.5.11
- The Wedding Ring can no longer be sold to traders (value set to 0).
- Added `[NetworkCompatibility]`: every player must have the same minor version.
- `DaysTogetherPerLevel` is now a server-synced config entry (the old world global-key workaround is removed and cleaned up automatically).
- New config entries `Upgrade/CoinCost` and `Upgrade/JadeCost` for the ring upgrade.
- Ring name and description are localized (English, Brazilian Portuguese).
- Marriages are now saved on the server only after both partners confirm, so a client can no longer marry another player alone. Players already married are rejected.
- The bond refreshes immediately on equip/unequip; the background check runs every 2 s instead of every 1 s.
- Skill bonus only applies to the local player's own Skills; cached reflection lookups; less log noise.
- Dedicated servers: marriage confirmation and level-up celebrations now resolve players through the peer roster, so they work for characters the server has not loaded nearby (previously the celebration could use the world origin and a placeholder name).
- If the server rejects a marriage (for example one partner is already married), both clients now clear the local engagement and show a message instead of staying engaged until relog.
- A marriage that only one partner confirmed now expires on the server after about 2.5 minutes, and whoever confirmed is told their partner did not answer in time.
- New `/divorce`, `/divorce confirm` and `/divorce cancel` commands with a configurable waiting period (`[Marriage] DivorceDelaySeconds`, default 300 s), plus the admin command `wedding_admin_divorce <player name>`.

# Changelog

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

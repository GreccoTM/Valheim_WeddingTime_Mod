# Wedding Time

A multiplayer wedding mod for Valheim: propose to another player, craft Wedding Rings for each other and grow a shared Wedding Bond that gains new perks the longer you play together. Every player and the server must run the same minor version of the mod.

## Development environment

- Valheim 1.0.16
- BepInExPack Valheim 5.4.2351 (Mono)
- Jötunn 2.30.2
- .NET SDK 10.0

## Build and test

```sh
dotnet build                                  # uses the Valheim path set in WeddingTime.csproj
dotnet build -p:ValheimDir=/path/to/Valheim   # or point it at your own install
dotnet test tests/WeddingTime.Tests           # unit tests for the save-key and level logic
```

The plugin is written to `artifacts/netstandard2.1/WeddingTime.dll`; copy it to Valheim's `BepInEx/plugins` directory to test.

The version lives only in `<Version>` in `WeddingTime.csproj`. `Plugin.PluginVersion` is generated from it, and the build fails if `thunderstore/manifest.json` has a different `version_number`.

## Wedding Ring

- A single-stack Utility item, crafted at the Forge with 1 Bronze and 1 Amber. Valheim records the crafter's name and ID on the ring.
- A married player activates the Wedding Bond by equipping the ring **crafted by their partner**. The bond is passive while the ring is equipped, like Megingjord; it does not use the Trinket/Adrenaline mechanic.
- The ring cannot be sold to traders.
- Upgrade it at the Forge with the game's quality system (default 3000 Coins and 1 Jade, see Configuration). The upgraded ring uses an engraved icon with a Jade stone and **doubles every Wedding Bond effect**, including damage resistance, skill bonus and maximum stats.

## Proposal and marriage

- Propose to a nearby player with `/do_you_marry_me <player name>`; they answer with `/i_do` or `/i_do_not`.
- The name must match one character exactly, and both players must be within 5 meters when the proposal is sent and answered. A proposal expires after two minutes, and a player can have only one pending proposal at a time.
- After acceptance, both clients ask the server to record the marriage. The server saves it in the world only once **both** partners have confirmed and neither is already married, so no client can marry someone on its own. The server also refuses any change to Wedding Time's world keys that does not come from the mod itself (vanilla Valheim lets any client change world keys, and the `setkey`/`removekey` dev commands are blocked for these keys too). If the partner never confirms, the request expires after about 2.5 minutes and whoever confirmed is told.
- The Wedding Bond turns on only once the marriage is saved in the world. Each client keeps checking its state against the world save, so a lost network message can never leave someone married on only one side.

## Wedding Bond progression

- The couple starts at level 1 and gains one cumulative level for every `DaysTogetherPerLevel` in-game days (default 5) they spend **online together** in that world, up to level 10. Shared time uses the world's in-game day length, so sleeping through the night counts. It is saved to the world periodically and when either partner leaves.
- `/wedding_time` shows your partner, level and progress, to the nearest 0.01 in-game day.
- Each level-up is announced to every connected player in chat and on screen, with fireplace fireworks at both partners' positions.

| Level | Effect (doubled by the upgraded ring) |
| --- | --- |
| 1 | +50% stamina regeneration |
| 2 | +20% movement speed |
| 3 | +50 carry weight |
| 4 | +20% health regeneration |
| 5 | 25% less fire, frost, lightning, poison and spirit damage |
| 6 | +50% Eitr regeneration |
| 7 | 25% less stamina cost |
| 8 | +25 to all skills |
| 9 | +15 maximum health, stamina and Eitr |
| 10 | 50% less stagger, plus a warm light around the player that every client can see and that clears Mistlands mist like a smaller Wisplight (60% of its radius) |

The status icon shows "Wedding Bond" on the first line and the level on the second.

## Divorce

- `/divorce` explains the consequences and arms the command for 60 seconds; `/divorce confirm` sends the request to the server.
- The divorce becomes final after `DivorceDelaySeconds` (default 300; 0 = immediate). Either partner can stop it with `/divorce cancel` before then. The pending divorce is saved in the world and counts real time, so it survives a server restart.
- A final divorce removes the marriage and the couple's shared progress from the world save and clears the bond on both clients. Rings keep their "Crafted by" name but no longer grant any effect. A partner who was offline is told the next time they join that world.
- Server admins can end any marriage at once with `wedding_admin_divorce <player name>` for an online player (anywhere in the world), or `wedding_admin_divorce <player ID>` for an offline one.

## Configuration

Edit `BepInEx/config/ice.weddingtime.cfg` on the server. Synced entries are pushed to every client, so a client's local values do not matter.

| Section / key | Default | Synced | Notes |
| --- | --- | --- | --- |
| `Progression` / `DaysTogetherPerLevel` | 5 | yes | 1–100 shared in-game days per level |
| `Marriage` / `DivorceDelaySeconds` | 300 | yes | 0–3600; 0 makes divorce immediate |
| `Upgrade` / `CoinCost` | 3000 | no | Coins for the ring upgrade; restart required |
| `Upgrade` / `JadeCost` | 1 | no | Jade for the ring upgrade; restart required |

## Languages

Every player-facing message is localized in English and Brazilian Portuguese. Messages sent by the server carry text keys, so each player reads them in their own game language.

## Debug commands

These require `devcommands`:

- `wedding_debug_married on|off` grants the bond with any equipped Wedding Ring, skipping the proposal and partner-crafted-ring checks.
- `wedding_debug_level 1-10|off` previews a level locally. It does not change the world save and lasts only for the current session.

## World save format

All state lives in world global keys (player IDs, smaller ID first):

- `weddingtime_married_<id1>_<id2>`: a marriage
- `weddingtime_shared_<id1>_<id2>_<hundredths of a day>`: shared progress
- `weddingtime_divorce_<id1>_<id2>_<unix time>`: a pending divorce and when it becomes final

Each character file also remembers its partner per world, which is how an offline partner learns about a divorce.

## License

MIT. See [LICENSE](LICENSE).

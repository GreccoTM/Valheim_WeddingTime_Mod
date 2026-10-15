# Wedding Time

A multiplayer wedding mod for Valheim, currently in development.

## Development environment

- Valheim 1.0.16
- BepInExPack Valheim 5.4.2351 (Mono)
- Jötunn 2.30.2
- .NET SDK 10.0.112

## Build

Run `dotnet build` from the project directory. The plugin is written to `artifacts/netstandard2.1/WeddingTime.dll`; copy it to Valheim's `BepInEx/plugins` directory to test.

## Current prototype

The Wedding Ring is a single-stack native Utility item, craftable at the Forge with 1 Bronze and 1 Amber. It uses an original ring icon. Valheim records and displays the item's crafter name and ID; the ID will help validate the pair during the ceremony.

The final wedding buff is intended to be passive while the ring is equipped, like Megingjord. The mod will not use the native Trinket mechanic (Adrenaline-triggered, temporary effects).

## Debug marriage and test effect

For solo testing, enable `devcommands` and run `wedding_debug_married on` or `wedding_debug_married off`. This bypasses the proposal and partner-crafted-ring checks locally. Use `wedding_debug_level 1` through `wedding_debug_level 10` to preview a level, or `wedding_debug_level off` to return to the saved progression. The level preview is local, does not alter the world save, and lasts only for the current session.

## Proposal and acceptance prototype

- Propose to a nearby player with `/do_you_marry_me <player name>`; the recipient answers `/i_do` or `/i_do_not`.
- The proposal targets an exact, unique character name. Network routing uses each character's ZDO owner peer ID, while engagement records use the character player ID. The recipient must be within 5 meters when the proposal is sent and answered.
- A proposal expires after two real-time minutes. A player can have only one pending incoming or outgoing proposal at a time.
- Acceptance records the marriage pair in the host's world save. The state is restored for each character when they rejoin that world. Each partner activates the Wedding Bond by equipping the ring crafted by the other. The couple starts at level 1; the bond gains one cumulative level for every configured number of in-game days they spend online together in that world, up to level 10. The default is 5 days. Shared time uses Valheim's current in-game day length and is saved to the world periodically and when either partner leaves.
- The server's `BepInEx/config/ice.weddingtime.cfg` file contains `[Progression] DaysTogetherPerLevel`. Set it to 1–100 and restart the server. The selected value is stored as a world global key so clients use the server's progression rule even if their local config differs.
- Whenever the couple gains a level, all connected players receive a congratulatory chat message and global center-screen announcement, with the game's fireplace fireworks effects displayed at both partners' positions.
- Use `/wedding_time` to check your Wedding Bond level, progress through the current level, and shared in-game days remaining until the next level. Progress is shown to the nearest 0.01 in-game day, matching the world's saved progress precision.

The cumulative effects are: Lv1 +50% stamina regeneration; Lv2 +20% movement speed; Lv3 +50 carry weight; Lv4 +20% health regeneration; Lv5 25% less fire, frost, lightning, poison, and spirit damage; Lv6 +50% Eitr regeneration; Lv7 25% less stamina cost; Lv8 +25 to all skills; Lv9 +15 maximum health, stamina, and Eitr; Lv10 50% less stagger and a hidden warm light around the bonded player while the Wedding Bond is active. Progress is checked every 10 real-time seconds, not every second. Upgrading the ring at the Forge doubles all Wedding Bond effects.

The Wedding Bond name appears on the first status-icon line, with the current level on the second line.

## Ring upgrade

- Upgrade the original Wedding Ring at the Forge using the game's quality-upgrade system: 3000 Gold Coins and 1 Jade.
- The upgraded ring is quality 2, uses a variant inventory icon with red engravings and a Jade stone, and doubles every Wedding Bond effect, including damage resistance, skill bonus, and maximum stats.

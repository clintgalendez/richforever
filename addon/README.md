# RichForever addon

The in-game half of [RichForever](../README.md). It collects your character's state and draws it as
a small block of coloured cells in the top-left corner of the game window, where the RichForever
companion app reads it. It sends nothing over the network.

## Install

Copy the `RichForever` folder into `World of Warcraft\_forever_\Interface\AddOns\` so that
`Interface\AddOns\RichForever\RichForever.toc` exists, then enable it on the character screen.
Requires WoW Forever (interface 16001) and the companion app.

## Commands

| Command | Effect |
| --- | --- |
| `/rfor` or `/rfor status` | What is being shared |
| `/rfor on`, `/rfor off` | Start or stop sharing (the coloured block disappears when off) |
| `/rfor name on`, `/rfor name off` | Share your character name |
| `/rfor stats on`, `/rfor stats off` | Share health, power and experience percentages |
| `/rfor debug` | Print the exact data being shared |
| `/rfor options` | Open the options panel (also under *Options → AddOns*) |

`/richforever` works as well.

## Notes

- Keep the coloured block uncovered and play in windowed or windowed (fullscreen) mode.
- The block is visible in screenshots and streams and encodes the same information as your presence.
- Values that the client marks as secret (for example in combat) are skipped, never inspected.

Protocol and design: [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md). Tests: `lua5.1 addon/tests/run.lua`.

# Discord setup

Discord shows Rich Presence on behalf of a Discord *application*. The application's name is what
appears after "Playing", and its uploaded images are what RichForever's asset keys refer to.

RichForever needs only the application's public **Application ID**. It never asks for your Discord
password or token, it does not use OAuth, and it does not need a bot.

## Create the application

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) and sign in.
2. Click **New Application**. Name it **World of Warcraft Forever** (this becomes
   "Playing World of Warcraft Forever"). Accept the terms and create it.
3. On **General Information**, copy the **Application ID** (a 17–20 digit number).
4. Paste it into the RichForever window (**Discord app ID**) and click **Save**.

That is enough for text presence. Images need one more step.

## Upload the artwork

1. Download `RichForever-discord-assets-<version>.zip` from the release (or use
   `companion/assets/discord/` from the source) and extract it.
2. In the developer portal open your application → **Rich Presence → Art Assets**.
3. Upload the PNG files. Discord uses the file name (without `.png`) as the asset key, for example
   `class_paladin`, `zone_elwynn_forest`, `inst_stratholme`, `type_raid`, `logo`. Keep those names.
   An application can hold up to 300 assets; RichForever ships 92.
4. Optionally set `logo.png` as the application icon.
5. New assets can take several minutes to become visible to Discord clients.

You do not have to upload everything. Missing art simply shows no image for that slot; to avoid
that, remove the entries you did not upload from a custom catalog (below).

## Asset catalog

[`companion/assets/catalog.json`](../companion/assets/catalog.json) is built into the companion and
maps game data to asset keys:

```json
{
  "default": "logo",
  "types": { "world": "type_world", "dungeon": "type_dungeon", "raid": "type_raid", "battleground": "type_pvp" },
  "classes": { "PALADIN": "class_paladin" },
  "zones": [ { "key": "zone_elwynn_forest", "names": ["Elwynn Forest"], "mapIds": [1429] } ],
  "instances": [ { "key": "inst_stratholme", "names": ["Stratholme"], "instanceIds": [329] } ]
}
```

Lookup order for the large image: instance (by ID, then by name) → zone (by map ID, then by name) →
type badge → `default`. Names are compared without case, spaces or punctuation.

To customise, copy `catalog.json` to `%APPDATA%\RichForever\assets.json` and edit it; the companion
loads it at start (check the log for "Using asset catalog"). Values may be asset keys
(`a-z`, `0-9`, `_`, up to 32 characters) or `https://` image URLs, which Discord fetches through its
media proxy. RichForever itself never downloads images.

## Adding a zone or instance

1. In game, go there and run `/rfor debug`. Note `m=` (map ID), `z=` (zone), `ii=` (instance ID)
   and `in=` (instance name).
2. Add an entry to the catalog with a new key.
3. Upload an image with that key, or for contributions to this repository add the entry to
   `companion/assets/catalog.json` and run `python scripts/generate_assets.py` (see
   [DEVELOPMENT.md](DEVELOPMENT.md)).

## Branding notes

The artwork in this repository is original. Do not upload Blizzard game art or logos to an
application you distribute unless you have the rights to do so, and follow Discord's
[Developer Terms of Service](https://support-dev.discord.com/hc/en-us/articles/8562894815383).

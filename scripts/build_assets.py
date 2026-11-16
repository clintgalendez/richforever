"""Build RichForever's Discord Rich Presence art from official World of Warcraft artwork.

Outputs companion/assets/discord/<key>.png (512x512) for every entry in ART, and
companion/assets/SOURCES.md, which credits where each image comes from.

Sources:
  wago  textures from the WoW Forever client (build below), served by https://wago.tools
  wiki  official Blizzard art and screenshots hosted on https://warcraft.wiki.gg

The artwork belongs to Blizzard Entertainment and is used for this free, non-commercial fan
project. Requires Pillow (python -m pip install pillow). Downloads are cached in the system temp
folder. Run from anywhere:  python scripts/build_assets.py
"""

import io
import json
import os
import sys
import tempfile
import time
import urllib.parse
import urllib.request

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "companion", "assets", "discord")
SOURCES = os.path.join(ROOT, "companion", "assets", "SOURCES.md")
CACHE = os.path.join(tempfile.gettempdir(), "richforever-art")
BUILD = "1.60.1.70938"
SIZE = 512
USER_AGENT = "RichForeverAssetBuilder/1.0 (https://github.com/clintgalendez/richforever)"

# Classic loading screens are stored square but shown at 4:3; the art sits between the logo banner
# and the bottom bar. WoW Forever loading screens are letterboxed 16:9.
CLASSIC = {"unsquash": True, "region": (0.02, 0.24, 0.98, 0.80)}
FULL_BLEED = {"unsquash": True, "region": (0, 0.24, 1, 1)}  # Alterac Valley has no frame
FOREVER = {"region": (0, 0.165, 1, 0.835)}

LOADING = "interface/glues/loadingscreens/"
ICONS = "interface/glues/charactercreate/charactercreateicons.blp"


def icon(left, top):
    """A 128 px class icon (the classicon-* atlas entries)."""
    return {"box": (left, top, left + 128, top + 128)}


# key: (source, game file path or wiki file name, crop options)
# Crop options: region (x0, y0, x1, y1) and focus (x, y) as fractions; box in pixels.
ART = {
    "logo": ("wago", LOADING + "camelot160/logo/logo_camelot.blp", {"logo": True}),
    "type_world": ("wago", LOADING + "camelot160/main/loadscreen_camelot_easternkingdom.blp", {**FOREVER, "focus": (0.55, 0.5)}),
    "type_dungeon": ("wago", LOADING + "loadscreendungeon.blp", CLASSIC),
    "type_raid": ("wago", LOADING + "loadscreenraid.blp", {**CLASSIC, "focus": (0, 0.5)}),
    "type_pvp": ("wago", LOADING + "loadscreenpvpbattleground.blp", FULL_BLEED),

    "class_druid": ("wago", ICONS, icon(1, 261)),
    "class_hunter": ("wago", ICONS, icon(1, 521)),
    "class_mage": ("wago", ICONS, icon(1, 651)),
    "class_paladin": ("wago", ICONS, icon(131, 1)),
    "class_priest": ("wago", ICONS, icon(131, 131)),
    "class_rogue": ("wago", ICONS, icon(131, 261)),
    "class_shaman": ("wago", ICONS, icon(131, 391)),
    "class_warlock": ("wago", ICONS, icon(131, 521)),
    "class_warrior": ("wago", ICONS, icon(131, 651)),

    "zone_durotar": ("wiki", "Durotar3.jpg", {}),
    "zone_mulgore": ("wiki", "Mulgore_tcg.jpg", {}),
    "zone_the_barrens": ("wiki", "Barrens-1024x.jpg", {"region": (0.02, 0.09, 0.98, 0.72), "focus": (0.62, 0.5)}),
    "zone_alterac_mountains": ("wiki", "Alterac_Mountains.jpg", {}),
    "zone_arathi_highlands": ("wiki", "Arathi_Highlands_BfA.jpg", {}),
    "zone_badlands": ("wiki", "Dustbowl.jpg", {}),
    "zone_blasted_lands": ("wiki", "Blasted_Lands_concept_art_Bill_Petras.jpg", {"focus": (0.7, 0.5)}),
    "zone_tirisfal_glades": ("wiki", "Tirisfal_Glades_concept.jpg", {"focus": (0.6, 0.5)}),
    "zone_silverpine_forest": ("wiki", "Silverpine_TAoW.jpg", {}),
    "zone_western_plaguelands": ("wiki", "Western_Plaguelands_TAoWoW.jpg", {}),
    "zone_eastern_plaguelands": ("wiki", "Eastern_Plaguelands_TAoWoW.jpg", {}),
    "zone_hillsbrad_foothills": ("wiki", "Hillsbrad_Foothills.jpg", {}),
    "zone_the_hinterlands": ("wiki", "Hinterlands_landscape.jpg", {}),
    "zone_dun_morogh": ("wiki", "Dun_Morogh.jpg", {}),
    "zone_searing_gorge": ("wiki", "Searing_Gorge.jpg", {}),
    "zone_burning_steppes": ("wiki", "Burning_Steppes.jpg", {}),
    "zone_elwynn_forest": ("wiki", "Elwynn_Forest.jpg", {}),
    "zone_deadwind_pass": ("wago", LOADING + "loadscreenkarazhan.blp", {"unsquash": True, "region": (0.01, 0.27, 0.98, 0.83), "focus": (0, 0.5)}),
    "zone_duskwood": ("wiki", "Duskwood.jpg", {}),
    "zone_loch_modan": ("wiki", "Loch_Modan_Stonewrought_Dam.jpg", {}),
    "zone_redridge_mountains": ("wiki", "Redridge_Mountains.jpg", {}),
    "zone_stranglethorn_vale": ("wiki", "Stranglethorn_concept.jpg", {}),
    "zone_swamp_of_sorrows": ("wiki", "Swamp_of_Sorrows_TAoW.jpg", {}),
    "zone_westfall": ("wiki", "Westfall_HS.jpg", {}),
    "zone_wetlands": ("wiki", "Wetlands_landscape.jpg", {}),
    "zone_teldrassil": ("wiki", "Exploring_Azeroth_-_Teldrassil.jpg", {}),
    "zone_darkshore": ("wiki", "Exploring_Azeroth_-_Darkshore.jpg", {}),
    "zone_ashenvale": ("wiki", "Ashenvale_landscape.jpg", {}),
    "zone_thousand_needles": ("wiki", "1000-needles-1600x.jpg", {"region": (0.02, 0.13, 0.98, 0.76), "focus": (0.72, 0.5)}),
    "zone_stonetalon_mountains": ("wiki", "Exploring_Azeroth_-_Stonetalon_Mountains.jpg", {}),
    "zone_desolace": ("wiki", "Desolace_TAoW.jpg", {}),
    "zone_feralas": ("wiki", "Exploring_Azeroth_-_Feralas.jpg", {}),
    "zone_dustwallow_marsh": ("wiki", "Dustwallow_Highway.jpg", {}),
    "zone_tanaris": ("wiki", "Tanaris_TAoW.jpg", {"focus": (0.6, 0.5)}),
    "zone_azshara": ("wiki", "AzsharaGeneralView.jpg", {}),
    "zone_felwood": ("wiki", "Travelling_through_Felwood.jpg", {}),
    "zone_ungoro_crater": ("wiki", "Un'Goro_Crater_HS.jpg", {"focus": (0.45, 0.5)}),
    "zone_moonglade": ("wiki", "Moonglade.jpg", {}),
    "zone_silithus": ("wiki", "Silithus2-1600x.jpg", {"region": (0.02, 0.1, 0.98, 0.98), "focus": (0.3, 0.5)}),
    "zone_winterspring": ("wiki", "Winterspring.jpg", {}),
    "zone_stormwind_city": ("wiki", "Exploring_Azeroth_-_Stormwind.jpg", {}),
    "zone_orgrimmar": ("wiki", "Exploring_Azeroth_-_Orgrimmar.jpg", {}),
    "zone_ironforge": ("wiki", "Ironforge_HS.jpg", {}),
    "zone_thunder_bluff": ("wiki", "Thunder_Bluff_HS.jpg", {}),
    "zone_darnassus": ("wiki", "Darnassus_TCG.jpg", {}),
    "zone_undercity": ("wiki", "Undercity_TCG.jpg", {}),
    "zone_mount_hyjal": ("wiki", "Mount_Hyjal_Art.jpg", {}),
    "zone_zephras_isle": ("wago", LOADING + "camelot160/main/loadscreen_camelot_zephrasisle.blp", {**FOREVER, "focus": (0.62, 0.5)}),
    "zone_riverglades": ("wiki", "Zones_Riverglades_035.jpg", {}),

    "inst_ragefire_chasm": ("wago", LOADING + "loadscreenragefirechasm.blp", CLASSIC),
    "inst_wailing_caverns": ("wago", LOADING + "loadscreenwailingcaverns.blp", CLASSIC),
    "inst_the_deadmines": ("wago", LOADING + "loadscreendeadmines.blp", CLASSIC),
    "inst_shadowfang_keep": ("wago", LOADING + "loadscreenshadowfangkeep.blp", {**CLASSIC, "focus": (0.4, 0.5)}),
    "inst_blackfathom_deeps": ("wago", LOADING + "loadscreenblackfathomdeeps.blp", CLASSIC),
    "inst_the_stockade": ("wago", LOADING + "loadscreenstormwindstockade.blp", CLASSIC),
    "inst_gnomeregan": ("wago", LOADING + "loadscreengnomeregan.blp", CLASSIC),
    "inst_razorfen_kraul": ("wago", LOADING + "loadscreenrazorfenkraul.blp", CLASSIC),
    "inst_scarlet_monastery": ("wago", LOADING + "loadscreenmonastery.blp", {**CLASSIC, "focus": (0.6, 0.5)}),
    "inst_razorfen_downs": ("wago", LOADING + "loadscreenrazorfendowns.blp", CLASSIC),
    "inst_uldaman": ("wago", LOADING + "loadscreenuldaman.blp", CLASSIC),
    "inst_zulfarrak": ("wago", LOADING + "loadscreenzulfarrak.blp", CLASSIC),
    "inst_maraudon": ("wago", LOADING + "loadscreenmaraudon.blp", CLASSIC),
    "inst_the_temple_of_atalhakkar": ("wago", LOADING + "loadscreensunkentemple.blp", CLASSIC),
    "inst_blackrock_depths": ("wago", LOADING + "loadscreenblackrockdepths.blp", CLASSIC),
    "inst_blackrock_spire": ("wago", LOADING + "loadscreenblackrockspire.blp", {**CLASSIC, "focus": (0.4, 0.5)}),
    "inst_dire_maul": ("wago", LOADING + "loadscreendiremaul.blp", CLASSIC),
    "inst_scholomance": ("wago", LOADING + "loadscreenscholomance.blp", CLASSIC),
    "inst_stratholme": ("wago", LOADING + "loadscreenstrathome.blp", CLASSIC),
    "inst_molten_core": ("wago", LOADING + "loadscreenmoltencore.blp", CLASSIC),
    "inst_onyxias_lair": ("wago", LOADING + "loadscreenraid.blp", {**CLASSIC, "focus": (0.75, 0.5)}),
    "inst_blackwing_lair": ("wago", LOADING + "loadscreenblackwinglair.blp", CLASSIC),
    "inst_zulgurub": ("wago", LOADING + "loadscreenzulgurub.blp", CLASSIC),
    "inst_ruins_of_ahnqiraj": ("wago", LOADING + "loadscreenahnqiraj20man.blp", {**CLASSIC, "focus": (0.4, 0.5)}),
    "inst_temple_of_ahnqiraj": ("wago", LOADING + "loadscreenahnqiraj40man.blp", {**CLASSIC, "focus": (0.35, 0.5)}),
    "inst_naxxramas": ("wago", LOADING + "loadscreennaxxramas.blp", CLASSIC),
    "inst_warsong_gulch": ("wago", LOADING + "loadscreenwarsonggulch.blp", CLASSIC),
    "inst_arathi_basin": ("wago", LOADING + "loadscreenarathibasin.blp", CLASSIC),
    "inst_alterac_valley": ("wago", LOADING + "loadscreenpvpbattleground.blp", {**FULL_BLEED, "focus": (0.2, 0.5)}),
}


def fetch(url, cache_name):
    path = os.path.join(CACHE, cache_name)
    if not os.path.exists(path):
        for attempt in range(5):
            try:
                request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
                data = urllib.request.urlopen(request, timeout=120).read()
                break
            except OSError as error:
                print(f"  retrying {url}: {error}")
                time.sleep(2 + 3 * attempt)
        else:
            raise SystemExit(f"could not download {url}")
        with open(path, "wb") as handle:
            handle.write(data)
        time.sleep(0.3)
    with open(path, "rb") as handle:
        return handle.read()


def game_file(path):
    """A WoW Forever client file; wago.tools maps the path to a file data ID and serves the file."""
    query = urllib.parse.urlencode({"search": path, "version": BUILD})
    matches = json.loads(fetch("https://wago.tools/api/files?" + query, path.replace("/", "_") + ".json")) or {}
    ids = [fdid for fdid, name in matches.items() if name == path]
    if not ids:
        raise SystemExit(f"not in WoW Forever build {BUILD}: {path}")
    return fetch(f"https://wago.tools/api/casc/{ids[0]}?download&version={BUILD}", os.path.basename(path))


def wiki_files(names):
    """File name -> (image URL, description page URL), via the MediaWiki API."""
    found = {}
    names = sorted(set(names))
    for i in range(0, len(names), 40):
        query = urllib.parse.urlencode({"action": "query", "format": "json", "prop": "imageinfo", "iiprop": "url",
                                        "titles": "|".join("File:" + n for n in names[i:i + 40])})
        request = urllib.request.Request("https://warcraft.wiki.gg/api.php?" + query, headers={"User-Agent": USER_AGENT})
        for page in json.load(urllib.request.urlopen(request, timeout=60))["query"]["pages"].values():
            if "imageinfo" not in page:
                raise SystemExit(f"wiki file not found: {page['title']}")
            info = page["imageinfo"][0]
            found[page["title"][5:].replace(" ", "_")] = (info["url"].split("?")[0], info["descriptionurl"])
    return found


def square(image, region=(0, 0, 1, 1), focus=(0.5, 0.5), unsquash=False, box=None, logo=False):
    if box:
        return image.crop(box).resize((SIZE, SIZE), Image.LANCZOS)
    if logo:  # centre the logo on a transparent square
        image = image.crop(image.getbbox())
        side = int(max(image.size) * 1.04)
        canvas = Image.new("RGBA", (side, side))
        canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))
        return canvas.resize((SIZE, SIZE), Image.LANCZOS)
    if unsquash:
        image = image.resize((image.width, image.width * 3 // 4), Image.LANCZOS)
    w, h = image.size
    x0, y0, x1, y1 = region[0] * w, region[1] * h, region[2] * w, region[3] * h
    side = min(x1 - x0, y1 - y0)
    left = min(max(x0 + focus[0] * (x1 - x0) - side / 2, x0), x1 - side)
    top = min(max(y0 + focus[1] * (y1 - y0) - side / 2, y0), y1 - side)
    return image.crop((round(left), round(top), round(left + side), round(top + side))).resize((SIZE, SIZE), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(CACHE, exist_ok=True)
    with open(os.path.join(ROOT, "companion", "assets", "catalog.json"), encoding="utf-8") as handle:
        catalog = json.load(handle)
    keys = {catalog["default"], *catalog["types"].values(), *catalog["classes"].values()}
    keys |= {entry["key"] for entry in catalog["zones"] + catalog["instances"]}
    missing = sorted(keys - ART.keys())
    if missing:
        raise SystemExit("no artwork source for: " + ", ".join(missing))

    wiki = wiki_files(ref for source, ref, _ in ART.values() if source == "wiki")
    rows = []
    for key, (source, ref, options) in ART.items():
        if source == "wago":
            name = os.path.basename(ref)
            data, credit = game_file(ref), f"WoW Forever client file [`{ref}`](https://wago.tools/files?search={name})"
        else:
            url, page = wiki[ref]
            data, credit = fetch(url, ref), f"[{urllib.parse.unquote(ref)}]({page}) on warcraft.wiki.gg"
        image = Image.open(io.BytesIO(data))
        image = square(image.convert("RGBA" if options.get("logo") else "RGB"), **options)
        # 256-colour PNGs are a third of the size and look the same at Discord's display sizes.
        method = Image.Quantize.FASTOCTREE if image.mode == "RGBA" else Image.Quantize.MEDIANCUT
        image.quantize(256, method=method).save(os.path.join(OUT, key + ".png"), optimize=True)
        rows.append(f"| `{key}` | {credit} |")
        print(key)

    with open(SOURCES, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("# Artwork sources\n\n"
                     "Generated by `scripts/build_assets.py`. All artwork is official World of Warcraft art\n"
                     "by Blizzard Entertainment, used for this free, non-commercial fan project. World of\n"
                     f"Warcraft and Blizzard Entertainment are trademarks of Blizzard Entertainment, Inc.\n\n"
                     f"Game files come from the WoW Forever client, build {BUILD}, via [wago.tools](https://wago.tools).\n\n"
                     "| Asset key | Source |\n| --- | --- |\n" + "\n".join(rows) + "\n")
    print(f"wrote {len(rows)} images to {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

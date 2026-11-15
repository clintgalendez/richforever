-- RichForever addon tests. Run from the repository root:
--   lua5.1 addon/tests/run.lua                       (tests only)
--   lua5.1 addon/tests/run.lua --fixtures tests/fixtures   (also write strip images)
-- The fixture images are decoded by the companion's integration tests.

local here = (arg and arg[0] or ""):gsub("[^/\\]+$", "")
package.path = here .. "?.lua;" .. package.path
local mock = dofile(here .. "wowmock.lua")
local addonDir = here .. "../RichForever/"

local fixtureDir
for i = 1, #arg do
	if arg[i] == "--fixtures" then fixtureDir = arg[i + 1] end
end

local passed, failed = 0, 0
local function test(name, fn)
	local ok, err = pcall(fn)
	if ok then
		passed = passed + 1
	else
		failed = failed + 1
		io.write("FAIL ", name, "\n  ", tostring(err), "\n")
	end
end

local function eq(actual, expected, label)
	if actual ~= expected then
		error(("%s: expected %s, got %s"):format(label or "value", tostring(expected), tostring(actual)), 2)
	end
end

-- Load the addon the way the client does: every TOC file with (name, ns).
local function loadAddon(world, savedVariables, charVariables)
	mock.reset(world)
	mock.frames = {}
	RichForeverDB, RichForeverCharDB = savedVariables, charVariables
	local ns = {}
	for line in io.lines(addonDir .. "RichForever.toc") do
		if line:match("%.lua%s*$") then
			local chunk = assert(loadfile(addonDir .. line:gsub("%s+$", "")))
			chunk("RichForever", ns)
		end
	end
	mock.fire("ADDON_LOADED", "RichForever")
	return ns
end

local function login(world, sv, charSv)
	local ns = loadAddon(world, sv, charSv)
	mock.fire("PLAYER_ENTERING_WORLD", true, false)
	mock.advance(1)
	return ns
end

-- Reference decoder, the inverse of Protocol.EncodeCells.
local function decodeCells(P, cells)
	for i = 1, #P.MAGIC do assert(cells[i] == P.MAGIC[i], "magic") end
	assert(cells[8] == P.VERSION, "version")
	local length = cells[9] * 64 + cells[10]
	local sum = cells[11] * 4096 + cells[12] * 64 + cells[13]
	local bytes = {}
	local index = 14
	while #bytes < length do
		local value = cells[index] * 262144 + cells[index + 1] * 4096 + cells[index + 2] * 64 + cells[index + 3]
		bytes[#bytes + 1] = string.char(math.floor(value / 65536))
		bytes[#bytes + 1] = string.char(math.floor(value / 256) % 256)
		bytes[#bytes + 1] = string.char(value % 256)
		index = index + 4
	end
	local payload = table.concat(bytes):sub(1, length)
	assert(P.Checksum(payload) == sum, "checksum")
	return payload
end

local PALADIN = {
	name = "Archer", level = 60, className = "Paladin", classFile = "PALADIN", raceName = "Human", raceFile = "Human",
	faction = "Alliance", sex = 2, zone = "Eastern Plaguelands", subzone = "Darrowshire", mapID = 1423,
}

local function with(base, changes)
	local copy = {}
	for k, v in pairs(base) do copy[k] = v end
	for k, v in pairs(changes) do copy[k] = v end
	return copy
end

-- Protocol -----------------------------------------------------------------

local P = loadAddon({}).Protocol

test("checksum matches Fletcher-16 reference", function()
	eq(P.Checksum("abcde"), 0xC8F0)
	eq(P.Checksum("abcdef"), 0x2057)
	eq(P.Checksum(""), 0)
end)

test("clean trims, strips control characters and keeps UTF-8 intact", function()
	eq(P.Clean("  Elwynn\nForest\t ", 48), "Elwynn Forest")
	eq(P.Clean("", 10), nil)
	eq(P.Clean("   ", 10), nil)
	eq(P.Clean("Ærandir", 2), "Æ")     -- Æ is two bytes; the cut must not split it
	eq(P.Clean("Ærandir", 1), nil)
	eq(P.Clean("日本語", 7), "日本")
	eq(P.Clean(42, 8), "42")
end)

test("serialize keeps field order, skips nils and non-integers", function()
	local payload = P.Serialize({ v = 1, l = 60, n = "Archer", hp = 85.5, m = -3, z = "Duskwood", junk = "x" })
	eq(payload, "v=1\nn=Archer\nl=60\nz=Duskwood")
	-- Large integers must not go through C long formatting (32-bit on Windows).
	eq(P.Serialize({ v = 1, ss = 4102444800, l = 1000 }), "v=1\nss=4102444800")
end)

test("worst case payload fits the strip", function()
	local state = { v = 1 }
	for _, field in ipairs(P.FIELDS) do
		state[field[1]] = field[2] and string.rep("W", 200) or field[3]
	end
	local payload = assert(P.Serialize(state))
	assert(#payload <= P.MAX_PAYLOAD, "payload " .. #payload)
	local cells = assert(P.EncodeCells(payload))
	assert(#cells <= P.COLUMNS * P.MAX_ROWS, "cells " .. #cells)
end)

test("encode rejects oversized payloads", function()
	local cells, err = P.EncodeCells(string.rep("x", P.MAX_PAYLOAD + 1))
	eq(cells, nil)
	eq(err, "payload too large")
end)

test("cells round-trip through the reference decoder", function()
	for _, payload in ipairs({ "v=1", "v=1\nn=Ælfwyn\nz=Zephras Isle", string.rep("abc\n", 100) }) do
		local cells = P.EncodeCells(payload)
		for _, value in ipairs(cells) do assert(value >= 0 and value < 64, "cell range") end
		eq(decodeCells(P, cells), payload)
	end
end)

test("cell colours use four levels per channel", function()
	local r, g, b = P.CellColor(48)
	eq(r, 255); eq(g, 0); eq(b, 0)
	r, g, b = P.CellColor(42)
	eq(r, 170); eq(g, 170); eq(b, 170)
	r, g, b = P.CellColor(0)
	eq(r + g + b, 0)
end)

-- State collection -----------------------------------------------------------

test("collects open world character", function()
	local ns = login(PALADIN)
	eq(ns.lastPayload, "v=1\nn=Archer\nl=60\nc=PALADIN\ncn=Paladin\nr=Human\nrn=Human\nf=Alliance\ng=2\n"
		.. "z=Eastern Plaguelands\nsz=Darrowshire\nm=1423\nit=none\nss=1763200000")
end)

test("collects dungeon details and stats", function()
	local ns = login(with(PALADIN, {
		level = 58, zone = "Stratholme", subzone = "Stratholme", inInstance = true, instanceType = "party",
		instanceName = "Stratholme", instanceID = 329, difficultyName = "Normal", maxPlayers = 5, groupSize = 5,
		health = 85, healthMax = 100, power = 40, powerMax = 100, xp = 42, xpMax = 100, combat = true,
	}), { shareStats = true })
	local p = ns.lastPayload
	assert(p:find("\nz=Stratholme\n", 1, true), p)
	assert(not p:find("sz=", 1, true), "subzone equal to zone is dropped")
	assert(p:find("\nit=party\nin=Stratholme\nii=329\nd=Normal\nmp=5\ngs=5\nhp=85\npp=40\npt=MANA\nxp=42\nfl=c\n", 1, true), p)
end)

test("xp is omitted at max level", function()
	local ns = login(with(PALADIN, { xp = 10, xpMax = 100 }), { shareStats = true })
	assert(not ns.lastPayload:find("xp=", 1, true))
end)

test("unknown instance keeps a neutral type and no name", function()
	local ns = login(with(PALADIN, { inInstance = true, instanceType = "party", instanceName = "" }))
	assert(ns.lastPayload:find("\nit=party\n", 1, true))
	assert(not ns.lastPayload:find("in=", 1, true))
end)

test("unrecognised instance type becomes unknown", function()
	local ns = login(with(PALADIN, { inInstance = true, instanceType = "delve", instanceName = "Somewhere" }))
	assert(ns.lastPayload:find("\nit=unknown\nin=Somewhere\n", 1, true), ns.lastPayload)
end)

test("secret values are never used", function()
	local world = with(PALADIN, { health = 1234, healthMax = 5000 })
	local ns = loadAddon(world, { shareStats = true })
	mock.secret[1234] = true
	mock.fire("PLAYER_ENTERING_WORLD", true, false)
	mock.advance(1)
	assert(not ns.lastPayload:find("hp=", 1, true), ns.lastPayload)
	assert(ns.lastPayload:find("pp=100", 1, true))
end)

test("failing API calls are tolerated", function()
	local original = UnitRace
	UnitRace = function() error("restricted") end
	local ok, err = pcall(function()
		local ns = login(PALADIN)
		assert(ns.lastPayload and not ns.lastPayload:find("\nr=", 1, true))
	end)
	UnitRace = original
	assert(ok, err)
end)

test("missing API functions are tolerated", function()
	local original = UnitIsAFK
	UnitIsAFK = nil
	local ok, err = pcall(function() assert(login(with(PALADIN, { afk = true })).lastPayload) end)
	UnitIsAFK = original
	assert(ok, err)
end)

test("flags cover combat, death, resting and away", function()
	local ns = login(with(PALADIN, { combat = true, dead = true, resting = true, afk = true }))
	assert(ns.lastPayload:find("\nfl=cdra\n", 1, true), ns.lastPayload)
end)

-- Settings and privacy -------------------------------------------------------

test("defaults are applied to empty or corrupt saved variables", function()
	local ns = loadAddon(PALADIN, { enabled = "yes" }, "garbage")
	eq(ns.config.enabled, true)
	eq(ns.config.shareName, true)
	eq(ns.config.shareStats, false)
	eq(type(RichForeverCharDB), "table")
end)

test("hiding the name keeps it out of the payload", function()
	local ns = login(PALADIN, { shareName = false })
	assert(not ns.lastPayload:find("Archer", 1, true))
end)

test("disabling sharing hides the strip", function()
	local ns = login(PALADIN)
	assert(ns.Strip.IsShown())
	SlashCmdList.RICHFOREVER("off")
	assert(not ns.Strip.IsShown())
	eq(ns.lastPayload, nil)
	SlashCmdList.RICHFOREVER("on")
	assert(ns.Strip.IsShown())
end)

test("slash toggles update settings", function()
	local ns = login(PALADIN)
	SlashCmdList.RICHFOREVER("name off")
	eq(ns.config.shareName, false)
	SlashCmdList.RICHFOREVER("stats on")
	eq(ns.config.shareStats, true)
	SlashCmdList.RICHFOREVER("bogus")
	assert(mock.printed[#mock.printed]:find("commands:", 1, true))
	SlashCmdList.RICHFOREVER("debug")
	assert(mock.printed[#mock.printed]:find("ss=", 1, true))
end)

test("options panel registers and opens", function()
	local ns = loadAddon(PALADIN)
	assert(Settings.registered and Settings.registered.name == "RichForever")
	RichForever_OnCompartmentClick()
	eq(Settings.opened, 42)
	ns.SetOption("shareStats", true)
end)

test("options panel failure falls back to slash commands", function()
	mock.templates.UICheckButtonTemplate = nil
	Settings.registered = nil
	local ok, err = pcall(function()
		local ns = loadAddon(PALADIN)
		eq(Settings.registered, nil)
		ns.OpenOptions()
		assert(mock.printed[#mock.printed]:find("unavailable", 1, true))
	end)
	mock.templates.UICheckButtonTemplate = true
	assert(ok, err)
end)

-- Session and scheduling -----------------------------------------------------

test("session starts at initial login", function()
	local ns = login(PALADIN, nil, { sessionStart = 5 })
	eq(RichForeverCharDB.sessionStart, mock.now)
	assert(ns.lastPayload:find("ss=" .. mock.now, 1, true))
end)

test("reload keeps a recent session start", function()
	loadAddon(PALADIN, nil, { sessionStart = mock.now - 3600 })
	mock.fire("PLAYER_ENTERING_WORLD", false, true)
	eq(RichForeverCharDB.sessionStart, mock.now - 3600)
end)

test("reload ignores a stale or future session start", function()
	loadAddon(PALADIN, nil, { sessionStart = mock.now - 13 * 3600 })
	mock.fire("PLAYER_ENTERING_WORLD", false, true)
	eq(RichForeverCharDB.sessionStart, mock.now)
	loadAddon(PALADIN, nil, { sessionStart = mock.now + 60 })
	mock.fire("PLAYER_ENTERING_WORLD", false, true)
	eq(RichForeverCharDB.sessionStart, mock.now)
end)

test("zone changes refresh quickly and coalesce", function()
	local ns = login(PALADIN)
	local renders = 0
	local render = ns.Strip.Render
	ns.Strip.Render = function(cells) renders = renders + 1; render(cells) end
	mock.world.zone = "Western Plaguelands"
	mock.fire("ZONE_CHANGED_NEW_AREA")
	mock.fire("ZONE_CHANGED")
	mock.fire("ZONE_CHANGED_INDOORS")
	mock.advance(0.3)
	eq(renders, 1)
	assert(ns.lastPayload:find("z=Western Plaguelands", 1, true))
	mock.fire("ZONE_CHANGED")
	mock.advance(0.3)
	eq(renders, 1, "unchanged payload is not redrawn")
end)

test("stat events are throttled and ignored when stats are off", function()
	local ns = login(PALADIN, { shareStats = true })
	mock.world.health = 50
	mock.fire("UNIT_HEALTH", "player")
	mock.advance(1)
	assert(ns.lastPayload:find("hp=100", 1, true), "not yet")
	mock.advance(5)
	assert(ns.lastPayload:find("hp=50", 1, true), "after throttle")
	SlashCmdList.RICHFOREVER("stats off")
	mock.fire("UNIT_HEALTH", "player")
	eq(#mock.timers, 0)
end)

test("strip scales one UI unit to one physical pixel", function()
	mock.physical = { 3840, 2160 }
	local ns = login(PALADIN)
	local frame = ns.Strip.Frame()
	eq(frame.scale, 768 / 2160)
	eq(frame.strata, "TOOLTIP")
	eq(frame.parent, nil)
	mock.physical = { 1920, 1080 }
	mock.fire("DISPLAY_SIZE_CHANGED")
	eq(frame.scale, 768 / 1080)
end)

test("rasterized strip reproduces the cell grid", function()
	local ns = login(PALADIN)
	local cells = P.EncodeCells(ns.lastPayload)
	local width, height = P.COLUMNS * P.CELL_PX, P.MAX_ROWS * P.CELL_PX
	local pixels = mock.rasterize(ns.Strip.Frame(), width, height)
	for i = 1, #cells do
		local x = ((i - 1) % P.COLUMNS) * P.CELL_PX + 1
		local y = math.floor((i - 1) / P.COLUMNS) * P.CELL_PX + 1
		local index = (y * width + x) * 3
		local r, g, b = P.CellColor(cells[i])
		eq(pixels[index + 1] * 65536 + pixels[index + 2] * 256 + pixels[index + 3], r * 65536 + g * 256 + b, "cell " .. i)
	end
end)

-- Fixtures for the companion integration tests -------------------------------

local FIXTURES = {
	{ "world", PALADIN, {} },
	{ "dungeon", with(PALADIN, {
		level = 58, zone = "Stratholme", subzone = "", inInstance = true, instanceType = "party",
		instanceName = "Stratholme", instanceID = 329, difficultyName = "Normal", maxPlayers = 5, groupSize = 5,
		health = 85, healthMax = 100, power = 40, powerMax = 100, xp = 42, xpMax = 100, combat = true,
	}), { shareStats = true } },
	{ "raid", {
		name = "Grommash", level = 60, className = "Warrior", classFile = "WARRIOR", raceName = "Orc", raceFile = "Orc",
		faction = "Horde", sex = 2, zone = "Molten Core", inInstance = true, instanceType = "raid", mapID = 1419,
		instanceName = "Molten Core", instanceID = 409, difficultyName = "40 Player", maxPlayers = 40, groupSize = 25,
	}, {} },
	{ "unknown-instance", with(PALADIN, { zone = "", subzone = "", mapID = false, inInstance = true, instanceType = "party", instanceName = "" }), {} },
	{ "battleground", with(PALADIN, { zone = "Warsong Gulch", subzone = "", mapID = 1460, inInstance = true, instanceType = "pvp",
		instanceName = "Warsong Gulch", instanceID = 489, maxPlayers = 10, groupSize = 10 }), {} },
	{ "private", with(PALADIN, { resting = true, zone = "Stormwind City", subzone = "Trade District", mapID = 1453 }), { shareName = false } },
	{ "unicode", { name = "Ælfwyn", level = 23, className = "Hunter", classFile = "HUNTER", raceName = "Skyborne",
		raceFile = "Skyborne", faction = "Alliance", sex = 3, zone = "Zephras Isle", subzone = "Sølvhavn", mapID = 2521 }, {} },
}

if fixtureDir then
	local width, height = P.COLUMNS * P.CELL_PX, P.MAX_ROWS * P.CELL_PX
	for _, fixture in ipairs(FIXTURES) do
		test("fixture " .. fixture[1], function()
			local world = fixture[2]
			if world.mapID == false then world = with(world, {}); world.mapID = nil end
			local ns = login(world, with(fixture[3], {}))
			assert(ns.lastPayload, "no payload")
			mock.writePPM(fixtureDir .. "/" .. fixture[1] .. ".ppm", mock.rasterize(ns.Strip.Frame(), width, height), width, height)
			local out = assert(io.open(fixtureDir .. "/" .. fixture[1] .. ".txt", "wb"))
			out:write(ns.lastPayload, "\n")
			out:close()
		end)
	end
end

io.write(("%d passed, %d failed\n"):format(passed, failed))
if failed > 0 then os.exit(1) end

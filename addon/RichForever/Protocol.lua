-- RichForever strip protocol v1.
--
-- The addon cannot open sockets or write files while the game runs, so the
-- character snapshot is drawn as a small block of coloured cells in the
-- top-left corner of the game window. The companion reads that block from
-- the screen. This file is pure Lua (no WoW API) so it can be unit tested.
--
-- Layout: 64 cells per row, up to 10 rows, each cell 3x3 physical pixels.
-- Each cell carries 6 bits: 2 bits per colour channel at levels 0/85/170/255.
--
--   cells 1-7   magic + calibration: red, green, blue, white, light grey,
--               dark grey, black (values 48, 12, 3, 63, 42, 21, 0)
--   cell  8     protocol version
--   cells 9-10  payload length in bytes (12 bits)
--   cells 11-13 Fletcher-16 checksum of the payload (18 bits, top 2 unused)
--   cells 14..  payload, 3 bytes per 4 cells, zero padded
--
-- The payload is UTF-8 text: one "key=value" pair per line. See
-- docs/ARCHITECTURE.md for the field list.

local _, ns = ...
local Protocol = {}
ns.Protocol = Protocol

local floor, byte, concat = math.floor, string.byte, table.concat

Protocol.VERSION = 1
Protocol.COLUMNS = 64
Protocol.MAX_ROWS = 10
Protocol.CELL_PX = 3
Protocol.MAX_PAYLOAD = 468
Protocol.MAGIC = { 48, 12, 3, 63, 42, 21, 0 }

-- Ordered field list: { key, max text bytes } or { key, nil, max integer }.
local FIELDS = {
	{ "v", nil, 63 },           -- protocol version
	{ "n", 36 },                -- character name
	{ "l", nil, 999 },          -- level
	{ "c", 16 },                -- class token (PALADIN)
	{ "cn", 24 },               -- localized class name
	{ "r", 16 },                -- race token (Human)
	{ "rn", 24 },               -- localized race name
	{ "f", 12 },                -- faction (Alliance, Horde, Neutral)
	{ "g", nil, 9 },            -- sex (1 unknown, 2 male, 3 female)
	{ "z", 48 },                -- zone
	{ "sz", 48 },               -- subzone
	{ "m", nil, 999999 },       -- uiMapID
	{ "it", 10 },               -- instance type (none, party, raid, pvp, arena, scenario)
	{ "in", 48 },               -- instance name
	{ "ii", nil, 999999 },      -- instance ID
	{ "d", 24 },                -- difficulty name
	{ "mp", nil, 99 },          -- instance max players
	{ "gs", nil, 99 },          -- group size
	{ "hp", nil, 100 },         -- health percent
	{ "pp", nil, 100 },         -- power percent
	{ "pt", 12 },               -- power token (MANA, RAGE, ENERGY)
	{ "xp", nil, 100 },         -- experience percent
	{ "fl", 12 },               -- flags: c combat, d dead, r resting, a away
	{ "ss", nil, 9999999999 },  -- session start, epoch seconds
}
Protocol.FIELDS = FIELDS

-- Remove control characters, trim, and cut to maxBytes without splitting a
-- UTF-8 sequence. Returns nil for empty results.
function Protocol.Clean(value, maxBytes)
	if value == nil then return nil end
	value = tostring(value):gsub("%c", " ")
	value = value:gsub("^%s+", ""):gsub("%s+$", "")
	if #value > maxBytes then
		local cut = maxBytes
		local nextByte = byte(value, cut + 1)
		while cut > 0 and nextByte and nextByte >= 128 and nextByte < 192 do
			cut = cut - 1
			nextByte = byte(value, cut + 1)
		end
		value = value:sub(1, cut)
	end
	if value == "" then return nil end
	return value
end

local function integer(value, maximum)
	if type(value) ~= "number" or value ~= value or value < 0 or value > maximum then return nil end
	if value ~= floor(value) then return nil end
	return string.format("%.0f", value)
end

-- Turn a state table into the payload string. Unknown keys are ignored.
function Protocol.Serialize(state)
	local lines = {}
	for i = 1, #FIELDS do
		local key, limit, maximum = FIELDS[i][1], FIELDS[i][2], FIELDS[i][3]
		local value = state[key]
		if value ~= nil then
			if limit then value = Protocol.Clean(value, limit) else value = integer(value, maximum) end
			if value then lines[#lines + 1] = key .. "=" .. value end
		end
	end
	local payload = concat(lines, "\n")
	if #payload > Protocol.MAX_PAYLOAD then return nil, "payload too large" end
	return payload
end

function Protocol.Checksum(text)
	local a, b = 0, 0
	for i = 1, #text do
		a = (a + byte(text, i)) % 255
		b = (b + a) % 255
	end
	return b * 256 + a
end

-- Encode a payload into a list of 6-bit cell values.
function Protocol.EncodeCells(payload)
	local length = #payload
	if length > Protocol.MAX_PAYLOAD then return nil, "payload too large" end
	local cells = {}
	for i = 1, #Protocol.MAGIC do cells[i] = Protocol.MAGIC[i] end
	local sum = Protocol.Checksum(payload)
	cells[#cells + 1] = Protocol.VERSION
	cells[#cells + 1] = floor(length / 64)
	cells[#cells + 1] = length % 64
	cells[#cells + 1] = floor(sum / 4096)
	cells[#cells + 1] = floor(sum / 64) % 64
	cells[#cells + 1] = sum % 64
	for i = 1, length, 3 do
		local b1, b2, b3 = byte(payload, i, i + 2)
		local value = b1 * 65536 + (b2 or 0) * 256 + (b3 or 0)
		cells[#cells + 1] = floor(value / 262144)
		cells[#cells + 1] = floor(value / 4096) % 64
		cells[#cells + 1] = floor(value / 64) % 64
		cells[#cells + 1] = value % 64
	end
	return cells
end

-- 6-bit cell value to 0-255 RGB.
function Protocol.CellColor(value)
	return floor(value / 16) * 85, floor(value / 4) % 4 * 85, value % 4 * 85
end

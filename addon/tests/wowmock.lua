-- Minimal WoW API mock for RichForever tests. Only the API surface the addon
-- uses is implemented; calling anything else fails loudly.

local mock = {}

mock.world = {}
mock.clock = 1000
mock.now = 1763200000
mock.timers = {}
mock.printed = {}
mock.secret = {}      -- values reported as secret by issecretvalue
mock.physical = { 1920, 1080 }

function mock.reset(world)
	-- Copy so tests that change the world never leak into shared tables.
	mock.world = {}
	for key, value in pairs(world or {}) do mock.world[key] = value end
	mock.timers = {}
	mock.printed = {}
	mock.secret = {}
end

local function w(key, default)
	local value = mock.world[key]
	if value == nil then return default end
	return value
end

-- Frames ---------------------------------------------------------------------

local Region = {}
Region.__index = Region

function Region:SetPoint(point, relative, relativePoint, x, y)
	if type(relative) == "number" then relative, relativePoint, x, y = nil, point, relative, relativePoint end
	self.point = { point, relative, relativePoint or point, x or 0, y or 0 }
end
function Region:SetSize(width, height) self.width, self.height = width, height end
function Region:SetWidth(width) self.width = width end
function Region:Show() self.shown = true end
function Region:Hide() self.shown = false end
function Region:IsShown() return self.shown ~= false end
function Region:SetText(text) self.text = text end
function Region:SetJustifyH() end
function Region:SetColorTexture(r, g, b, a) self.color = { r, g, b, a } end
function Region:SetSnapToPixelGrid(value) self.snap = value end
function Region:SetTexelSnappingBias(value) self.bias = value end

local Frame = setmetatable({}, { __index = Region })
Frame.__index = Frame

function Frame:RegisterEvent(event) self.events[event] = true end
function Frame:RegisterUnitEvent(event) self.events[event] = true end
function Frame:UnregisterEvent(event) self.events[event] = nil end
function Frame:SetScript(name, fn) self.scripts[name] = fn end
function Frame:GetScript(name) return self.scripts[name] end
function Frame:SetScale(scale) self.scale = scale end
function Frame:GetScale() return self.scale end
function Frame:SetFrameStrata(strata) self.strata = strata end
function Frame:SetFrameLevel(level) self.level = level end
function Frame:EnableMouse(value) self.mouse = value end
function Frame:SetChecked(value) self.checked = value and true or false end
function Frame:GetChecked() return self.checked end
function Frame:CreateTexture(_, layer)
	local texture = setmetatable({ layer = layer, parent = self }, Region)
	self.textures[#self.textures + 1] = texture
	return texture
end
function Frame:CreateFontString()
	return setmetatable({}, Region)
end

mock.frames = {}

function CreateFrame(kind, name, parent, template)
	if template and not mock.templates[template] then error("unknown template " .. template) end
	local frame = setmetatable({ kind = kind, name = name, parent = parent, events = {}, scripts = {}, textures = {}, scale = 1 }, Frame)
	if template == "UICheckButtonTemplate" then frame.Text = setmetatable({}, Region) end
	mock.frames[#mock.frames + 1] = frame
	if name then _G[name] = frame end
	return frame
end
mock.templates = { UICheckButtonTemplate = true }

UIParent = setmetatable({ events = {}, scripts = {}, textures = {}, scale = 1 }, Frame)

function mock.fire(event, ...)
	for _, frame in ipairs(mock.frames) do
		local handler = frame.scripts.OnEvent
		if handler and (frame.events[event] or event == "ADDON_LOADED") then handler(frame, event, ...) end
	end
end

-- Timers ---------------------------------------------------------------------

function GetTime() return mock.clock end
function time() return mock.now end

C_Timer = {}
function C_Timer.After(delay, fn)
	mock.timers[#mock.timers + 1] = { at = mock.clock + delay, fn = fn }
end

function mock.advance(seconds)
	mock.clock = mock.clock + seconds
	local ran = true
	while ran do
		ran = false
		for i, timer in ipairs(mock.timers) do
			if timer.at <= mock.clock then
				table.remove(mock.timers, i)
				timer.fn()
				ran = true
				break
			end
		end
	end
end

-- Game API -------------------------------------------------------------------

function issecretvalue(value) return mock.secret[value] == true end
function GetPhysicalScreenSize() return mock.physical[1], mock.physical[2] end
function UnitName() return w("name"), nil end
function GetRealmName() return w("realm", "") end
function GetGuildInfo() return w("guild"), w("guild") and "Member", w("guild") and 0 end
function UnitLevel() return w("level", 1) end
function UnitClass() return w("className"), w("classFile"), w("classID") end
function UnitRace() return w("raceName"), w("raceFile"), w("raceID") end
function UnitFactionGroup() return w("faction"), w("faction") end
function UnitSex() return w("sex", 1) end
function GetRealZoneText() return w("zone", "") end
function GetSubZoneText() return w("subzone", "") end
C_Map = {}
function C_Map.GetBestMapForUnit() return w("mapID") end
function IsInInstance() return w("inInstance", false), w("instanceType", "none") end
function GetInstanceInfo()
	return w("instanceName", ""), w("instanceType", "none"), w("difficultyID", 0), w("difficultyName", ""),
		w("maxPlayers", 0), false, false, w("instanceID", 0), 0, nil
end
function GetNumGroupMembers() return w("groupSize", 0) end
function UnitHealth() return w("health", 100) end
function UnitHealthMax() return w("healthMax", 100) end
function UnitPowerType() return w("powerType", 0), w("powerToken", "MANA") end
function UnitPower() return w("power", 100) end
function UnitPowerMax() return w("powerMax", 100) end
function GetMaxPlayerLevel() return w("maxLevel", 60) end
function UnitXP() return w("xp", 0) end
function UnitXPMax() return w("xpMax", 0) end
function UnitAffectingCombat() return w("combat", false) end
function UnitIsDeadOrGhost() return w("dead", false) end
function IsResting() return w("resting", false) end
function UnitIsAFK() return w("afk", false) end

function print(...)
	local parts = {}
	for i = 1, select("#", ...) do parts[#parts + 1] = tostring((select(i, ...))) end
	mock.printed[#mock.printed + 1] = table.concat(parts, " ")
end

SlashCmdList = {}

Settings = {
	registered = nil,
	RegisterCanvasLayoutCategory = function(panel, name)
		return { panel = panel, name = name, GetID = function() return 42 end }
	end,
	RegisterAddOnCategory = function(category) Settings.registered = category end,
	OpenToCategory = function(id) Settings.opened = id end,
}

-- Rasterizer -----------------------------------------------------------------

-- Paint the visible textures of the strip frame into an RGB image the size of
-- the companion's capture region, using the same geometry the client would:
-- frame anchored TOPLEFT of the screen, one UI unit = physicalHeight/768/scale.
function mock.rasterize(frame, width, height)
	local pixels = {}
	for i = 1, width * height * 3 do pixels[i] = 0 end
	if not frame or not frame:IsShown() then return pixels end
	local unit = frame.scale * mock.physical[2] / 768
	local originX, originY = frame.point[4] * unit, -frame.point[5] * unit
	for _, texture in ipairs(frame.textures) do
		if texture:IsShown() and texture.color then
			local x0 = math.floor(originX + texture.point[4] * unit + 0.5)
			local y0 = math.floor(originY - texture.point[5] * unit + 0.5)
			local x1 = math.floor(originX + (texture.point[4] + texture.width) * unit + 0.5)
			local y1 = math.floor(originY + (-texture.point[5] + texture.height) * unit + 0.5)
			local r = math.floor(texture.color[1] * 255 + 0.5)
			local g = math.floor(texture.color[2] * 255 + 0.5)
			local b = math.floor(texture.color[3] * 255 + 0.5)
			for y = math.max(y0, 0), math.min(y1, height) - 1 do
				for x = math.max(x0, 0), math.min(x1, width) - 1 do
					local index = (y * width + x) * 3
					pixels[index + 1], pixels[index + 2], pixels[index + 3] = r, g, b
				end
			end
		end
	end
	return pixels
end

function mock.writePPM(path, pixels, width, height)
	local out = assert(io.open(path, "wb"))
	out:write(("P6\n%d %d\n255\n"):format(width, height))
	local chunk = {}
	for i = 1, #pixels do
		chunk[#chunk + 1] = string.char(pixels[i])
		if #chunk == 4096 then out:write(table.concat(chunk)); chunk = {} end
	end
	out:write(table.concat(chunk))
	out:close()
end

return mock

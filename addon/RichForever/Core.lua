-- Event handling, settings, session tracking and slash commands.

local addonName, ns = ...
local P, State, Strip = ns.Protocol, ns.State, ns.Strip

local DEFAULTS = { enabled = true, shareName = true, shareStats = false }
local SESSION_RESUME_LIMIT = 12 * 3600 -- seconds a /reload may reuse a stored session start
local STATS_DELAY = 5                   -- health/power events refresh at most this often
local EVENT_DELAY = 0.25

ns.config = nil
ns.lastPayload = nil
ns.lastError = nil
local sessionStart
local pendingAt

local function say(message)
	print("|cff66ccffRichForever|r: " .. message)
end
ns.Say = say

function ns.Refresh()
	pendingAt = nil
	if not ns.config then return end
	if not ns.config.enabled then
		Strip.Hide()
		ns.lastPayload = nil
		return
	end
	local payload, err = P.Serialize(State.Collect(ns.config, sessionStart))
	if not payload then
		if ns.lastError ~= err then say("could not build presence: " .. tostring(err)) end
		ns.lastError = err
		return
	end
	ns.lastError = nil
	if payload == ns.lastPayload and Strip.IsShown() then return end
	Strip.Render(P.EncodeCells(payload))
	ns.lastPayload = payload
end

-- Coalesce bursts of events into one refresh; an earlier request wins.
function ns.Schedule(delay)
	local due = GetTime() + delay
	if pendingAt and pendingAt <= due then return end
	pendingAt = due
	C_Timer.After(delay, function()
		if pendingAt and GetTime() + 0.01 >= pendingAt then ns.Refresh() end
	end)
end

local function startSession(isInitialLogin)
	local now = time()
	local stored = RichForeverCharDB.sessionStart
	if not isInitialLogin and type(stored) == "number" and stored <= now and now - stored < SESSION_RESUME_LIMIT then
		sessionStart = stored
	elseif not sessionStart or isInitialLogin then
		sessionStart = now
	end
	RichForeverCharDB.sessionStart = sessionStart
end

local function loadSettings()
	RichForeverDB = type(RichForeverDB) == "table" and RichForeverDB or {}
	RichForeverCharDB = type(RichForeverCharDB) == "table" and RichForeverCharDB or {}
	for key, value in pairs(DEFAULTS) do
		if type(RichForeverDB[key]) ~= type(value) then RichForeverDB[key] = value end
	end
	ns.config = RichForeverDB
end

function ns.SetOption(key, value)
	if DEFAULTS[key] == nil then return end
	ns.config[key] = value and true or false
	ns.Refresh()
	if ns.OnOptionChanged then ns.OnOptionChanged() end
end

local QUICK_EVENTS = {
	"PLAYER_ENTERING_WORLD", "ZONE_CHANGED", "ZONE_CHANGED_INDOORS", "ZONE_CHANGED_NEW_AREA",
	"PLAYER_LEVEL_UP", "PLAYER_REGEN_DISABLED", "PLAYER_REGEN_ENABLED", "PLAYER_DEAD", "PLAYER_ALIVE",
	"PLAYER_UNGHOST", "PLAYER_UPDATE_RESTING", "PLAYER_FLAGS_CHANGED", "GROUP_ROSTER_UPDATE",
	"PLAYER_DIFFICULTY_CHANGED", "UPDATE_INSTANCE_INFO",
}
local STAT_EVENTS = { "UNIT_HEALTH", "UNIT_MAXHEALTH", "UNIT_POWER_UPDATE", "UNIT_MAXPOWER", "UNIT_DISPLAYPOWER", "PLAYER_XP_UPDATE" }
local quick = {}
for _, event in ipairs(QUICK_EVENTS) do quick[event] = true end

local events = CreateFrame("Frame")
events:RegisterEvent("ADDON_LOADED")
events:SetScript("OnEvent", function(self, event, arg1, arg2)
	if event == "ADDON_LOADED" then
		if arg1 ~= addonName then return end
		self:UnregisterEvent("ADDON_LOADED")
		loadSettings()
		for _, name in ipairs(QUICK_EVENTS) do pcall(self.RegisterEvent, self, name) end
		for _, name in ipairs(STAT_EVENTS) do
			if name == "PLAYER_XP_UPDATE" then pcall(self.RegisterEvent, self, name)
			else pcall(self.RegisterUnitEvent, self, name, "player") end
		end
		pcall(self.RegisterEvent, self, "DISPLAY_SIZE_CHANGED")
		pcall(self.RegisterEvent, self, "UI_SCALE_CHANGED")
		if ns.CreateOptions then ns.CreateOptions() end
		return
	end
	if event == "PLAYER_ENTERING_WORLD" then
		startSession(arg1 == true and arg2 ~= true)
	elseif event == "DISPLAY_SIZE_CHANGED" or event == "UI_SCALE_CHANGED" then
		Strip.UpdateScale()
		return
	end
	if quick[event] then
		ns.Schedule(EVENT_DELAY)
	elseif ns.config and ns.config.shareStats then
		ns.Schedule(STATS_DELAY)
	end
end)

local function onOff(value) return value and "on" or "off" end

local function status()
	local c = ns.config
	say(("sharing %s, character name %s, health/power/XP %s."):format(onOff(c.enabled), onOff(c.shareName), onOff(c.shareStats)))
	if not c.enabled then
		say("the companion receives nothing while sharing is off.")
	elseif ns.lastPayload then
		say(("broadcasting %d bytes to the RichForever companion (it only reads them while the game window is visible)."):format(#ns.lastPayload))
	else
		say("waiting for character data.")
	end
	if ns.lastError then say("last error: " .. ns.lastError) end
end

local function debugDump()
	ns.Refresh()
	if not ns.lastPayload then say("nothing is being shared.") return end
	say("current payload:")
	for line in ns.lastPayload:gmatch("[^\n]+") do print("  " .. line) end
end

local TOGGLES = { name = "shareName", stats = "shareStats" }

SLASH_RICHFOREVER1 = "/richforever"
SLASH_RICHFOREVER2 = "/rfor"
SlashCmdList.RICHFOREVER = function(input)
	local command, argument = (input or ""):lower():match("^%s*(%S*)%s*(%S*)")
	if command == "" or command == "status" then
		status()
	elseif command == "on" or command == "off" then
		ns.SetOption("enabled", command == "on")
		say("sharing " .. command .. ".")
	elseif TOGGLES[command] and (argument == "on" or argument == "off") then
		ns.SetOption(TOGGLES[command], argument == "on")
		say(command .. " " .. argument .. ".")
	elseif command == "debug" then
		debugDump()
	elseif command == "options" or command == "config" then
		if ns.OpenOptions then ns.OpenOptions() end
	else
		say("commands: /rfor status | on | off | name on|off | stats on|off | debug | options")
	end
end

function RichForever_OnCompartmentClick()
	if ns.OpenOptions then ns.OpenOptions() else status() end
end

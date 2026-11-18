-- Collects the character snapshot from the WoW API.
--
-- WoW Forever runs on the modern client, where some values can be "secret"
-- during combat (issecretvalue). Secret values may be passed to widgets but
-- any arithmetic or string work on them raises an error, so every value is
-- checked before use and each API call is protected. Missing APIs and secret
-- values simply leave the field out.

local _, ns = ...
local State = {}
ns.State = State

local floor, pcall, type = math.floor, pcall, type

local function plain(value)
	if value == nil then return nil end
	local check = issecretvalue
	if check and check(value) then return nil end
	return value
end

-- Call a global API function by value; returns nothing if it is missing or fails.
local function call(fn, ...)
	if type(fn) ~= "function" then return end
	local results = { pcall(fn, ...) }
	if not results[1] then return end
	return unpack(results, 2, 9)
end
State.Call = call

local function number(value)
	value = plain(value)
	if type(value) ~= "number" or value ~= value then return nil end
	return value
end

local function text(value)
	value = plain(value)
	if type(value) ~= "string" or value == "" then return nil end
	return value
end

local function percent(current, maximum)
	current, maximum = number(current), number(maximum)
	if not current or not maximum or maximum <= 0 then return nil end
	local value = floor(current / maximum * 100 + 0.5)
	if value < 0 then return 0 end
	if value > 100 then return 100 end
	return value
end

local INSTANCE_TYPES = { none = true, party = true, raid = true, pvp = true, arena = true, scenario = true }

function State.Collect(config, sessionStart)
	local s = { v = ns.Protocol.VERSION, ss = sessionStart }

	if config.shareName then s.n = text(call(UnitName, "player")) end
	if config.shareName and config.shareGuild then
		s.rl = text(call(GetRealmName))
		s.gu = text(call(GetGuildInfo, "player"))
	end
	local level = number(call(UnitLevel, "player"))
	if level and level > 0 then s.l = level end

	local className, classFile = call(UnitClass, "player")
	s.cn, s.c = text(className), text(classFile)
	local raceName, raceFile = call(UnitRace, "player")
	s.rn, s.r = text(raceName), text(raceFile)
	s.f = text(call(UnitFactionGroup, "player"))
	s.g = number(call(UnitSex, "player"))

	s.z = text(call(GetRealZoneText))
	s.sz = text(call(GetSubZoneText))
	if s.sz == s.z then s.sz = nil end
	if C_Map then s.m = number(call(C_Map.GetBestMapForUnit, "player")) end

	local inInstance, instanceType = call(IsInInstance)
	inInstance, instanceType = plain(inInstance), text(instanceType)
	if inInstance then
		local name, infoType, _, difficultyName, maxPlayers, _, _, instanceID = call(GetInstanceInfo)
		instanceType = instanceType or text(infoType)
		s["in"] = text(name)
		s.d = text(difficultyName)
		s.mp = number(maxPlayers)
		s.ii = number(instanceID)
	end
	s.it = (instanceType and INSTANCE_TYPES[instanceType]) and instanceType or (inInstance and "unknown" or "none")

	local groupSize = number(call(GetNumGroupMembers))
	if groupSize and groupSize > 1 then s.gs = groupSize end

	if config.shareStats then
		s.hp = percent(call(UnitHealth, "player"), call(UnitHealthMax, "player"))
		local _, powerToken = call(UnitPowerType, "player")
		s.pt = text(powerToken)
		s.pp = percent(call(UnitPower, "player"), call(UnitPowerMax, "player"))
		local maxLevel = number(call(GetMaxPlayerLevel))
		if not (maxLevel and level and level >= maxLevel) then
			s.xp = percent(call(UnitXP, "player"), call(UnitXPMax, "player"))
		end
	end

	local flags = ""
	if plain(call(UnitAffectingCombat, "player")) then flags = flags .. "c" end
	if plain(call(UnitIsDeadOrGhost, "player")) then flags = flags .. "d" end
	if plain(call(IsResting)) then flags = flags .. "r" end
	if plain(call(UnitIsAFK, "player")) then flags = flags .. "a" end
	if flags ~= "" then s.fl = flags end

	return s
end

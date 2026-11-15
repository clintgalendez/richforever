-- luacheck configuration: WoW addons run on Lua 5.1 with Blizzard's API as globals.
std = "lua51"
max_line_length = 160
codes = true

files["addon/RichForever"] = {
	globals = {
		"RichForeverDB", "RichForeverCharDB", "SLASH_RICHFOREVER1", "SLASH_RICHFOREVER2",
		"RichForever_OnCompartmentClick", "SlashCmdList",
	},
	read_globals = {
		"C_Map", "C_Timer", "CreateFrame", "GetInstanceInfo", "GetMaxPlayerLevel", "GetNumGroupMembers",
		"GetPhysicalScreenSize", "GetRealZoneText", "GetSubZoneText", "GetTime", "IsInInstance", "IsResting",
		"Settings", "UIParent", "UnitAffectingCombat", "UnitClass", "UnitFactionGroup", "UnitHealth",
		"UnitHealthMax", "UnitIsAFK", "UnitIsDeadOrGhost", "UnitLevel", "UnitName", "UnitPower", "UnitPowerMax",
		"UnitPowerType", "UnitRace", "UnitSex", "UnitXP", "UnitXPMax", "issecretvalue", "time",
	},
}

-- The test mock defines the WoW API as globals on purpose.
files["addon/tests"] = {
	allow_defined_top = true,
	globals = { "RichForeverDB", "RichForeverCharDB", "SlashCmdList", "Settings", "UnitRace", "UnitIsAFK", "print", "time" },
	read_globals = { "RichForever_OnCompartmentClick" },
}

-- Settings panel (Game Menu > Options > AddOns > RichForever). Optional: if the
-- client's Settings API or check button template is unavailable the slash
-- commands still work.

local _, ns = ...

local category
local checkboxes = {}

local OPTIONS = {
	{ key = "enabled", label = "Share presence with the RichForever companion", tip = "Turn off to stop sharing anything. Same as /rfor off." },
	{ key = "shareName", label = "Share character name", tip = "When off, your name never leaves the game." },
	{ key = "shareGuild", label = "Share realm and guild name", tip = "Off by default and needs the character name to be shared. "
		.. "The companion shows them only if you use {realm} or {guild} in your own presence text." },
	{ key = "shareStats", label = "Share health, power and experience", tip = "Lets the companion show stats if you enable them there too." },
}

local function refreshChecks()
	for key, box in pairs(checkboxes) do box:SetChecked(ns.config[key]) end
end
ns.OnOptionChanged = refreshChecks

local function build()
	local panel = CreateFrame("Frame")
	panel.name = "RichForever"

	local title = panel:CreateFontString(nil, "ARTWORK", "GameFontNormalLarge")
	title:SetPoint("TOPLEFT", 16, -16)
	title:SetText("RichForever")

	local about = panel:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall")
	about:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -8)
	about:SetWidth(560)
	about:SetJustifyH("LEFT")
	about:SetText("Shares your character with Discord through the RichForever companion app. "
		.. "A small coloured block in the top-left corner carries the data; keep it uncovered. "
		.. "Nothing is sent over the network by this addon.")

	local anchor = about
	for _, option in ipairs(OPTIONS) do
		local box = CreateFrame("CheckButton", nil, panel, "UICheckButtonTemplate")
		box:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -12)
		local label = box.Text or box.text
		if not label then
			label = box:CreateFontString(nil, "ARTWORK", "GameFontHighlight")
			label:SetPoint("LEFT", box, "RIGHT", 4, 0)
		end
		label:SetText(option.label)
		box.tooltipText = option.tip
		box:SetScript("OnClick", function(self) ns.SetOption(option.key, self:GetChecked()) end)
		checkboxes[option.key] = box
		anchor = box
	end

	local help = panel:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall")
	help:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -16)
	help:SetText("Slash commands: /rfor status, /rfor on, /rfor off, /rfor debug")

	panel:SetScript("OnShow", refreshChecks)
	return panel
end

function ns.CreateOptions()
	if not (Settings and Settings.RegisterCanvasLayoutCategory and Settings.RegisterAddOnCategory) then return end
	local ok, panel = pcall(build)
	if not ok then return end
	local registered, result = pcall(Settings.RegisterCanvasLayoutCategory, panel, "RichForever")
	if not registered then return end
	category = result
	pcall(Settings.RegisterAddOnCategory, category)
end

function ns.OpenOptions()
	if category and Settings and Settings.OpenToCategory then
		local ok = pcall(Settings.OpenToCategory, category:GetID())
		if ok then return end
	end
	ns.Say("options panel unavailable; use /rfor commands.")
end

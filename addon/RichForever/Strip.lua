-- Draws protocol cells as a block of solid colour textures at the top-left
-- corner of the screen. The frame has no parent so hiding the UI (Alt+Z) does
-- not hide it, and its scale is set so one UI unit equals one physical pixel.

local _, ns = ...
local Strip = {}
ns.Strip = Strip

local P = ns.Protocol
local floor, ceil = math.floor, math.ceil
local COLUMNS, CELL = P.COLUMNS, P.CELL_PX

local frame
local textures = {}
local shownCount = 0

local function cellTexture(index)
	local texture = textures[index]
	if texture then return texture end
	texture = frame:CreateTexture(nil, "OVERLAY")
	texture:SetSize(CELL, CELL)
	local column, row = (index - 1) % COLUMNS, floor((index - 1) / COLUMNS)
	texture:SetPoint("TOPLEFT", frame, "TOPLEFT", column * CELL, -row * CELL)
	if texture.SetSnapToPixelGrid then
		texture:SetSnapToPixelGrid(false)
		texture:SetTexelSnappingBias(0)
	end
	textures[index] = texture
	return texture
end

function Strip.UpdateScale()
	if not frame then return end
	local _, height = GetPhysicalScreenSize()
	if height and height > 0 then frame:SetScale(768 / height) end
end

local function ensureFrame()
	if frame then return end
	frame = CreateFrame("Frame", "RichForeverStrip", nil)
	frame:SetFrameStrata("TOOLTIP")
	frame:SetFrameLevel(9000)
	frame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 0, 0)
	frame:EnableMouse(false)
	Strip.UpdateScale()
end

function Strip.Render(cells)
	ensureFrame()
	local rows = ceil(#cells / COLUMNS)
	local count = rows * COLUMNS
	frame:SetSize(COLUMNS * CELL, rows * CELL)
	for i = 1, count do
		local r, g, b = P.CellColor(cells[i] or 0)
		local texture = cellTexture(i)
		texture:SetColorTexture(r / 255, g / 255, b / 255, 1)
		texture:Show()
	end
	for i = count + 1, shownCount do textures[i]:Hide() end
	shownCount = count
	frame:Show()
end

function Strip.Hide()
	if frame then frame:Hide() end
end

function Strip.IsShown()
	return frame ~= nil and frame:IsShown()
end

-- Exposed for tests.
function Strip.Frame() return frame end

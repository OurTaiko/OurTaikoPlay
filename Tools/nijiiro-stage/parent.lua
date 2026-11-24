-- Run the inherited 1280px skin's scripts at their own resolution and send the texture draws
-- to parent.py at each sampled frame; parent.py enlarges them to the Nijiiro skin's 1920px.
local parent, name, manifest = arg[1], arg[2], arg[3]
package.path = parent .. "/Scripts/?.lua;" .. package.path
local folders = dofile(manifest)
local loaded, textures = {}, {}
local now, bpm = 0, 120

tex = {
    get_current_ms = function() return now end,
    get_screen_scale = function() return 1 end,
    get_screen_width = function() return 1280 end,
    get_screen_height = function() return 720 end,
}

function tex.load_folder(root, folder)
    local entries = folders[root .. "/" .. folder] or {}
    local subset = folder:match("[^/]+$")
    loaded[subset] = entries
    for key, texture in pairs(entries) do
        textures[root .. "/" .. subset .. "/" .. key] = texture
    end
end

function tex.get_texture(key)
    return textures[key]
end

function tex.get_texture_info(subset, key)
    return (loaded[subset] or {})[key]
end

function tex.get_texture_keys(subset)
    local keys = {}
    for key in pairs(loaded[subset] or {}) do table.insert(keys, key) end
    table.sort(keys)
    return keys
end

function tex.draw_texture(texture, params)
    if not texture or texture == 0 then return end
    local p = params or {}
    local origin = p.origin or {0, 0}
    print(table.concat({
        "D", texture.id, p.frame or 0, p.x or 0, p.y or 0, p.x2 or 0, p.y2 or 0,
        origin[1], origin[2], p.rotation or 0, p.fade or 1, p.mirror or "", p.blend or "", p.scale or 1
    }, "\t"))
end

-- Lua counterparts of the parent's engine animation calls.
local timing = require("background/bg_objects/timing")
anim = {move = timing.move_lua}
local function sampled(value_at)
    return function(duration, data, options)
        local animation = {attribute = 0}
        function animation:start()
            self.t = now
            self.is_finished = false
        end
        function animation:update(ms)
            local elapsed = ms - (self.t or 0)
            self.is_finished = elapsed >= duration
            if options and options.loop then elapsed = elapsed % duration end
            self.attribute = value_at(elapsed, data, duration)
        end
        return animation
    end
end

anim.texture_change = sampled(function(ms, frames)
    for _, frame in ipairs(frames) do
        if ms <= frame[2] then return frame[3] end
    end
    return frames[#frames][3]
end)
anim.fade = sampled(function(ms, options, duration)
    return options.initial_opacity + (options.final_opacity - options.initial_opacity) * math.min(1, ms / duration)
end)

math.randomseed(0)
local stage = require("background/bg_collabs/" .. name).new(1, bpm)
local function clip(folder, file, label, count, loop, draw, start)
    now = 0
    print("C", folder, file, label, count, loop or -1)
    if start then start() end
    for frame = 0, count - 1 do
        now = frame * 1000 / 60
        print("F")
        draw(frame)
    end
end

clip("scene", name, "normal", 1, 0, function() stage.bg_normal:draw() end)
clip("clear", name, "clear", 480, 240, function()
    stage.bg_fever:update(now, bpm)
    stage.bg_fever:draw()
end, function() stage.bg_fever:start() end)

for _, state in ipairs({"normal", "clear"}) do
    clip("header", name, state, 640, 0, function()
        stage.donbg:update(now, bpm)
        stage.donbg:draw_textures(1, 0, state == "clear" and 1 or 0)
    end)
end
if stage.footer then
    clip("footer", name, "normal", 1, 0, function() stage.footer:draw() end)
end
if stage.fever then
    local function draw_crowd()
        stage.fever:update(now, bpm)
        stage.fever:draw()
    end
    clip("crowd", name, "full", 90, 30, draw_crowd, function() stage.fever:start() end)
    clip("crowd", name, "release", 31, nil, draw_crowd, function() stage.fever:stop() end)
end

local renda = stage.renda
local kinds = renda.kinds
-- Each hit is baked from the middle of where the skin would spawn it; the game spreads hits
-- around that by the set's catalog spread.
renda.x, renda.x_range = renda.x + renda.x_range / 2, 0
renda.y, renda.y_range = renda.y + renda.y_range / 2, 0
for index, kind in ipairs(kinds) do
    renda.notes = {}
    renda.kinds = {kind}
    clip("renda", name, "hit_" .. index, math.ceil(kind[2] * .06), nil, function()
        renda:update(now)
        renda:draw()
    end, function() renda:add_renda() end)
end
renda.kinds = kinds

for _, judgment in ipairs({"good", "bad"}) do
    local count = name == "oshiri" and judgment == "good" and 2 or 1
    for variant = 1, count do
        stage.chibi.chibis = {}
        clip("chibi", name, judgment .. "_" .. variant, 150, nil, function()
            stage.chibi:update(now, bpm)
            stage.chibi:draw()
        end, function()
            local random = math.random
            if name == "oshiri" then math.random = function() return variant end end
            if judgment == "bad" then stage:handle_bad(1) else stage:handle_good(1) end
            math.random = random
        end)
    end
end

local troupe = stage.dancer
local centers = ({
    animal = {172, 426, 640, 874, 1108},
    funassyi = {183, 417, 640, 863, 1097},
    oshiri = {208, 422, 635, 848, 1062},
})[name]
for index, dancer in ipairs(troupe.dancers) do
    local slot = troupe.spawn_positions[index] + 1
    local width = troupe:loop_info(dancer.index).width
    local x
    if centers then
        x = math.floor(centers[slot] - width / 2)
    else
        local gap = (1280 - #troupe.dancers * width) / (#troupe.dancers + 1)
        x = math.floor(gap + (slot - 1) * (width + gap))
    end

    local function frames(beats) return math.floor(.5 + 40 * beats) end
    local enter = frames(dancer.spec.enter.beats)
    local dance = math.max(1, frames(dancer.parts.dance[1].length or dancer.spec.dance.beats))
    local leave = frames(dancer.spec.leave and dancer.spec.leave.beats or 0)
    print("T", index - 1, enter, enter + dance)
    clip("dancers", name .. "_" .. (index - 1), "rig", enter + dance + math.max(1, leave), nil, function(frame)
        local state = "enter"
        if frame >= enter + dance then
            state, frame = "leave", frame - enter - dance
        elseif frame >= enter then
            state, frame = "dance", frame - enter
        end
        if dancer.spec[state] then
            dancer:play(state, 0)
            dancer.now = frame * 12.5
            dancer.group = nil
            dancer:draw(x)
        end
    end)
end

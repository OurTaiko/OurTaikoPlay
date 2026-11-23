-- Read a trusted skin data module and serialize its tables, preserving zero-based keys.
local function encode(v)
    if type(v) == "string" then
        return '"' .. v:gsub('[%z\1-\31\\"]', function(c) return string.format("\\u%04x", c:byte()) end) .. '"'
    elseif type(v) ~= "table" then return tostring(v) end
    local n, list = 0, true
    for k in pairs(v) do
        n = n + 1
        if type(k) ~= 'number' or k < 1 or k > #v then list = false end
    end
    list = list and n == #v
    local out = {}
    if list then
        for i=1,n do out[i] = encode(v[i]) end
        return '['..table.concat(out,',')..']'
    end
    for k,value in pairs(v) do out[#out+1] = encode(tostring(k))..':'..encode(value) end
    return '{'..table.concat(out,',')..'}'
end
print(encode(dofile(arg[1])))

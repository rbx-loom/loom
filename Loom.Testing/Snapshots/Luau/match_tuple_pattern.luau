const t: { string | number } = {"abc", 420}
local _match
if typeof(t) == "table" then
  const a = t[1]
  const b = t[2]
  _match = a
else
  _match = "none"
end
const result = _match
print(result)
return nil
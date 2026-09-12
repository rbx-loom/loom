const maybe_pair = {1}
const first = maybe_pair[1]
local second = maybe_pair[2]
if second == nil then
  second = 0
end
print(first)
print(second)
return nil
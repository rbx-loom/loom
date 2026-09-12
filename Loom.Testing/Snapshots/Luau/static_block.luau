type Vector2 = {
  read x: number,
  read y: number,
}
local Vector2 = {}
Vector2.zero = { x = 0, y = 0 }
function Vector2.create(x, y)
  return { x = x, y = y }
end
const v = Vector2.create(1, 2)
const z = Vector2.zero
return nil
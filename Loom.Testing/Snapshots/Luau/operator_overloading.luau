type Location = {
  read position: number,
} & Add<Location>
type Add<T> = {
  add: (Add<T>, T) -> T,
}
local Add_Location_for_Location = {}
Add_Location_for_Location.__index = Add_Location_for_Location
Add_Location_for_Location = Add_Location_for_Location :: Location
function Add_Location_for_Location.add(self: Location, other)
  return setmetatable({ position = self.position + other.position }, Add_Location_for_Location) :: Location
end
Add_Location_for_Location.__add = Add_Location_for_Location.add
const start = setmetatable({ position = 69 }, Add_Location_for_Location) :: Location
const finish = setmetatable({ position = 420 }, Add_Location_for_Location) :: Location
const result1 = start + finish
const result2 = start:add(finish)
return nil
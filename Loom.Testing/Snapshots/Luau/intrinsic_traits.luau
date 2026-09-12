const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type User = {
  read name: string,
  read age: number,
} & Display & Eq
local Display_for_User = {}
Display_for_User.__index = Display_for_User
Display_for_User = Display_for_User :: User
function Display_for_User.to_string(self: User): string
  return "User(" .. self.name .. ")"
end
local Eq_for_User = {}
Eq_for_User.__index = Eq_for_User
Eq_for_User = Eq_for_User :: User
const function Eq_equals_default(self: unknown, other: unknown): boolean
  return Loom.deep_equal(self, other)
end
Eq_for_User.equals = Eq_equals_default
local User_meta = Loom.merge_meta(Display_for_User, Eq_for_User)
const a = setmetatable({ name = "Poppy", age = 31 }, User_meta) :: User
const b = setmetatable({ name = "Rose", age = 20 }, User_meta) :: User
print(a:to_string())
print(a:equals(b))
return nil
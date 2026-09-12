const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type User = {
  read name: string,
  read age: number,
} & Display & Eq & Hash
local Display_for_User = {}
Display_for_User.__index = Display_for_User
Display_for_User = Display_for_User :: User
const function Display_to_string_default(self: unknown): string
  return Loom.deep_display(self)
end
Display_for_User.to_string = Display_to_string_default
local Eq_for_User = {}
Eq_for_User.__index = Eq_for_User
Eq_for_User = Eq_for_User :: User
const function Eq_equals_default(self: unknown, other: unknown): boolean
  return Loom.deep_equal(self, other)
end
Eq_for_User.equals = Eq_equals_default
local Hash_for_User = {}
Hash_for_User.__index = Hash_for_User
Hash_for_User = Hash_for_User :: User
const function Hash_hash_default(self: unknown): number
  return Loom.deep_hash(self)
end
Hash_for_User.hash = Hash_hash_default
local User_meta = Loom.merge_meta(Display_for_User, Eq_for_User, Hash_for_User)
const a = setmetatable({ name = "Poppy", age = 31 }, User_meta) :: User
const b = setmetatable({ name = "Poppy", age = 31 }, User_meta) :: User
print(a:to_string())
print(a:equals(b))
print(a:hash())
return nil
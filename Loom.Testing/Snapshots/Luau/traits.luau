type ToString = {
  to_string: (ToString) -> string,
}
type Person = {
  read name: string,
  read age: number,
} & ToString
local ToString_for_Person = {}
ToString_for_Person.__index = ToString_for_Person
ToString_for_Person = ToString_for_Person :: Person
function ToString_for_Person.to_string(self: Person)
  return "Person" .. " { name: ''" .. self.name .. "', age: " .. tostring(self.age) .. " }"
end
const person = setmetatable({ name = "Runic", age = 21 }, ToString_for_Person) :: Person
print(person:to_string())
return nil
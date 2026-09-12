type Greeting = {
  greet: (Greeting) -> string,
}
type A = {} & Greeting
type B = {} & Greeting
local Greeting_for_A = {}
Greeting_for_A.__index = Greeting_for_A
Greeting_for_A = Greeting_for_A :: A
const function Greeting_greet_default(self: unknown): string
  return "Hello!"
end
Greeting_for_A.greet = Greeting_greet_default
local Greeting_for_B = {}
Greeting_for_B.__index = Greeting_for_B
Greeting_for_B = Greeting_for_B :: B
function Greeting_for_B.greet(self: B): string
  return "Howdy!"
end
const a = setmetatable({}, Greeting_for_A) :: A
const b = setmetatable({}, Greeting_for_B) :: B
print(a:greet())
print(b:greet())
return nil
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const my_event: Loom.Event<number> = Loom.Event.new()
const function on_event(p: number): ()
  print("param: " .. tostring(p))
end
type Execute = {
  execute: (Execute, number) -> (),
}
type Foo = {} & Execute
local Execute_for_Foo = {}
Execute_for_Foo.__index = Execute_for_Foo
Execute_for_Foo = Execute_for_Foo :: Foo
function Execute_for_Foo.execute(self: Foo, p)
  return print(p)
end
const foo = setmetatable({}, Execute_for_Foo) :: Foo
my_event:Connect(function(...): ()
  foo:execute(...)
end)
my_event:Connect(function(argument0)
  return table.insert({}, argument0)
end)
my_event:Fire(69420)
const conn = my_event:Connect(on_event)
conn:Disconnect()
return nil
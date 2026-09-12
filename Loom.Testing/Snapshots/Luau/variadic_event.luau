const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const abc: Loom.Event<...unknown> = Loom.Event.new()
const labelled: Loom.Event<string, ...number> = Loom.Event.new()
abc:Connect(function(a, b, c)
  print(a, b, c)
end)
labelled:Connect(function(label, first)
  print(label, first)
end)
const ns = {1, 2}
abc:Fire(1, "two", true)
labelled:Fire("hi", table.unpack(ns))
return nil
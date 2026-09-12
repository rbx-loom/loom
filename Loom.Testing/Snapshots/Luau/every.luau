const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
Loom.every(0.5, nil, function(): ()
  print("im")
  print("so")
  print("epic")
end)
Loom.every(10, nil, print, "big dawg")
const running = true
Loom.every(1, function()
  return running
end, print, "tick")
return nil
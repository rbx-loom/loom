const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const abc: Loom.Event = Loom.Event.new()
const function handler(): ()
  print("handled")
end
const my_conn = abc:Connect(handler)
my_conn:Disconnect()
const handler_conn = abc:Connect(handler)
handler_conn:Disconnect()
return nil
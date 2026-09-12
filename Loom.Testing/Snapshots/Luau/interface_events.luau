const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type EventObject = {
  read consumer: Loom.Event<string>,
}
const function on_consumer(p: string): ()
  print("param: " .. p)
end
const eo = (nil :: never) :: EventObject
const on_consumer_conn = eo.consumer:Connect(on_consumer)
eo.consumer:Fire("abc")
on_consumer_conn:Disconnect()
return nil
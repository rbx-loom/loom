const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type EventObject = {
  read ConsumerEvent: Loom.Event<string>,
}
const function on_consumer(p: string): ()
  print("param: " .. p)
end
const eo = (nil :: never) :: EventObject
const on_consumer_conn = eo.ConsumerEvent:Connect(on_consumer)
eo.ConsumerEvent:Fire("abc")
on_consumer_conn:Disconnect()
const my_event: Loom.Event<string> = Loom.Event.new()
return nil
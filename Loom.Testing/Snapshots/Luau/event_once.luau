const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const my_event: Loom.Event<string> = Loom.Event.new()
const function handler(data: string): ()
  return print(data)
end
const handler_conn = my_event:Once(handler)
my_event:Fire("hello!")
return nil
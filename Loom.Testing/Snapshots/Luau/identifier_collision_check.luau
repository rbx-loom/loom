const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const _range = 69
type Foo = {
  read bar: Loom.Range,
}
const foo = { bar = { minimum = 1, maximum = 69 } }
const _range_1 = foo.bar
const x = math.clamp(69, _range_1.minimum, _range_1.maximum)
const _range_2 = foo.bar
const y = math.clamp(4, _range_2.minimum, _range_2.maximum)
const _range_3 = foo.bar
const z = math.clamp(2, _range_3.minimum, _range_3.maximum)
return nil
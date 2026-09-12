const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_readbits = buffer.readbits
const buffer_len = buffer.len
const function Counter_serialize_binary(value: Counter): Loom.Serialized
  const b = buffer_create(4)
  buffer_writebits(b, 0, 32, math.round(value.total))
  return { buffer = b }
end
const function Counter_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Counter, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { total = buffer_readbits(b, 0, 32) } }
end
type Counter = {
  read total: number,
}
const function _trigger_usage(): ()
  const payload = Counter_serialize_binary((nil :: never) :: Counter)
  const restored = Counter_deserialize_binary(payload)
end
return nil
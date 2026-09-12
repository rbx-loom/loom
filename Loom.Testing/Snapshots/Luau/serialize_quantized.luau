const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_readbits = buffer.readbits
const buffer_len = buffer.len
const function Dials_serialize_binary(value: Dials): Loom.Serialized
  const b = buffer_create(3)
  buffer_writebits(b, 0, 7, math.round(value.opacity / 0.01))
  buffer_writebits(b, 7, 9, math.round((value.offset - -100) / 0.5))
  buffer_writebits(b, 16, 7, math.round(value.health))
  return { buffer = b }
end
const function Dials_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Dials, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 3 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { opacity = buffer_readbits(b, 0, 7) * 0.01, offset = -100 + buffer_readbits(b, 7, 9) * 0.5, health = buffer_readbits(b, 16, 7) } }
end
type Dials = {
  read opacity: number,
  read offset: number,
  read health: number,
}
const function _trigger_usage(): ()
  const payload = Dials_serialize_binary((nil :: never) :: Dials)
  const restored = Dials_deserialize_binary(payload)
end
return nil
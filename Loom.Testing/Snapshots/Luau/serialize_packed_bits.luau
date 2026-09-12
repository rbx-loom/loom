const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_readbits = buffer.readbits
const buffer_len = buffer.len
const function Flags_serialize_binary(value: Flags): Loom.Serialized
  const b = buffer_create(2)
  buffer_writebits(b, 0, 1, if value.alive then 1 else 0)
  buffer_writebits(b, 1, 1, if value.ready then 1 else 0)
  buffer_writebits(b, 2, 7, math.round(value.health))
  return { buffer = b }
end
const function Flags_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Flags, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { kind = "spawn", alive = buffer_readbits(b, 0, 1) == 1, ready = buffer_readbits(b, 1, 1) == 1, health = buffer_readbits(b, 2, 7) } }
end
type Flags = {
  read kind: "spawn",
  read alive: boolean,
  read ready: boolean,
  read health: number,
}
const function _trigger_usage(): ()
  const payload = Flags_serialize_binary((nil :: never) :: Flags)
  const restored = Flags_deserialize_binary(payload)
end
return nil
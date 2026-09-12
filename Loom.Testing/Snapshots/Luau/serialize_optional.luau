const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_writebits = buffer.writebits
const buffer_writei16 = buffer.writei16
const buffer_readu8 = buffer.readu8
const buffer_readbits = buffer.readbits
const buffer_readi16 = buffer.readi16
const buffer_len = buffer.len
const function Entity_serialize_binary(value: Entity): Loom.Serialized
  const b = buffer_create(2 + (if value.target ~= nil then 2 else 0))
  buffer_writeu8(b, 1, value.id)
  buffer_writebits(b, 0, 1, if value.target ~= nil then 1 else 0)
  local offset = 2
  if value.target ~= nil then
    buffer_writei16(b, offset, value.target)
    offset += 2
  end
  return { buffer = b }
end
const function Entity_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Entity, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const target_present = buffer_readbits(b, 0, 1) == 1
  local target = nil
  local offset = 2
  if target_present then
    if b_len < offset + 2 then
      return { ok = false, error = { kind = "truncated", field = "target" } }
    end
    const target_2 = buffer_readi16(b, offset)
    target = target_2
    offset += 2
  end
  return { ok = true, value = { id = buffer_readu8(b, 1), target = target } }
end
type Entity = {
  read id: number,
  read target: number?,
}
const function _trigger_usage(): ()
  const payload = Entity_serialize_binary((nil :: never) :: Entity)
  const restored = Entity_deserialize_binary(payload)
end
return nil
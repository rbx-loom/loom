const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_readu8 = buffer.readu8
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_len = buffer.len
const function Player_serialize_binary(value: Player): Loom.Serialized
  const b = buffer_create(1 + 4 + #value.name)
  buffer_writeu8(b, 0, value.id)
  const name_value = value.name
  const name_length = #name_value
  buffer_writeu32(b, 1, name_length)
  local offset = 5
  buffer_writestring(b, offset, name_value)
  offset += name_length
  return { buffer = b }
end
const function Player_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Player, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 5 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const name_length = buffer_readu32(b, 1)
  local offset = 5
  if b_len < offset + name_length then
    return { ok = false, error = { kind = "invalid_length", field = "name" } }
  end
  const name = buffer_readstring(b, offset, name_length)
  offset += name_length
  return { ok = true, value = { id = buffer_readu8(b, 0), name = name } }
end
type Player = {
  read id: number,
  read name: string,
}
const function _trigger_usage(): ()
  const payload = Player_serialize_binary((nil :: never) :: Player)
  const restored = Player_deserialize_binary(payload)
end
return nil
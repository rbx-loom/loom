const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writei16 = buffer.writei16
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readi16 = buffer.readi16
const buffer_len = buffer.len
const function Roster_serialize_binary(value: Roster): Loom.Serialized
  local size = 4 + #value.positions * 6
  size += 4
  const names_count = #value.names
  for i = 1, names_count do
    const names_element = value.names[i]
    size += 4 + #names_element
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, names_count)
  local offset = 4
  for i = 1, names_count do
    const names_element = value.names[i]
    const names_element_length = #names_element
    buffer_writeu32(b, offset, names_element_length)
    buffer_writestring(b, offset + 4, names_element)
    offset += 4 + names_element_length
  end
  const positions_count = #value.positions
  buffer_writeu32(b, offset, positions_count)
  offset += 4
  for i = 1, positions_count do
    const positions_element = value.positions[i]
    buffer_writei16(b, offset, positions_element.X)
    buffer_writei16(b, offset + 2, positions_element.Y)
    buffer_writei16(b, offset + 4, positions_element.Z)
    offset += 6
  end
  return { buffer = b }
end
const function Roster_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Roster, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 8 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const names_count = buffer_readu32(b, 0)
  local offset = 4
  const names = {}
  for i = 1, names_count do
    const names_element_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + names_element_length then
      return { ok = false, error = { kind = "invalid_length", field = "names[]" } }
    end
    const names_element = buffer_readstring(b, offset + 4, names_element_length)
    offset += 4 + names_element_length
    names[i] = names_element
  end
  const positions_count = buffer_readu32(b, offset)
  if b_len < offset + 4 + positions_count * 6 then
    return { ok = false, error = { kind = "invalid_length", field = "positions" } }
  end
  const positions = {}
  offset += 4
  for i = 1, positions_count do
    const positions_element_x = buffer_readi16(b, offset)
    const positions_element_y = buffer_readi16(b, offset + 2)
    const positions_element_z = buffer_readi16(b, offset + 4)
    offset += 6
    positions[i] = Vector3.new(positions_element_x, positions_element_y, positions_element_z)
  end
  return { ok = true, value = { names = names, positions = positions } }
end
type Roster = {
  read names: { string },
  read positions: { Vector3 },
}
const function _trigger_usage(): ()
  const payload = Roster_serialize_binary((nil :: never) :: Roster)
  const restored = Roster_deserialize_binary(payload)
end
return nil
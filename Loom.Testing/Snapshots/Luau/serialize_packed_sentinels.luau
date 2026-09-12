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
  const position_value = value.position
  local position_sentinel = 0
  if position_value == Vector3.zero then
    position_sentinel = 1
  elseif position_value == Vector3.one then
    position_sentinel = 2
  elseif position_value == Vector3.xAxis then
    position_sentinel = 3
  elseif position_value == Vector3.yAxis then
    position_sentinel = 4
  elseif position_value == Vector3.zAxis then
    position_sentinel = 5
  end
  const b = buffer_create(2 + (if position_sentinel == 0 then 6 else 0))
  buffer_writeu8(b, 1, value.id)
  buffer_writebits(b, 0, 3, position_sentinel)
  local offset = 2
  if position_sentinel == 0 then
    buffer_writei16(b, offset, position_value.X)
    buffer_writei16(b, offset + 2, position_value.Y)
    buffer_writei16(b, offset + 4, position_value.Z)
    offset += 6
  end
  return { buffer = b }
end
const function Entity_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Entity, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const position_sentinel = buffer_readbits(b, 0, 3)
  local position = nil
  local offset = 2
  if position_sentinel == 0 then
    if b_len < offset + 6 then
      return { ok = false, error = { kind = "truncated", field = "position" } }
    end
    const position_x = buffer_readi16(b, offset)
    const position_y = buffer_readi16(b, offset + 2)
    const position_z = buffer_readi16(b, offset + 4)
    position = Vector3.new(position_x, position_y, position_z)
    offset += 6
  elseif position_sentinel == 1 then
    position = Vector3.zero
  elseif position_sentinel == 2 then
    position = Vector3.one
  elseif position_sentinel == 3 then
    position = Vector3.xAxis
  elseif position_sentinel == 4 then
    position = Vector3.yAxis
  elseif position_sentinel == 5 then
    position = Vector3.zAxis
  else
    return { ok = false, error = { kind = "invalid_tag", field = "position" } }
  end
  return { ok = true, value = { id = buffer_readu8(b, 1), position = position } }
end
type Entity = {
  read id: number,
  read position: Vector3,
}
const function _trigger_usage(): ()
  const payload = Entity_serialize_binary((nil :: never) :: Entity)
  const restored = Entity_deserialize_binary(payload)
end
return nil
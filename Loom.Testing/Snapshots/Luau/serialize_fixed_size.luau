const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_writei16 = buffer.writei16
const buffer_readu8 = buffer.readu8
const buffer_readi16 = buffer.readi16
const buffer_len = buffer.len
const function MyData_serialize_binary(value: MyData): Loom.Serialized
  const b = buffer_create(7)
  buffer_writeu8(b, 0, value.id)
  const position_value = value.position
  buffer_writei16(b, 1, position_value.X)
  buffer_writei16(b, 3, position_value.Y)
  buffer_writei16(b, 5, position_value.Z)
  return { buffer = b }
end
const function MyData_deserialize_binary(serialized: Loom.Serialized): Loom.Result<MyData, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 7 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { id = buffer_readu8(b, 0), position = Vector3.new(buffer_readi16(b, 1), buffer_readi16(b, 3), buffer_readi16(b, 5)) } }
end
type MyData = {
  read id: number,
  read position: Vector3,
}
const function _trigger_usage(): ()
  const payload = MyData_serialize_binary((nil :: never) :: MyData)
  const restored = MyData_deserialize_binary(payload)
end
return nil
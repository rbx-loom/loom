const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_readu8 = buffer.readu8
const buffer_len = buffer.len
const function MyData_serialize_binary(value: MyData): Loom.Serialized
  const b = buffer_create(1)
  buffer_writeu8(b, 0, value.id)
  return { buffer = b }
end
const function MyData_deserialize_binary(serialized: Loom.Serialized): Loom.Result<MyData, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { id = buffer_readu8(b, 0) } }
end
type MyData = {
  read id: number,
}
const data = { id = 3 }
const payload = MyData_serialize_binary(data)
const restored = MyData_deserialize_binary(payload)
if restored.ok then
  print(restored.value.id)
end
return nil
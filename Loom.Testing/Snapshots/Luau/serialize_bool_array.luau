const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writebits = buffer.writebits
const buffer_readu32 = buffer.readu32
const buffer_readbits = buffer.readbits
const buffer_len = buffer.len
const function Flags_serialize_binary(value: Flags): Loom.Serialized
  const b = buffer_create(4 + (#value.values + 7) // 8)
  const values_count = #value.values
  buffer_writeu32(b, 0, values_count)
  local offset = 4
  const values_bits = offset * 8
  offset += (values_count + 7) // 8
  for i = 1, values_count do
    const values_element = value.values[i]
    buffer_writebits(b, values_bits + (i - 1), 1, if values_element then 1 else 0)
  end
  return { buffer = b }
end
const function Flags_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Flags, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const values_count = buffer_readu32(b, 0)
  local offset = 4
  const values_bits = offset * 8
  offset += (values_count + 7) // 8
  const values = {}
  for i = 1, values_count do
    values[i] = buffer_readbits(b, values_bits + (i - 1), 1) == 1
  end
  return { ok = true, value = { values = values } }
end
type Flags = {
  read values: { boolean },
}
const function _trigger_usage(): ()
  const payload = Flags_serialize_binary((nil :: never) :: Flags)
  const restored = Flags_deserialize_binary(payload)
end
return nil
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writef32 = buffer.writef32
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readf32 = buffer.readf32
const buffer_len = buffer.len
const function Scores_serialize_binary(value: Scores): Loom.Serialized
  local size = 0
  size += 4
  local entries_count = 0
  for entries_key, entries_value in value.entries do
    entries_count += 1
    size += 4 + #entries_key
    size += 4
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, entries_count)
  local offset = 4
  for entries_key, entries_value in value.entries do
    const entries_key_length = #entries_key
    buffer_writeu32(b, offset, entries_key_length)
    buffer_writestring(b, offset + 4, entries_key)
    offset += 4 + entries_key_length
    buffer_writef32(b, offset, entries_value)
    offset += 4
  end
  return { buffer = b }
end
const function Scores_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Scores, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const entries_count = buffer_readu32(b, 0)
  local offset = 4
  const entries = {}
  for i = 1, entries_count do
    const entries_key_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + entries_key_length then
      return { ok = false, error = { kind = "invalid_length", field = "entries[k]" } }
    end
    const entries_key = buffer_readstring(b, offset + 4, entries_key_length)
    offset += 4 + entries_key_length
    const entries_value = buffer_readf32(b, offset)
    offset += 4
    entries[entries_key] = entries_value
  end
  return { ok = true, value = { entries = entries } }
end
type Scores = {
  read entries: Record<string, number>,
}
const function _trigger_usage(): ()
  const payload = Scores_serialize_binary((nil :: never) :: Scores)
  const restored = Scores_deserialize_binary(payload)
end
return nil
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writebits = buffer.writebits
const buffer_writef32 = buffer.writef32
const buffer_writeu8 = buffer.writeu8
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readbits = buffer.readbits
const buffer_readf32 = buffer.readf32
const buffer_readu8 = buffer.readu8
const buffer_len = buffer.len
const function Registry_serialize_binary(value: Registry): Loom.Serialized
  local size = 0
  size += 4
  local flags_count = 0
  for flags_key, flags_value in value.flags do
    flags_count += 1
    size += 4 + #flags_key
  end
  size += (flags_count + 7) // 8
  size += 4
  local entries_count = 0
  for entries_key, entries_value in value.entries do
    entries_count += 1
    size += 4
    size += 1
    size += 4 + #entries_value.label
  end
  size += 4
  local lists_count = 0
  for lists_key, lists_value in value.lists do
    lists_count += 1
    size += 4 + #lists_key
    size += 4 + #lists_value * 4
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, flags_count)
  local offset = 4
  const flags_bits = offset * 8
  offset += (flags_count + 7) // 8
  local flags_index = 1
  for flags_key, flags_value in value.flags do
    const flags_key_length = #flags_key
    buffer_writeu32(b, offset, flags_key_length)
    buffer_writestring(b, offset + 4, flags_key)
    offset += 4 + flags_key_length
    buffer_writebits(b, flags_bits + (flags_index - 1), 1, if flags_value then 1 else 0)
    flags_index += 1
  end
  buffer_writeu32(b, offset, entries_count)
  offset += 4
  for entries_key, entries_value in value.entries do
    buffer_writef32(b, offset, entries_key)
    buffer_writeu8(b, offset + 4, entries_value.level)
    const label_value = entries_value.label
    const label_length = #label_value
    buffer_writeu32(b, offset + 5, label_length)
    buffer_writestring(b, offset + 9, label_value)
    offset += 9 + label_length
  end
  buffer_writeu32(b, offset, lists_count)
  offset += 4
  for lists_key, lists_value in value.lists do
    const lists_key_length = #lists_key
    buffer_writeu32(b, offset, lists_key_length)
    buffer_writestring(b, offset + 4, lists_key)
    offset += 4 + lists_key_length
    const lists_value_count = #lists_value
    buffer_writeu32(b, offset, lists_value_count)
    offset += 4
    for i = 1, lists_value_count do
      const lists_value_element = lists_value[i]
      buffer_writef32(b, offset, lists_value_element)
      offset += 4
    end
  end
  return { buffer = b }
end
const function Registry_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Registry, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 12 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const flags_count = buffer_readu32(b, 0)
  local offset = 4
  const flags_bits = offset * 8
  offset += (flags_count + 7) // 8
  const flags = {}
  for i = 1, flags_count do
    const flags_key_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + flags_key_length then
      return { ok = false, error = { kind = "invalid_length", field = "flags[k]" } }
    end
    const flags_key = buffer_readstring(b, offset + 4, flags_key_length)
    offset += 4 + flags_key_length
    flags[flags_key] = buffer_readbits(b, flags_bits + (i - 1), 1) == 1
  end
  const entries_count = buffer_readu32(b, offset)
  const entries = {}
  offset += 4
  for i = 1, entries_count do
    const entries_key = buffer_readf32(b, offset)
    const level = buffer_readu8(b, offset + 4)
    const label_length = buffer_readu32(b, offset + 5)
    if b_len < offset + 9 + label_length then
      return { ok = false, error = { kind = "invalid_length", field = "entries[v].label" } }
    end
    const label = buffer_readstring(b, offset + 9, label_length)
    offset += 9 + label_length
    entries[entries_key] = { level = level, label = label }
  end
  const lists_count = buffer_readu32(b, offset)
  const lists = {}
  offset += 4
  for i = 1, lists_count do
    const lists_key_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + lists_key_length then
      return { ok = false, error = { kind = "invalid_length", field = "lists[k]" } }
    end
    const lists_key = buffer_readstring(b, offset + 4, lists_key_length)
    offset += 4 + lists_key_length
    const lists_value_count = buffer_readu32(b, offset)
    if b_len < offset + 4 + lists_value_count * 4 then
      return { ok = false, error = { kind = "invalid_length", field = "lists[v]" } }
    end
    const lists_value = {}
    offset += 4
    for i_2 = 1, lists_value_count do
      const lists_value_element = buffer_readf32(b, offset)
      offset += 4
      lists_value[i_2] = lists_value_element
    end
    lists[lists_key] = lists_value
  end
  return { ok = true, value = { flags = flags, entries = entries, lists = lists } }
end
type Entry = {
  read level: number,
  read label: string,
}
type Registry = {
  read flags: Record<string, boolean>,
  read entries: Record<number, Entry>,
  read lists: Record<string, { number }>,
}
const function _trigger_usage(): ()
  const payload = Registry_serialize_binary((nil :: never) :: Registry)
  const restored = Registry_deserialize_binary(payload)
end
return nil
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_len = buffer.len
const function Grid_serialize_binary(value: Grid): Loom.Serialized
  local size = 0
  size += 4
  const rows_count = #value.rows
  for i = 1, rows_count do
    const rows_element = value.rows[i]
    size += 4
    const rows_element_count = #rows_element
    for i_2 = 1, rows_element_count do
      const rows_element_element = rows_element[i_2]
      size += 4 + #rows_element_element
    end
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, rows_count)
  local offset = 4
  for i = 1, rows_count do
    const rows_element = value.rows[i]
    const rows_element_count = #rows_element
    buffer_writeu32(b, offset, rows_element_count)
    offset += 4
    for i_2 = 1, rows_element_count do
      const rows_element_element = rows_element[i_2]
      const rows_element_element_length = #rows_element_element
      buffer_writeu32(b, offset, rows_element_element_length)
      buffer_writestring(b, offset + 4, rows_element_element)
      offset += 4 + rows_element_element_length
    end
  end
  return { buffer = b }
end
const function Grid_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Grid, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const rows_count = buffer_readu32(b, 0)
  local offset = 4
  const rows = {}
  for i = 1, rows_count do
    const rows_element_count = buffer_readu32(b, offset)
    const rows_element = {}
    offset += 4
    for i_2 = 1, rows_element_count do
      const rows_element_element_length = buffer_readu32(b, offset)
      if b_len < offset + 4 + rows_element_element_length then
        return { ok = false, error = { kind = "invalid_length", field = "rows[][]" } }
      end
      const rows_element_element = buffer_readstring(b, offset + 4, rows_element_element_length)
      offset += 4 + rows_element_element_length
      rows_element[i_2] = rows_element_element
    end
    rows[i] = rows_element
  end
  return { ok = true, value = { rows = rows } }
end
type Grid = {
  read rows: { { string } },
}
const function _trigger_usage(): ()
  const payload = Grid_serialize_binary((nil :: never) :: Grid)
  const restored = Grid_deserialize_binary(payload)
end
return nil
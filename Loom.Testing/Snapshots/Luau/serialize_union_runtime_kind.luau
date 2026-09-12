const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_writef32 = buffer.writef32
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_readbits = buffer.readbits
const buffer_readf32 = buffer.readf32
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_len = buffer.len
const function Cell_serialize_binary(value: Cell): Loom.Serialized
  const content_value = value.content
  local content_tag = 0
  if typeof(content_value) == "string" then
    content_tag = 1
  end
  local size = 1
  if content_tag == 0 then
    size += 4
  elseif content_tag == 1 then
    size += 4 + #value.content
  end
  const b = buffer_create(size)
  buffer_writebits(b, 0, 1, content_tag)
  local offset = 1
  if content_tag == 0 then
    buffer_writef32(b, offset, value.content)
    offset += 4
  elseif content_tag == 1 then
    const content_value = value.content
    const content_length = #content_value
    buffer_writeu32(b, offset, content_length)
    buffer_writestring(b, offset + 4, content_value)
    offset += 4 + content_length
  end
  return { buffer = b }
end
const function Cell_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Cell, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const content_tag = buffer_readbits(b, 0, 1)
  local content = nil
  local offset = 1
  if content_tag == 0 then
    if b_len < offset + 4 then
      return { ok = false, error = { kind = "truncated", field = "content" } }
    end
    const content_2 = buffer_readf32(b, offset)
    content = content_2
    offset += 4
  elseif content_tag == 1 then
    const content_3_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + content_3_length then
      return { ok = false, error = { kind = "invalid_length", field = "content" } }
    end
    const content_3 = buffer_readstring(b, offset + 4, content_3_length)
    offset += 4 + content_3_length
    content = content_3
  else
    return { ok = false, error = { kind = "invalid_tag", field = "content" } }
  end
  return { ok = true, value = { content = content } }
end
type Cell = {
  read content: number | string,
}
const function _trigger_usage(): ()
  const payload = Cell_serialize_binary((nil :: never) :: Cell)
  const restored = Cell_deserialize_binary(payload)
end
return nil
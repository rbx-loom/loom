const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_writebits = buffer.writebits
const buffer_writestring = buffer.writestring
const buffer_readu32 = buffer.readu32
const buffer_readbits = buffer.readbits
const buffer_readstring = buffer.readstring
const buffer_len = buffer.len
const function Canvas_serialize_binary(value: Canvas): Loom.Serialized
  local size = 0
  size += 4
  const masks_count = #value.masks
  for i = 1, masks_count do
    const masks_element = value.masks[i]
    size += 4 + (#masks_element + 7) // 8
  end
  size += 4
  const shapes_count = #value.shapes
  size += (shapes_count + 7) // 8
  for i = 1, shapes_count do
    const shapes_element = value.shapes[i]
    local shapes_element_tag = 0
    if shapes_element.kind == "Line" then
      shapes_element_tag = 1
    end
    if shapes_element_tag == 1 then
      size += 4 + #shapes_element.label
    end
  end
  const b = buffer_create(size)
  buffer_writeu32(b, 0, masks_count)
  local offset = 4
  for i = 1, masks_count do
    const masks_element = value.masks[i]
    const masks_element_count = #masks_element
    buffer_writeu32(b, offset, masks_element_count)
    offset += 4
    const masks_element_bits = offset * 8
    offset += (masks_element_count + 7) // 8
    for i_2 = 1, masks_element_count do
      const masks_element_element = masks_element[i_2]
      buffer_writebits(b, masks_element_bits + (i_2 - 1), 1, if masks_element_element then 1 else 0)
    end
  end
  buffer_writeu32(b, offset, shapes_count)
  offset += 4
  const shapes_bits = offset * 8
  offset += (shapes_count + 7) // 8
  for i = 1, shapes_count do
    const shapes_element = value.shapes[i]
    local shapes_element_tag = 0
    if shapes_element.kind == "Line" then
      shapes_element_tag = 1
    end
    buffer_writebits(b, shapes_bits + (i - 1), 1, shapes_element_tag)
    if shapes_element_tag == 1 then
      const label_value = shapes_element.label
      const label_length = #label_value
      buffer_writeu32(b, offset, label_length)
      buffer_writestring(b, offset + 4, label_value)
      offset += 4 + label_length
    end
  end
  return { buffer = b }
end
const function Canvas_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Canvas, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 8 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const masks_count = buffer_readu32(b, 0)
  local offset = 4
  const masks = {}
  for i = 1, masks_count do
    const masks_element_count = buffer_readu32(b, offset)
    offset += 4
    const masks_element_bits = offset * 8
    offset += (masks_element_count + 7) // 8
    const masks_element = {}
    for i_2 = 1, masks_element_count do
      masks_element[i_2] = buffer_readbits(b, masks_element_bits + (i_2 - 1), 1) == 1
    end
    masks[i] = masks_element
  end
  const shapes_count = buffer_readu32(b, offset)
  offset += 4
  const shapes_bits = offset * 8
  offset += (shapes_count + 7) // 8
  const shapes = {}
  for i = 1, shapes_count do
    const shapes_element_tag = buffer_readbits(b, shapes_bits + (i - 1), 1)
    local shapes_element = nil
    if shapes_element_tag == 0 then
      shapes_element = { kind = "Dot" }
    elseif shapes_element_tag == 1 then
      const label_length = buffer_readu32(b, offset)
      if b_len < offset + 4 + label_length then
        return { ok = false, error = { kind = "invalid_length", field = "shapes[].label" } }
      end
      const label = buffer_readstring(b, offset + 4, label_length)
      offset += 4 + label_length
      shapes_element = { kind = "Line", label = label }
    else
      return { ok = false, error = { kind = "invalid_tag", field = "shapes[]" } }
    end
    shapes[i] = shapes_element
  end
  return { ok = true, value = { masks = masks, shapes = shapes } }
end
type IShape<Kind> = {
  read kind: Kind & string,
}
type Dot = IShape<"Dot"> & {}
type Line = IShape<"Line"> & {
  read label: string,
}
type Canvas = {
  read masks: { { boolean } },
  read shapes: { (Dot | Line) },
}
const function _trigger_usage(): ()
  const payload = Canvas_serialize_binary((nil :: never) :: Canvas)
  const restored = Canvas_deserialize_binary(payload)
end
return nil
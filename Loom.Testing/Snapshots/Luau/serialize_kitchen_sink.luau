const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_writeu8 = buffer.writeu8
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writei16 = buffer.writei16
const buffer_writef32 = buffer.writef32
const buffer_readbits = buffer.readbits
const buffer_readu8 = buffer.readu8
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readi16 = buffer.readi16
const buffer_readf32 = buffer.readf32
const buffer_len = buffer.len
const function KitchenSink_serialize_binary(value: KitchenSink): Loom.Serialized
  const blobs = {}
  const payload_value = value.payload
  local payload_tag = 0
  if payload_value.kind == "Chat" then
    payload_tag = 1
  end
  local size = 31 + 4 + #value.points * 6
  if value.label ~= nil then
    size += 4 + #value.label
  end
  if payload_tag == 1 then
    size += 4 + #value.payload.message
  end
  const b = buffer_create(size)
  buffer_writebits(b, 0, 1, if value.inner.flag then 1 else 0)
  buffer_writeu8(b, 2, value.inner.code)
  buffer_writebits(b, 1, 1, if value.label ~= nil then 1 else 0)
  local offset = 3
  if value.label ~= nil then
    const label_value = value.label
    const label_length = #label_value
    buffer_writeu32(b, offset, label_length)
    buffer_writestring(b, offset + 4, label_value)
    offset += 4 + label_length
  end
  const points_count = #value.points
  buffer_writeu32(b, offset, points_count)
  offset += 4
  for i = 1, points_count do
    const points_element = value.points[i]
    buffer_writei16(b, offset, points_element.X)
    buffer_writei16(b, offset + 2, points_element.Y)
    buffer_writei16(b, offset + 4, points_element.Z)
    offset += 6
  end
  buffer_writebits(b, 2, 1, payload_tag)
  if payload_tag == 1 then
    const message_value = value.payload.message
    const message_length = #message_value
    buffer_writeu32(b, offset, message_length)
    buffer_writestring(b, offset + 4, message_value)
    offset += 4 + message_length
    buffer_writebits(b, 3, 7, math.round(value.payload.volume))
  end
  table.insert(blobs, value.owner)
  buffer_writef32(b, offset, value.frame.X)
  buffer_writef32(b, offset + 4, value.frame.Y)
  buffer_writef32(b, offset + 8, value.frame.Z)
  const qx, qy, qz, qw = Loom.cframe_to_quaternion(value.frame)
  buffer_writef32(b, offset + 12, qx)
  buffer_writef32(b, offset + 16, qy)
  buffer_writef32(b, offset + 20, qz)
  buffer_writef32(b, offset + 24, qw)
  return { buffer = b, blobs = blobs }
end
const function KitchenSink_deserialize_binary(serialized: Loom.Serialized): Loom.Result<KitchenSink, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 35 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const blobs = serialized.blobs
  if blobs == nil then
    return { ok = false, error = { kind = "missing_blob" } }
  end
  local blob_index = 1
  const label_present = buffer_readbits(b, 1, 1) == 1
  local label = nil
  local offset = 3
  if label_present then
    const label_2_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + label_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "label" } }
    end
    const label_2 = buffer_readstring(b, offset + 4, label_2_length)
    offset += 4 + label_2_length
    label = label_2
  end
  const points_count = buffer_readu32(b, offset)
  if b_len < offset + 4 + points_count * 6 then
    return { ok = false, error = { kind = "invalid_length", field = "points" } }
  end
  const points = {}
  offset += 4
  for i = 1, points_count do
    const points_element_x = buffer_readi16(b, offset)
    const points_element_y = buffer_readi16(b, offset + 2)
    const points_element_z = buffer_readi16(b, offset + 4)
    offset += 6
    points[i] = Vector3.new(points_element_x, points_element_y, points_element_z)
  end
  const payload_tag = buffer_readbits(b, 2, 1)
  local payload = nil
  if payload_tag == 0 then
    payload = { kind = "Ping" }
  elseif payload_tag == 1 then
    const message_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + message_length then
      return { ok = false, error = { kind = "invalid_length", field = "payload.message" } }
    end
    const message = buffer_readstring(b, offset + 4, message_length)
    offset += 4 + message_length
    payload = { kind = "Chat", message = message, volume = buffer_readbits(b, 3, 7) }
  else
    return { ok = false, error = { kind = "invalid_tag", field = "payload" } }
  end
  const owner_blob = blobs[blob_index]
  blob_index += 1
  if owner_blob == nil then
    return { ok = false, error = { kind = "missing_blob", field = "owner" } }
  end
  if typeof(owner_blob) ~= "Instance" or not owner_blob:IsA("Part") then
    return { ok = false, error = { kind = "invalid_blob", field = "owner" } }
  end
  const frame_x = buffer_readf32(b, offset)
  const frame_y = buffer_readf32(b, offset + 4)
  const frame_z = buffer_readf32(b, offset + 8)
  const frame_qx = buffer_readf32(b, offset + 12)
  const frame_qy = buffer_readf32(b, offset + 16)
  const frame_qz = buffer_readf32(b, offset + 20)
  const frame_qw = buffer_readf32(b, offset + 24)
  return { ok = true, value = {
    tag = "sink",
    inner = { flag = buffer_readbits(b, 0, 1) == 1, code = buffer_readu8(b, 2) },
    label = label,
    points = points,
    payload = payload,
    owner = owner_blob,
    frame = CFrame.new(frame_x, frame_y, frame_z, frame_qx, frame_qy, frame_qz, frame_qw),
  } }
end
type IEvent<Kind> = {
  read kind: Kind & string,
}
type Ping = IEvent<"Ping"> & {}
type Chat = IEvent<"Chat"> & {
  read message: string,
  read volume: number,
}
type Inner = {
  read flag: boolean,
  read code: number,
}
type KitchenSink = {
  read tag: "sink",
  read inner: Inner,
  read label: string?,
  read points: { Vector3 },
  read payload: Ping | Chat,
  read owner: Part,
  read frame: CFrame,
}
const function _trigger_usage(): ()
  const payload = KitchenSink_serialize_binary((nil :: never) :: KitchenSink)
  const restored = KitchenSink_deserialize_binary(payload)
end
return nil
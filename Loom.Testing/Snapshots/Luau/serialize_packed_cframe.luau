const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_writef32 = buffer.writef32
const buffer_writeu32 = buffer.writeu32
const buffer_readbits = buffer.readbits
const buffer_readf32 = buffer.readf32
const buffer_readu32 = buffer.readu32
const buffer_len = buffer.len
const function Waypoint_serialize_binary(value: Waypoint): Loom.Serialized
  const frame_value = value.frame
  local frame_sentinel = 0
  if frame_value == CFrame.identity then
    frame_sentinel = 1
  end
  const b = buffer_create(1 + (if frame_sentinel == 0 then 16 else 0))
  buffer_writebits(b, 0, 1, frame_sentinel)
  local offset = 1
  if frame_sentinel == 0 then
    buffer_writef32(b, offset, frame_value.X)
    buffer_writef32(b, offset + 4, frame_value.Y)
    buffer_writef32(b, offset + 8, frame_value.Z)
    buffer_writeu32(b, offset + 12, Loom.pack_quaternion(Loom.cframe_to_quaternion(frame_value)))
    offset += 16
  end
  return { buffer = b }
end
const function Waypoint_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Waypoint, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const frame_sentinel = buffer_readbits(b, 0, 1)
  local frame = nil
  local offset = 1
  if frame_sentinel == 0 then
    if b_len < offset + 16 then
      return { ok = false, error = { kind = "truncated", field = "frame" } }
    end
    const frame_x = buffer_readf32(b, offset)
    const frame_y = buffer_readf32(b, offset + 4)
    const frame_z = buffer_readf32(b, offset + 8)
    const frame_rotation = buffer_readu32(b, offset + 12)
    frame = CFrame.new(frame_x, frame_y, frame_z, Loom.unpack_quaternion(frame_rotation))
    offset += 16
  elseif frame_sentinel == 1 then
    frame = CFrame.identity
  else
    return { ok = false, error = { kind = "invalid_tag", field = "frame" } }
  end
  return { ok = true, value = { frame = frame } }
end
type Waypoint = {
  read frame: CFrame,
}
const function _trigger_usage(): ()
  const payload = Waypoint_serialize_binary((nil :: never) :: Waypoint)
  const restored = Waypoint_deserialize_binary(payload)
end
return nil
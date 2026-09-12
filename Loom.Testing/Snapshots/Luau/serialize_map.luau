const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_readu8 = buffer.readu8
const buffer_len = buffer.len
const buffer_writebits = buffer.writebits
const buffer_copy = buffer.copy
const buffer_readbits = buffer.readbits
const function ShootGunPacket_serialize_binary(value: ShootGunPacket): Loom.Serialized
  const b = buffer_create(1)
  buffer_writeu8(b, 0, value.velocity)
  return { buffer = b }
end
const function ShootGunPacket_deserialize_binary(serialized: Loom.Serialized): Loom.Result<ShootGunPacket, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { velocity = buffer_readu8(b, 0) } }
end
const function ShootGunPacket_diff_binary_write(baseline: ShootGunPacket, value: ShootGunPacket, b: buffer)
  local offset = 1
  const velocity_changed = not Loom.deep_equal(baseline.velocity, value.velocity)
  buffer_writebits(b, 0, 1, if velocity_changed then 1 else 0)
  if velocity_changed then
    buffer_writeu8(b, offset, value.velocity)
    offset += 1
  end
  return offset
end
const function ShootGunPacket_diff_binary_attempt(baseline: ShootGunPacket, value: ShootGunPacket, size: number)
  const scratch = buffer_create(size)
  const ok, written = pcall(ShootGunPacket_diff_binary_write, baseline, value, scratch)
  if ok then
    return scratch, written
  end
  return ShootGunPacket_diff_binary_attempt(baseline, value, size * 2)
end
const function ShootGunPacket_diff_binary(baseline: ShootGunPacket, value: ShootGunPacket): Loom.Serialized
  const baseline_s = ShootGunPacket_serialize_binary(baseline)
  const value_s = ShootGunPacket_serialize_binary(value)
  const size = (if baseline_s.buffer ~= nil then buffer.len(baseline_s.buffer) else 0) + (if value_s.buffer ~= nil then buffer.len(value_s.buffer) else 0) + 1
  const scratch, written = ShootGunPacket_diff_binary_attempt(baseline, value, size)
  const result = buffer_create(written)
  buffer_copy(result, 0, scratch, 0, written)
  return { buffer = result }
end
const function ShootGunPacket_apply_diff_binary_read(baseline: ShootGunPacket, diff: Loom.Serialized): Loom.Result<ShootGunPacket, Loom.DeserializeError>
  const b = diff.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  local offset = 1
  const velocity_changed = buffer_readbits(b, 0, 1) == 1
  local velocity = nil
  if velocity_changed then
    const velocity_2 = buffer_readu8(b, offset)
    velocity = velocity_2
    offset += 1
  else
    velocity = baseline.velocity
  end
  return { ok = true, value = { velocity = velocity } }
end
const function ShootGunPacket_apply_diff_binary(baseline: ShootGunPacket, diff: Loom.Serialized): Loom.Result<ShootGunPacket, Loom.DeserializeError>
  const ok, result = pcall(ShootGunPacket_apply_diff_binary_read, baseline, diff)
  if ok then
    return result
  end
  return { ok = false, error = { kind = "truncated" } }
end
const ShootGunPacket_serializer: Loom.Serializer<ShootGunPacket> = { serialize = ShootGunPacket_serialize_binary, deserialize = ShootGunPacket_deserialize_binary, diff = ShootGunPacket_diff_binary, apply_diff = ShootGunPacket_apply_diff_binary }
const MessageData_serializer_map = { [0] = ShootGunPacket_serializer }
type Message = number
type ShootGunPacket = {
  read velocity: number,
}
type MessageData = { read [number]: ShootGunPacket }
const function create_message_serializer<K>(message: K & Message): Loom.Serializer<index<MessageData, K & Message>>
  return MessageData_serializer_map[message]
end
return nil
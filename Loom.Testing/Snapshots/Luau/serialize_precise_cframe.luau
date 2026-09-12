const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writef32 = buffer.writef32
const buffer_writebits = buffer.writebits
const buffer_readf32 = buffer.readf32
const buffer_readbits = buffer.readbits
const buffer_len = buffer.len
const function Anchor_serialize_binary(value: Anchor): Loom.Serialized
  const b = buffer_create(44)
  buffer_writef32(b, 4, value.exact.X)
  buffer_writef32(b, 8, value.exact.Y)
  buffer_writef32(b, 12, value.exact.Z)
  const qx, qy, qz, qw = Loom.cframe_to_quaternion(value.exact)
  buffer_writef32(b, 16, qx)
  buffer_writef32(b, 20, qy)
  buffer_writef32(b, 24, qz)
  buffer_writef32(b, 28, qw)
  buffer_writef32(b, 32, value.approximate.X)
  buffer_writef32(b, 36, value.approximate.Y)
  buffer_writef32(b, 40, value.approximate.Z)
  buffer_writebits(b, 0, 32, Loom.pack_quaternion(Loom.cframe_to_quaternion(value.approximate)))
  return { buffer = b }
end
const function Anchor_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Anchor, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 44 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { exact = CFrame.new(buffer_readf32(b, 4), buffer_readf32(b, 8), buffer_readf32(b, 12), buffer_readf32(b, 16), buffer_readf32(b, 20), buffer_readf32(b, 24), buffer_readf32(b, 28)), approximate = CFrame.new(buffer_readf32(b, 32), buffer_readf32(b, 36), buffer_readf32(b, 40), Loom.unpack_quaternion(buffer_readbits(b, 0, 32))) } }
end
type Anchor = {
  read exact: CFrame,
  read approximate: CFrame,
}
const function _trigger_usage(): ()
  const payload = Anchor_serialize_binary((nil :: never) :: Anchor)
  const restored = Anchor_deserialize_binary(payload)
end
return nil
const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writei8 = buffer.writei8
const buffer_writeu8 = buffer.writeu8
const buffer_writei16 = buffer.writei16
const buffer_writeu16 = buffer.writeu16
const buffer_writei32 = buffer.writei32
const buffer_writeu32 = buffer.writeu32
const buffer_writef32 = buffer.writef32
const buffer_writef64 = buffer.writef64
const buffer_readi8 = buffer.readi8
const buffer_readu8 = buffer.readu8
const buffer_readi16 = buffer.readi16
const buffer_readu16 = buffer.readu16
const buffer_readi32 = buffer.readi32
const buffer_readu32 = buffer.readu32
const buffer_readf32 = buffer.readf32
const buffer_readf64 = buffer.readf64
const buffer_len = buffer.len
const function Limits_serialize_binary(value: Limits): Loom.Serialized
  const b = buffer_create(26)
  buffer_writei8(b, 0, value.i8)
  buffer_writeu8(b, 1, value.u8)
  buffer_writei16(b, 2, value.i16)
  buffer_writeu16(b, 4, value.u16)
  buffer_writei32(b, 6, value.i32)
  buffer_writeu32(b, 10, value.u32)
  buffer_writef32(b, 14, value.f32)
  buffer_writef64(b, 18, value.f64)
  return { buffer = b }
end
const function Limits_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Limits, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 26 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = {
    i8 = buffer_readi8(b, 0),
    u8 = buffer_readu8(b, 1),
    i16 = buffer_readi16(b, 2),
    u16 = buffer_readu16(b, 4),
    i32 = buffer_readi32(b, 6),
    u32 = buffer_readu32(b, 10),
    f32 = buffer_readf32(b, 14),
    f64 = buffer_readf64(b, 18),
  } }
end
type Limits = {
  read i8: number,
  read u8: number,
  read i16: number,
  read u16: number,
  read i32: number,
  read u32: number,
  read f32: number,
  read f64: number,
}
const function _trigger_usage(): ()
  const payload = Limits_serialize_binary((nil :: never) :: Limits)
  const restored = Limits_deserialize_binary(payload)
end
return nil
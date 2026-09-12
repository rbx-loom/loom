const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writebits = buffer.writebits
const buffer_writeu8 = buffer.writeu8
const buffer_readbits = buffer.readbits
const buffer_readu8 = buffer.readu8
const buffer_len = buffer.len
const function Envelope_serialize_binary(value: Envelope): Loom.Serialized
  const action_value = value.action
  local action_tag = 0
  if action_value.kind == "Click" then
    action_tag = 1
  end
  const color_value = value.color
  local color_tag = 0
  if color_value == "green" then
    color_tag = 1
  elseif color_value == "blue" then
    color_tag = 2
  end
  local size = 1
  if action_tag == 1 then
    size += 1
  end
  const b = buffer_create(size)
  buffer_writebits(b, 0, 1, action_tag)
  local offset = 1
  if action_tag == 1 then
    buffer_writeu8(b, offset, value.action.x)
    offset += 1
  end
  buffer_writebits(b, 1, 2, color_tag)
  return { buffer = b }
end
const function Envelope_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Envelope, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const action_tag = buffer_readbits(b, 0, 1)
  local action = nil
  local offset = 1
  if action_tag == 0 then
    action = { kind = "LogOut" }
  elseif action_tag == 1 then
    if b_len < offset + 1 then
      return { ok = false, error = { kind = "truncated", field = "action" } }
    end
    const x = buffer_readu8(b, offset)
    action = { kind = "Click", x = x }
    offset += 1
  else
    return { ok = false, error = { kind = "invalid_tag", field = "action" } }
  end
  const color_tag = buffer_readbits(b, 1, 2)
  local color = nil
  if color_tag == 0 then
    color = "red"
  elseif color_tag == 1 then
    color = "green"
  elseif color_tag == 2 then
    color = "blue"
  else
    return { ok = false, error = { kind = "invalid_tag", field = "color" } }
  end
  return { ok = true, value = { action = action, color = color } }
end
type IAction<Kind> = {
  read kind: Kind & string,
}
type LogOutAction = IAction<"LogOut"> & {}
type ClickAction = IAction<"Click"> & {
  read x: number,
}
type Envelope = {
  read action: LogOutAction | ClickAction,
  read color: "red" | "green" | "blue",
}
const function _trigger_usage(): ()
  const payload = Envelope_serialize_binary((nil :: never) :: Envelope)
  const restored = Envelope_deserialize_binary(payload)
end
return nil
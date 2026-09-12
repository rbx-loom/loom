const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu16 = buffer.writeu16
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writebits = buffer.writebits
const buffer_writei16 = buffer.writei16
const buffer_writef32 = buffer.writef32
const buffer_writeu8 = buffer.writeu8
const buffer_readu16 = buffer.readu16
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readbits = buffer.readbits
const buffer_readi16 = buffer.readi16
const buffer_readf32 = buffer.readf32
const buffer_readu8 = buffer.readu8
const buffer_len = buffer.len
const function PlayerSnapshot_serialize_binary(value: PlayerSnapshot): Loom.Serialized
  const blobs = {}
  const team_value = value.team
  local team_tag = 0
  if team_value == 1 then
    team_tag = 1
  elseif team_value == 2 then
    team_tag = 2
  end
  const velocity_value = value.velocity
  local velocity_sentinel = 0
  if velocity_value == Vector3.zero then
    velocity_sentinel = 1
  elseif velocity_value == Vector3.one then
    velocity_sentinel = 2
  elseif velocity_value == Vector3.xAxis then
    velocity_sentinel = 3
  elseif velocity_value == Vector3.yAxis then
    velocity_sentinel = 4
  elseif velocity_value == Vector3.zAxis then
    velocity_sentinel = 5
  end
  const aim_value = value.aim
  local aim_sentinel = 0
  if aim_value == CFrame.identity then
    aim_sentinel = 1
  end
  local size = 6 + 4 + #value.display_name + (if velocity_sentinel == 0 then 6 else 0) + (if aim_sentinel == 0 then 16 else 0)
  size += 4
  const recent_count = #value.recent
  size += (recent_count * 12 + 7) // 8
  for i = 1, recent_count do
    const recent_element = value.recent[i]
    local recent_element_tag = 0
    if recent_element.kind == "Respawned" then
      recent_element_tag = 1
    end
    if recent_element_tag == 1 then
      size += 6
    end
  end
  const b = buffer_create(size)
  buffer_writeu16(b, 2, value.user_id)
  const display_name_value = value.display_name
  const display_name_length = #display_name_value
  buffer_writeu32(b, 4, display_name_length)
  local offset = 8
  buffer_writestring(b, offset, display_name_value)
  offset += display_name_length
  buffer_writebits(b, 0, 7, math.round(value.health))
  buffer_writebits(b, 7, 1, if value.alive then 1 else 0)
  buffer_writebits(b, 8, 2, team_tag)
  buffer_writebits(b, 10, 3, velocity_sentinel)
  if velocity_sentinel == 0 then
    buffer_writei16(b, offset, velocity_value.X)
    buffer_writei16(b, offset + 2, velocity_value.Y)
    buffer_writei16(b, offset + 4, velocity_value.Z)
    offset += 6
  end
  buffer_writebits(b, 13, 1, aim_sentinel)
  if aim_sentinel == 0 then
    buffer_writef32(b, offset, aim_value.X)
    buffer_writef32(b, offset + 4, aim_value.Y)
    buffer_writef32(b, offset + 8, aim_value.Z)
    buffer_writeu32(b, offset + 12, Loom.pack_quaternion(Loom.cframe_to_quaternion(aim_value)))
    offset += 16
  end
  buffer_writeu8(b, offset, value.loadout.primary)
  buffer_writeu8(b, offset + 1, value.loadout.secondary)
  buffer_writeu32(b, offset + 2, recent_count)
  offset += 6
  const recent_bits = offset * 8
  offset += (recent_count * 12 + 7) // 8
  for i = 1, recent_count do
    const recent_element = value.recent[i]
    local recent_element_tag = 0
    if recent_element.kind == "Respawned" then
      recent_element_tag = 1
    end
    buffer_writebits(b, recent_bits + (i - 1) * 12, 1, recent_element_tag)
    if recent_element_tag == 0 then
      buffer_writebits(b, recent_bits + (i - 1) * 12 + 1, 10, math.round(recent_element.amount))
      buffer_writebits(b, recent_bits + (i - 1) * 12 + 11, 1, if recent_element.attacker ~= nil then 1 else 0)
      if recent_element.attacker ~= nil then
        table.insert(blobs, recent_element.attacker)
      end
    elseif recent_element_tag == 1 then
      const at_value = recent_element.at
      buffer_writei16(b, offset, at_value.X)
      buffer_writei16(b, offset + 2, at_value.Y)
      buffer_writei16(b, offset + 4, at_value.Z)
      offset += 6
    end
  end
  buffer_writebits(b, 14, 1, if value.character ~= nil then 1 else 0)
  if value.character ~= nil then
    table.insert(blobs, value.character)
  end
  return { buffer = b, blobs = blobs }
end
const function PlayerSnapshot_deserialize_binary(serialized: Loom.Serialized): Loom.Result<PlayerSnapshot, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 14 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const blobs = serialized.blobs
  if blobs == nil then
    return { ok = false, error = { kind = "missing_blob" } }
  end
  local blob_index = 1
  const display_name_length = buffer_readu32(b, 4)
  local offset = 8
  if b_len < offset + display_name_length then
    return { ok = false, error = { kind = "invalid_length", field = "display_name" } }
  end
  const display_name = buffer_readstring(b, offset, display_name_length)
  offset += display_name_length
  const team_tag = buffer_readbits(b, 8, 2)
  local team = nil
  if team_tag == 0 then
    team = 0
  elseif team_tag == 1 then
    team = 1
  elseif team_tag == 2 then
    team = 2
  else
    return { ok = false, error = { kind = "invalid_tag", field = "team" } }
  end
  const velocity_sentinel = buffer_readbits(b, 10, 3)
  local velocity = nil
  if velocity_sentinel == 0 then
    if b_len < offset + 6 then
      return { ok = false, error = { kind = "truncated", field = "velocity" } }
    end
    const velocity_x = buffer_readi16(b, offset)
    const velocity_y = buffer_readi16(b, offset + 2)
    const velocity_z = buffer_readi16(b, offset + 4)
    velocity = Vector3.new(velocity_x, velocity_y, velocity_z)
    offset += 6
  elseif velocity_sentinel == 1 then
    velocity = Vector3.zero
  elseif velocity_sentinel == 2 then
    velocity = Vector3.one
  elseif velocity_sentinel == 3 then
    velocity = Vector3.xAxis
  elseif velocity_sentinel == 4 then
    velocity = Vector3.yAxis
  elseif velocity_sentinel == 5 then
    velocity = Vector3.zAxis
  else
    return { ok = false, error = { kind = "invalid_tag", field = "velocity" } }
  end
  const aim_sentinel = buffer_readbits(b, 13, 1)
  local aim = nil
  if aim_sentinel == 0 then
    if b_len < offset + 16 then
      return { ok = false, error = { kind = "truncated", field = "aim" } }
    end
    const aim_x = buffer_readf32(b, offset)
    const aim_y = buffer_readf32(b, offset + 4)
    const aim_z = buffer_readf32(b, offset + 8)
    const aim_rotation = buffer_readu32(b, offset + 12)
    aim = CFrame.new(aim_x, aim_y, aim_z, Loom.unpack_quaternion(aim_rotation))
    offset += 16
  elseif aim_sentinel == 1 then
    aim = CFrame.identity
  else
    return { ok = false, error = { kind = "invalid_tag", field = "aim" } }
  end
  const primary = buffer_readu8(b, offset)
  const secondary = buffer_readu8(b, offset + 1)
  const recent_count = buffer_readu32(b, offset + 2)
  offset += 6
  const recent_bits = offset * 8
  offset += (recent_count * 12 + 7) // 8
  const recent = {}
  for i = 1, recent_count do
    const recent_element_tag = buffer_readbits(b, recent_bits + (i - 1) * 12, 1)
    local recent_element = nil
    if recent_element_tag == 0 then
      const attacker_present = buffer_readbits(b, recent_bits + (i - 1) * 12 + 11, 1) == 1
      local attacker = nil
      if attacker_present then
        const attacker_blob = blobs[blob_index]
        blob_index += 1
        if attacker_blob == nil then
          return { ok = false, error = { kind = "missing_blob", field = "recent[].attacker" } }
        end
        if typeof(attacker_blob) ~= "Instance" or not attacker_blob:IsA("Player") then
          return { ok = false, error = { kind = "invalid_blob", field = "recent[].attacker" } }
        end
        attacker = attacker_blob
      end
      recent_element = { kind = "Damaged", amount = buffer_readbits(b, recent_bits + (i - 1) * 12 + 1, 10), attacker = attacker }
    elseif recent_element_tag == 1 then
      if b_len < offset + 6 then
        return { ok = false, error = { kind = "truncated", field = "recent[]" } }
      end
      const at_x = buffer_readi16(b, offset)
      const at_y = buffer_readi16(b, offset + 2)
      const at_z = buffer_readi16(b, offset + 4)
      recent_element = { kind = "Respawned", at = Vector3.new(at_x, at_y, at_z) }
      offset += 6
    else
      return { ok = false, error = { kind = "invalid_tag", field = "recent[]" } }
    end
    recent[i] = recent_element
  end
  const character_present = buffer_readbits(b, 14, 1) == 1
  local character = nil
  if character_present then
    const character_blob = blobs[blob_index]
    blob_index += 1
    if character_blob == nil then
      return { ok = false, error = { kind = "missing_blob", field = "character" } }
    end
    if typeof(character_blob) ~= "Instance" or not character_blob:IsA("Model") then
      return { ok = false, error = { kind = "invalid_blob", field = "character" } }
    end
    character = character_blob
  end
  return { ok = true, value = {
    user_id = buffer_readu16(b, 2),
    display_name = display_name,
    health = buffer_readbits(b, 0, 7),
    alive = buffer_readbits(b, 7, 1) == 1,
    team = team,
    velocity = velocity,
    aim = aim,
    loadout = { primary = primary, secondary = secondary },
    recent = recent,
    character = character,
  } }
end
-- Server-to-client replication for one player, sent every tick.
type Team = number
type IEvent<Kind> = {
  read kind: Kind & string,
}
type Damaged = IEvent<"Damaged"> & {
  read amount: number,
  read attacker: Player?,
}
type Respawned = IEvent<"Respawned"> & {
  read at: Vector3,
}
type Loadout = {
  read primary: number,
  read secondary: number,
}
type PlayerSnapshot = {
  read user_id: number,
  read display_name: string,
  read health: number,
  read alive: boolean,
  read team: Team,
  read velocity: Vector3,
  read aim: CFrame,
  read loadout: Loadout,
  read recent: { (Damaged | Respawned) },
  read character: Model?,
}
const function _trigger_usage(): ()
  const payload = PlayerSnapshot_serialize_binary((nil :: never) :: PlayerSnapshot)
  const restored = PlayerSnapshot_deserialize_binary(payload)
end
return nil
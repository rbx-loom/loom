const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_writebits = buffer.writebits
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_readu8 = buffer.readu8
const buffer_readbits = buffer.readbits
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_len = buffer.len
const function Profile_serialize_binary(value: Profile): Loom.Serialized
  local size = 2
  if value.nickname ~= nil then
    size += 4 + #value.nickname
  end
  const b = buffer_create(size)
  buffer_writeu8(b, 1, value.level)
  buffer_writebits(b, 0, 1, if value.nickname ~= nil then 1 else 0)
  local offset = 2
  if value.nickname ~= nil then
    const nickname_value = value.nickname
    const nickname_length = #nickname_value
    buffer_writeu32(b, offset, nickname_length)
    buffer_writestring(b, offset + 4, nickname_value)
    offset += 4 + nickname_length
  end
  return { buffer = b }
end
const function Profile_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Profile, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const nickname_present = buffer_readbits(b, 0, 1) == 1
  local nickname = nil
  local offset = 2
  if nickname_present then
    const nickname_2_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + nickname_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "nickname" } }
    end
    const nickname_2 = buffer_readstring(b, offset + 4, nickname_2_length)
    offset += 4 + nickname_2_length
    nickname = nickname_2
  end
  return { ok = true, value = { level = buffer_readu8(b, 1), nickname = nickname } }
end
const function Account_serialize_binary(value: Account): Loom.Serialized
  local size = 1
  size += 1
  if value.profile.nickname ~= nil then
    size += 4 + #value.profile.nickname
  end
  if value.tag ~= nil then
    size += 4 + #value.tag
  end
  const b = buffer_create(size)
  buffer_writeu8(b, 1, value.profile.level)
  buffer_writebits(b, 0, 1, if value.profile.nickname ~= nil then 1 else 0)
  local offset = 2
  if value.profile.nickname ~= nil then
    const nickname_value = value.profile.nickname
    const nickname_length = #nickname_value
    buffer_writeu32(b, offset, nickname_length)
    buffer_writestring(b, offset + 4, nickname_value)
    offset += 4 + nickname_length
  end
  buffer_writebits(b, 1, 1, if value.tag ~= nil then 1 else 0)
  if value.tag ~= nil then
    const tag_value = value.tag
    const tag_length = #tag_value
    buffer_writeu32(b, offset, tag_length)
    buffer_writestring(b, offset + 4, tag_value)
    offset += 4 + tag_length
  end
  return { buffer = b }
end
const function Account_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Account, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const nickname_present = buffer_readbits(b, 0, 1) == 1
  local nickname = nil
  local offset = 2
  if nickname_present then
    const nickname_2_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + nickname_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "profile.nickname" } }
    end
    const nickname_2 = buffer_readstring(b, offset + 4, nickname_2_length)
    offset += 4 + nickname_2_length
    nickname = nickname_2
  end
  const tag_present = buffer_readbits(b, 1, 1) == 1
  local tag = nil
  if tag_present then
    const tag_2_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + tag_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "tag" } }
    end
    const tag_2 = buffer_readstring(b, offset + 4, tag_2_length)
    offset += 4 + tag_2_length
    tag = tag_2
  end
  return { ok = true, value = { profile = { level = buffer_readu8(b, 1), nickname = nickname }, tag = tag } }
end
type Profile = {
  read level: number,
  read nickname: string?,
}
type Account = {
  read profile: Profile,
  read tag: string?,
}
const function _trigger_usage(): ()
  const profile_payload = Profile_serialize_binary((nil :: never) :: Profile)
  const profile_restored = Profile_deserialize_binary(profile_payload)
  const account_payload = Account_serialize_binary((nil :: never) :: Account)
  const account_restored = Account_deserialize_binary(account_payload)
end
return nil
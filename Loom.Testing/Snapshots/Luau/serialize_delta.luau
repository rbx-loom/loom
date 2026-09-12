const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu16 = buffer.writeu16
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writeu8 = buffer.writeu8
const buffer_writebits = buffer.writebits
const buffer_writei16 = buffer.writei16
const buffer_writef32 = buffer.writef32
const buffer_copy = buffer.copy
const buffer_readbits = buffer.readbits
const buffer_readu16 = buffer.readu16
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readu8 = buffer.readu8
const buffer_readi16 = buffer.readi16
const buffer_readf32 = buffer.readf32
const buffer_len = buffer.len
const function PlayerState_serialize_binary(value: PlayerState): Loom.Serialized
  const position_value = value.position
  local position_sentinel = 0
  if position_value == Vector3.zero then
    position_sentinel = 1
  elseif position_value == Vector3.one then
    position_sentinel = 2
  elseif position_value == Vector3.xAxis then
    position_sentinel = 3
  elseif position_value == Vector3.yAxis then
    position_sentinel = 4
  elseif position_value == Vector3.zAxis then
    position_sentinel = 5
  end
  local size = 5 + 4 + #value.name + (if position_sentinel == 0 then 6 else 0)
  if value.last_event ~= nil then
    const last_event_value = value.last_event
    local last_event_tag = 0
    if last_event_value.kind == "Miss" then
      last_event_tag = 1
    end
    if last_event_tag == 0 then
      size += 4
    end
  end
  size += 4
  const recent_count = #value.recent
  size += (recent_count + 7) // 8
  for i = 1, recent_count do
    const recent_element = value.recent[i]
    local recent_element_tag = 0
    if recent_element.kind == "Miss" then
      recent_element_tag = 1
    end
    if recent_element_tag == 0 then
      size += 4
    end
  end
  size += 4
  local scores_count = 0
  for scores_key, scores_value in value.scores do
    scores_count += 1
    size += 4 + #scores_key
    size += 2
  end
  const b = buffer_create(size)
  buffer_writeu16(b, 1, value.health)
  const name_value = value.name
  const name_length = #name_value
  buffer_writeu32(b, 3, name_length)
  local offset = 7
  buffer_writestring(b, offset, name_value)
  offset += name_length
  buffer_writeu8(b, offset, value.loadout.primary)
  buffer_writeu8(b, offset + 1, value.loadout.secondary)
  buffer_writebits(b, 0, 3, position_sentinel)
  offset += 2
  if position_sentinel == 0 then
    buffer_writei16(b, offset, position_value.X)
    buffer_writei16(b, offset + 2, position_value.Y)
    buffer_writei16(b, offset + 4, position_value.Z)
    offset += 6
  end
  buffer_writebits(b, 3, 1, if value.last_event ~= nil then 1 else 0)
  if value.last_event ~= nil then
    const last_event_value = value.last_event
    local last_event_tag = 0
    if last_event_value.kind == "Miss" then
      last_event_tag = 1
    end
    buffer_writebits(b, 4, 1, last_event_tag)
    if last_event_tag == 0 then
      buffer_writef32(b, offset, value.last_event.amount)
      offset += 4
    end
  end
  buffer_writeu32(b, offset, recent_count)
  offset += 4
  const recent_bits = offset * 8
  offset += (recent_count + 7) // 8
  for i = 1, recent_count do
    const recent_element = value.recent[i]
    local recent_element_tag = 0
    if recent_element.kind == "Miss" then
      recent_element_tag = 1
    end
    buffer_writebits(b, recent_bits + (i - 1), 1, recent_element_tag)
    if recent_element_tag == 0 then
      buffer_writef32(b, offset, recent_element.amount)
      offset += 4
    end
  end
  buffer_writeu32(b, offset, scores_count)
  offset += 4
  for scores_key, scores_value in value.scores do
    const scores_key_length = #scores_key
    buffer_writeu32(b, offset, scores_key_length)
    buffer_writestring(b, offset + 4, scores_key)
    offset += 4 + scores_key_length
    buffer_writeu8(b, offset, scores_value.primary)
    buffer_writeu8(b, offset + 1, scores_value.secondary)
    offset += 2
  end
  return { buffer = b }
end
const function PlayerState_diff_binary_write(baseline: PlayerState, value: PlayerState, b: buffer)
  local offset = 2
  const health_changed = not Loom.deep_equal(baseline.health, value.health)
  buffer_writebits(b, 0, 1, if health_changed then 1 else 0)
  if health_changed then
    buffer_writeu16(b, offset, value.health)
    offset += 2
  end
  const name_changed = not Loom.deep_equal(baseline.name, value.name)
  buffer_writebits(b, 1, 1, if name_changed then 1 else 0)
  if name_changed then
    const name_value = value.name
    const name_length = #name_value
    buffer_writeu32(b, offset, name_length)
    buffer_writestring(b, offset + 4, name_value)
    offset += 4 + name_length
  end
  const primary_changed = not Loom.deep_equal(baseline.loadout.primary, value.loadout.primary)
  buffer_writebits(b, 2, 1, if primary_changed then 1 else 0)
  if primary_changed then
    buffer_writeu8(b, offset, value.loadout.primary)
    offset += 1
  end
  const secondary_changed = not Loom.deep_equal(baseline.loadout.secondary, value.loadout.secondary)
  buffer_writebits(b, 3, 1, if secondary_changed then 1 else 0)
  if secondary_changed then
    buffer_writeu8(b, offset, value.loadout.secondary)
    offset += 1
  end
  const position_changed = not Loom.deep_equal(baseline.position, value.position)
  buffer_writebits(b, 4, 1, if position_changed then 1 else 0)
  if position_changed then
    const position_value = value.position
    local position_sentinel = 0
    if position_value == Vector3.zero then
      position_sentinel = 1
    elseif position_value == Vector3.one then
      position_sentinel = 2
    elseif position_value == Vector3.xAxis then
      position_sentinel = 3
    elseif position_value == Vector3.yAxis then
      position_sentinel = 4
    elseif position_value == Vector3.zAxis then
      position_sentinel = 5
    end
    buffer_writebits(b, 5, 3, position_sentinel)
    if position_sentinel == 0 then
      buffer_writei16(b, offset, position_value.X)
      buffer_writei16(b, offset + 2, position_value.Y)
      buffer_writei16(b, offset + 4, position_value.Z)
      offset += 6
    end
  end
  const last_event_presence_changed = (baseline.last_event ~= nil) ~= (value.last_event ~= nil)
  buffer_writebits(b, 8, 1, if last_event_presence_changed then 1 else 0)
  if last_event_presence_changed then
    buffer_writebits(b, 9, 1, if value.last_event ~= nil then 1 else 0)
    if value.last_event ~= nil then
      const last_event_value = value.last_event
      local last_event_tag = 0
      if last_event_value.kind == "Miss" then
        last_event_tag = 1
      end
      buffer_writebits(b, 10, 1, last_event_tag)
      if last_event_tag == 0 then
        buffer_writef32(b, offset, value.last_event.amount)
        offset += 4
      end
    end
  else
    if value.last_event ~= nil then
      const last_event_tag_b_value = baseline.last_event
      local last_event_tag_b = 0
      if last_event_tag_b_value.kind == "Miss" then
        last_event_tag_b = 1
      end
      const last_event_tag_c_value = value.last_event
      local last_event_tag_c = 0
      if last_event_tag_c_value.kind == "Miss" then
        last_event_tag_c = 1
      end
      const last_event_tag_changed = last_event_tag_b ~= last_event_tag_c
      buffer_writebits(b, 9, 1, if last_event_tag_changed then 1 else 0)
      if last_event_tag_changed then
        buffer_writebits(b, 10, 1, last_event_tag_c)
        if last_event_tag_c == 0 then
          buffer_writef32(b, offset, value.last_event.amount)
          offset += 4
        end
      else
        if last_event_tag_c == 0 then
          const amount_changed = not Loom.deep_equal(baseline.last_event.amount, value.last_event.amount)
          buffer_writebits(b, 10, 1, if amount_changed then 1 else 0)
          if amount_changed then
            buffer_writef32(b, offset, value.last_event.amount)
            offset += 4
          end
        end
      end
    end
  end
  const recent_count = #value.recent
  const recent_length_changed = #baseline.recent ~= recent_count
  buffer_writebits(b, 11, 1, if recent_length_changed then 1 else 0)
  if recent_length_changed then
    buffer_writeu32(b, offset, recent_count)
    offset += 4
    const recent_bits = offset * 8
    offset += (recent_count + 7) // 8
    for i = 1, recent_count do
      const recent_element = value.recent[i]
      local recent_element_tag = 0
      if recent_element.kind == "Miss" then
        recent_element_tag = 1
      end
      buffer_writebits(b, recent_bits + (i - 1), 1, recent_element_tag)
      if recent_element_tag == 0 then
        buffer_writef32(b, offset, recent_element.amount)
        offset += 4
      end
    end
  else
    const recent_bits_2 = offset * 8
    offset += (recent_count * 2 + 7) // 8
    for i = 1, recent_count do
      const recent_element_baseline = baseline.recent[i]
      const recent_element = value.recent[i]
      local recent_element_tag_b = 0
      if recent_element_baseline.kind == "Miss" then
        recent_element_tag_b = 1
      end
      local recent_element_tag_c = 0
      if recent_element.kind == "Miss" then
        recent_element_tag_c = 1
      end
      const recent_element_tag_changed = recent_element_tag_b ~= recent_element_tag_c
      buffer_writebits(b, recent_bits_2 + (i - 1) * 2, 1, if recent_element_tag_changed then 1 else 0)
      if recent_element_tag_changed then
        buffer_writebits(b, recent_bits_2 + (i - 1) * 2 + 1, 1, recent_element_tag_c)
        if recent_element_tag_c == 0 then
          buffer_writef32(b, offset, recent_element.amount)
          offset += 4
        end
      else
        if recent_element_tag_c == 0 then
          const amount_changed_2 = not Loom.deep_equal(recent_element_baseline.amount, recent_element.amount)
          buffer_writebits(b, recent_bits_2 + (i - 1) * 2 + 1, 1, if amount_changed_2 then 1 else 0)
          if amount_changed_2 then
            buffer_writef32(b, offset, recent_element.amount)
            offset += 4
          end
        end
      end
    end
  end
  const scores_removed = {}
  const scores_added = {}
  const scores_changed = {}
  for scores_current_key, scores_current_entry in value.scores do
    const scores_baseline_entry = baseline.scores[scores_current_key]
    if scores_baseline_entry == nil then
      table.insert(scores_added, scores_current_key)
    else
      if not Loom.deep_equal(scores_baseline_entry, scores_current_entry) then
        table.insert(scores_changed, scores_current_key)
      end
    end
  end
  for scores_baseline_key in baseline.scores do
    if value.scores[scores_baseline_key] == nil then
      table.insert(scores_removed, scores_baseline_key)
    end
  end
  const scores_removed_count = #scores_removed
  buffer_writeu32(b, offset, scores_removed_count)
  offset += 4
  for i = 1, scores_removed_count do
    const scores_key = scores_removed[i]
    const scores_key_length = #scores_key
    buffer_writeu32(b, offset, scores_key_length)
    buffer_writestring(b, offset + 4, scores_key)
    offset += 4 + scores_key_length
  end
  const scores_added_count = #scores_added
  buffer_writeu32(b, offset, scores_added_count)
  offset += 4
  for i = 1, scores_added_count do
    const scores_key = scores_added[i]
    const scores_key_length = #scores_key
    buffer_writeu32(b, offset, scores_key_length)
    buffer_writestring(b, offset + 4, scores_key)
    offset += 4 + scores_key_length
    const scores_entry = value.scores[scores_key]
    buffer_writeu8(b, offset, scores_entry.primary)
    buffer_writeu8(b, offset + 1, scores_entry.secondary)
    offset += 2
  end
  const scores_changed_count = #scores_changed
  buffer_writeu32(b, offset, scores_changed_count)
  offset += 4
  const scores_entry_bits = offset * 8
  offset += (scores_changed_count * 2 + 7) // 8
  for i = 1, scores_changed_count do
    const scores_key = scores_changed[i]
    const scores_key_length = #scores_key
    buffer_writeu32(b, offset, scores_key_length)
    buffer_writestring(b, offset + 4, scores_key)
    offset += 4 + scores_key_length
    const scores_baseline_entry = baseline.scores[scores_key]
    const scores_entry = value.scores[scores_key]
    const primary_changed_2 = not Loom.deep_equal(scores_baseline_entry.primary, scores_entry.primary)
    buffer_writebits(b, scores_entry_bits + (i - 1) * 2, 1, if primary_changed_2 then 1 else 0)
    if primary_changed_2 then
      buffer_writeu8(b, offset, scores_entry.primary)
      offset += 1
    end
    const secondary_changed_2 = not Loom.deep_equal(scores_baseline_entry.secondary, scores_entry.secondary)
    buffer_writebits(b, scores_entry_bits + (i - 1) * 2 + 1, 1, if secondary_changed_2 then 1 else 0)
    if secondary_changed_2 then
      buffer_writeu8(b, offset, scores_entry.secondary)
      offset += 1
    end
  end
  return offset
end
const function PlayerState_diff_binary_attempt(baseline: PlayerState, value: PlayerState, size: number)
  const scratch = buffer_create(size)
  const ok, written = pcall(PlayerState_diff_binary_write, baseline, value, scratch)
  if ok then
    return scratch, written
  end
  return PlayerState_diff_binary_attempt(baseline, value, size * 2)
end
const function PlayerState_diff_binary(baseline: PlayerState, value: PlayerState): Loom.Serialized
  const baseline_s = PlayerState_serialize_binary(baseline)
  const value_s = PlayerState_serialize_binary(value)
  const size = (if baseline_s.buffer ~= nil then buffer.len(baseline_s.buffer) else 0) + (if value_s.buffer ~= nil then buffer.len(value_s.buffer) else 0) + 2
  const scratch, written = PlayerState_diff_binary_attempt(baseline, value, size)
  const result = buffer_create(written)
  buffer_copy(result, 0, scratch, 0, written)
  return { buffer = result }
end
const function PlayerState_apply_diff_binary_read(baseline: PlayerState, diff: Loom.Serialized): Loom.Result<PlayerState, Loom.DeserializeError>
  const b = diff.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 2 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  local offset = 2
  const health_changed = buffer_readbits(b, 0, 1) == 1
  local health = nil
  if health_changed then
    const health_2 = buffer_readu16(b, offset)
    health = health_2
    offset += 2
  else
    health = baseline.health
  end
  const name_changed = buffer_readbits(b, 1, 1) == 1
  local name = nil
  if name_changed then
    const name_2_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + name_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "name" } }
    end
    const name_2 = buffer_readstring(b, offset + 4, name_2_length)
    offset += 4 + name_2_length
    name = name_2
  else
    name = baseline.name
  end
  const primary_changed = buffer_readbits(b, 2, 1) == 1
  local primary = nil
  if primary_changed then
    const primary_2 = buffer_readu8(b, offset)
    primary = primary_2
    offset += 1
  else
    primary = baseline.loadout.primary
  end
  const secondary_changed = buffer_readbits(b, 3, 1) == 1
  local secondary = nil
  if secondary_changed then
    const secondary_2 = buffer_readu8(b, offset)
    secondary = secondary_2
    offset += 1
  else
    secondary = baseline.loadout.secondary
  end
  const position_changed = buffer_readbits(b, 4, 1) == 1
  local position = nil
  if position_changed then
    const position_2_sentinel = buffer_readbits(b, 5, 3)
    local position_2 = nil
    if position_2_sentinel == 0 then
      if b_len < offset + 6 then
        return { ok = false, error = { kind = "truncated", field = "position" } }
      end
      const position_x = buffer_readi16(b, offset)
      const position_y = buffer_readi16(b, offset + 2)
      const position_z = buffer_readi16(b, offset + 4)
      position_2 = Vector3.new(position_x, position_y, position_z)
      offset += 6
    elseif position_2_sentinel == 1 then
      position_2 = Vector3.zero
    elseif position_2_sentinel == 2 then
      position_2 = Vector3.one
    elseif position_2_sentinel == 3 then
      position_2 = Vector3.xAxis
    elseif position_2_sentinel == 4 then
      position_2 = Vector3.yAxis
    elseif position_2_sentinel == 5 then
      position_2 = Vector3.zAxis
    else
      return { ok = false, error = { kind = "invalid_tag", field = "position" } }
    end
    position = position_2
  else
    position = baseline.position
  end
  const last_event_presence_changed = buffer_readbits(b, 8, 1) == 1
  local last_event = nil
  if last_event_presence_changed then
    const last_event_2_present = buffer_readbits(b, 9, 1) == 1
    local last_event_2 = nil
    if last_event_2_present then
      const last_event_3_tag = buffer_readbits(b, 10, 1)
      local last_event_3 = nil
      if last_event_3_tag == 0 then
        if b_len < offset + 4 then
          return { ok = false, error = { kind = "truncated", field = "last_event" } }
        end
        const amount = buffer_readf32(b, offset)
        last_event_3 = { kind = "Hit", amount = amount }
        offset += 4
      elseif last_event_3_tag == 1 then
        last_event_3 = { kind = "Miss" }
      else
        return { ok = false, error = { kind = "invalid_tag", field = "last_event" } }
      end
      last_event_2 = last_event_3
    end
    last_event = last_event_2
  else
    if baseline.last_event ~= nil then
      const last_event_tag_changed = buffer_readbits(b, 9, 1) == 1
      local last_event_4 = nil
      if last_event_tag_changed then
        const last_event_5_tag = buffer_readbits(b, 10, 1)
        local last_event_5 = nil
        if last_event_5_tag == 0 then
          if b_len < offset + 4 then
            return { ok = false, error = { kind = "truncated", field = "last_event" } }
          end
          const amount_2 = buffer_readf32(b, offset)
          last_event_5 = { kind = "Hit", amount = amount_2 }
          offset += 4
        elseif last_event_5_tag == 1 then
          last_event_5 = { kind = "Miss" }
        else
          return { ok = false, error = { kind = "invalid_tag", field = "last_event" } }
        end
        last_event_4 = last_event_5
      else
        const last_event_tag_b_value = baseline.last_event
        local last_event_tag_b = 0
        if last_event_tag_b_value.kind == "Miss" then
          last_event_tag_b = 1
        end
        if last_event_tag_b == 0 then
          const amount_changed = buffer_readbits(b, 10, 1) == 1
          local amount_3 = nil
          if amount_changed then
            const amount_4 = buffer_readf32(b, offset)
            amount_3 = amount_4
            offset += 4
          else
            amount_3 = baseline.last_event.amount
          end
          last_event_4 = { kind = "Hit", amount = amount_3 }
        elseif last_event_tag_b == 1 then
          last_event_4 = { kind = "Miss" }
        end
      end
      last_event = last_event_4
    end
  end
  const recent_length_changed = buffer_readbits(b, 11, 1) == 1
  local recent = nil
  if recent_length_changed then
    const recent_2_count = buffer_readu32(b, offset)
    offset += 4
    const recent_2_bits = offset * 8
    offset += (recent_2_count + 7) // 8
    const recent_2 = {}
    for i = 1, recent_2_count do
      const recent_element_tag = buffer_readbits(b, recent_2_bits + (i - 1), 1)
      local recent_element = nil
      if recent_element_tag == 0 then
        if b_len < offset + 4 then
          return { ok = false, error = { kind = "truncated", field = "recent[]" } }
        end
        const amount_5 = buffer_readf32(b, offset)
        recent_element = { kind = "Hit", amount = amount_5 }
        offset += 4
      elseif recent_element_tag == 1 then
        recent_element = { kind = "Miss" }
      else
        return { ok = false, error = { kind = "invalid_tag", field = "recent[]" } }
      end
      recent_2[i] = recent_element
    end
    recent = recent_2
  else
    const recent_count = #baseline.recent
    const recent_bits = offset * 8
    offset += (recent_count * 2 + 7) // 8
    recent = {}
    for i = 1, recent_count do
      const recent_element_tag_changed = buffer_readbits(b, recent_bits + (i - 1) * 2, 1) == 1
      local recent_element = nil
      if recent_element_tag_changed then
        const recent_element_2_tag = buffer_readbits(b, recent_bits + (i - 1) * 2 + 1, 1)
        local recent_element_2 = nil
        if recent_element_2_tag == 0 then
          if b_len < offset + 4 then
            return { ok = false, error = { kind = "truncated", field = "recent[]" } }
          end
          const amount_5 = buffer_readf32(b, offset)
          recent_element_2 = { kind = "Hit", amount = amount_5 }
          offset += 4
        elseif recent_element_2_tag == 1 then
          recent_element_2 = { kind = "Miss" }
        else
          return { ok = false, error = { kind = "invalid_tag", field = "recent[]" } }
        end
        recent_element = recent_element_2
      else
        const recent_element_tag_b_value = baseline.recent[i]
        local recent_element_tag_b = 0
        if recent_element_tag_b_value.kind == "Miss" then
          recent_element_tag_b = 1
        end
        if recent_element_tag_b == 0 then
          const amount_changed_2 = buffer_readbits(b, recent_bits + (i - 1) * 2 + 1, 1) == 1
          local amount_6 = nil
          if amount_changed_2 then
            const amount_7 = buffer_readf32(b, offset)
            amount_6 = amount_7
            offset += 4
          else
            amount_6 = baseline.recent[i].amount
          end
          recent_element = { kind = "Hit", amount = amount_6 }
        elseif recent_element_tag_b == 1 then
          recent_element = { kind = "Miss" }
        end
      end
      recent[i] = recent_element
    end
  end
  const scores = {}
  for scores_copy_key, scores_copy_value in baseline.scores do
    scores[scores_copy_key] = scores_copy_value
  end
  const scores_removed_count = buffer_readu32(b, offset)
  offset += 4
  for i_2 = 1, scores_removed_count do
    const scores_key_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + scores_key_length then
      return { ok = false, error = { kind = "invalid_length", field = "scores[k]" } }
    end
    const scores_key = buffer_readstring(b, offset + 4, scores_key_length)
    offset += 4 + scores_key_length
    const scores_removed_key = scores_key
    scores[scores_removed_key] = nil
  end
  const scores_added_count = buffer_readu32(b, offset)
  offset += 4
  for i_3 = 1, scores_added_count do
    const scores_key_2_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + scores_key_2_length then
      return { ok = false, error = { kind = "invalid_length", field = "scores[k]" } }
    end
    const scores_key_2 = buffer_readstring(b, offset + 4, scores_key_2_length)
    offset += 4 + scores_key_2_length
    const scores_added_key = scores_key_2
    const primary_3 = buffer_readu8(b, offset)
    const secondary_3 = buffer_readu8(b, offset + 1)
    scores[scores_added_key] = { primary = primary_3, secondary = secondary_3 }
    offset += 2
  end
  const scores_changed_count = buffer_readu32(b, offset)
  offset += 4
  const scores_entry_bits = offset * 8
  offset += (scores_changed_count * 2 + 7) // 8
  for i_4 = 1, scores_changed_count do
    const scores_key_3_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + scores_key_3_length then
      return { ok = false, error = { kind = "invalid_length", field = "scores[k]" } }
    end
    const scores_key_3 = buffer_readstring(b, offset + 4, scores_key_3_length)
    offset += 4 + scores_key_3_length
    const scores_changed_key = scores_key_3
    const primary_changed_2 = buffer_readbits(b, scores_entry_bits + (i_4 - 1) * 2, 1) == 1
    local primary_4 = nil
    if primary_changed_2 then
      const primary_5 = buffer_readu8(b, offset)
      primary_4 = primary_5
      offset += 1
    else
      primary_4 = baseline.scores[scores_changed_key].primary
    end
    const secondary_changed_2 = buffer_readbits(b, scores_entry_bits + (i_4 - 1) * 2 + 1, 1) == 1
    local secondary_4 = nil
    if secondary_changed_2 then
      const secondary_5 = buffer_readu8(b, offset)
      secondary_4 = secondary_5
      offset += 1
    else
      secondary_4 = baseline.scores[scores_changed_key].secondary
    end
    scores[scores_changed_key] = { primary = primary_4, secondary = secondary_4 }
  end
  return { ok = true, value = {
    health = health,
    name = name,
    loadout = { primary = primary, secondary = secondary },
    position = position,
    last_event = last_event,
    recent = recent,
    scores = scores,
  } }
end
const function PlayerState_apply_diff_binary(baseline: PlayerState, diff: Loom.Serialized): Loom.Result<PlayerState, Loom.DeserializeError>
  const ok, result = pcall(PlayerState_apply_diff_binary_read, baseline, diff)
  if ok then
    return result
  end
  return { ok = false, error = { kind = "truncated" } }
end
-- Exercises every branch of the delta write/read: a leaf, a flattened struct, a packed sentinelled
-- datatype, an optional wrapping a union, an array of unions and a map whose values are structs.
type Loadout = {
  read primary: number,
  read secondary: number,
}
type Hit = {
  read kind: "Hit",
  read amount: number,
}
type Miss = {
  read kind: "Miss",
}
type PlayerState = {
  read health: number,
  read name: string,
  read loadout: Loadout,
  read position: Vector3,
  read last_event: (Hit | Miss)?,
  read recent: { (Hit | Miss) },
  read scores: Record<string, Loadout>,
}
const function _trigger_usage(): ()
  const diff = PlayerState_diff_binary((nil :: never) :: PlayerState, (nil :: never) :: PlayerState)
  const applied = PlayerState_apply_diff_binary((nil :: never) :: PlayerState, diff)
end
return nil
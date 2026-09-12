const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu8 = buffer.writeu8
const buffer_readu8 = buffer.readu8
const buffer_len = buffer.len
const buffer_writebits = buffer.writebits
const buffer_writeu32 = buffer.writeu32
const buffer_writestring = buffer.writestring
const buffer_writef32 = buffer.writef32
const buffer_readbits = buffer.readbits
const buffer_readu32 = buffer.readu32
const buffer_readstring = buffer.readstring
const buffer_readf32 = buffer.readf32
const function Leaf_serialize_binary(value: Leaf): Loom.Serialized
  const b = buffer_create(1)
  buffer_writeu8(b, 0, value.n)
  return { buffer = b }
end
const function Leaf_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Leaf, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { n = buffer_readu8(b, 0) } }
end
const function Compositions_serialize_binary(value: Compositions): Loom.Serialized
  local size = 1 + (if value.maybe_leaf ~= nil then 1 else 0)
  if value.maybe_map ~= nil then
    size += 4
    local maybe_map_count = 0
    for maybe_map_key, maybe_map_value in value.maybe_map do
      maybe_map_count += 1
      size += 4 + #maybe_map_key
      size += 4
    end
  end
  size += 4
  const map_list_count = #value.map_list
  for i = 1, map_list_count do
    const map_list_element = value.map_list[i]
    size += 4
    local map_list_element_count = 0
    for map_list_element_key, map_list_element_value in map_list_element do
      map_list_element_count += 1
      size += 4 + #map_list_element_key
      size += 1
    end
  end
  size += 4
  local sparse_count = 0
  for sparse_key, sparse_value in value.sparse do
    sparse_count += 1
    size += 4 + #sparse_key
    if sparse_value ~= nil then
      size += 4 + #sparse_value
    end
  end
  size += (sparse_count + 7) // 8
  if value.maybe_names ~= nil then
    size += 4
    const maybe_names_count = #value.maybe_names
    for i = 1, maybe_names_count do
      const maybe_names_element = value.maybe_names[i]
      size += 4 + #maybe_names_element
    end
  end
  const b = buffer_create(size)
  buffer_writebits(b, 0, 1, if value.maybe_map ~= nil then 1 else 0)
  local offset = 1
  if value.maybe_map ~= nil then
    local maybe_map_count_2 = 0
    for maybe_map_key in value.maybe_map do
      maybe_map_count_2 += 1
    end
    buffer_writeu32(b, offset, maybe_map_count_2)
    offset += 4
    for maybe_map_key, maybe_map_value in value.maybe_map do
      const maybe_map_key_length = #maybe_map_key
      buffer_writeu32(b, offset, maybe_map_key_length)
      buffer_writestring(b, offset + 4, maybe_map_key)
      offset += 4 + maybe_map_key_length
      buffer_writef32(b, offset, maybe_map_value)
      offset += 4
    end
  end
  buffer_writeu32(b, offset, map_list_count)
  offset += 4
  for i = 1, map_list_count do
    const map_list_element = value.map_list[i]
    local map_list_element_count = 0
    for map_list_element_key in map_list_element do
      map_list_element_count += 1
    end
    buffer_writeu32(b, offset, map_list_element_count)
    offset += 4
    for map_list_element_key, map_list_element_value in map_list_element do
      const map_list_element_key_length = #map_list_element_key
      buffer_writeu32(b, offset, map_list_element_key_length)
      buffer_writestring(b, offset + 4, map_list_element_key)
      offset += 4 + map_list_element_key_length
      buffer_writeu8(b, offset, map_list_element_value.n)
      offset += 1
    end
  end
  buffer_writeu32(b, offset, sparse_count)
  offset += 4
  const sparse_bits = offset * 8
  offset += (sparse_count + 7) // 8
  local sparse_index = 1
  for sparse_key, sparse_value in value.sparse do
    const sparse_key_length = #sparse_key
    buffer_writeu32(b, offset, sparse_key_length)
    buffer_writestring(b, offset + 4, sparse_key)
    offset += 4 + sparse_key_length
    buffer_writebits(b, sparse_bits + (sparse_index - 1), 1, if sparse_value ~= nil then 1 else 0)
    if sparse_value ~= nil then
      const sparse_value_length = #sparse_value
      buffer_writeu32(b, offset, sparse_value_length)
      buffer_writestring(b, offset + 4, sparse_value)
      offset += 4 + sparse_value_length
    end
    sparse_index += 1
  end
  buffer_writebits(b, 1, 1, if value.maybe_leaf ~= nil then 1 else 0)
  if value.maybe_leaf ~= nil then
    buffer_writeu8(b, offset, value.maybe_leaf.n)
    offset += 1
  end
  buffer_writebits(b, 2, 1, if value.maybe_names ~= nil then 1 else 0)
  if value.maybe_names ~= nil then
    const maybe_names_count_2 = #value.maybe_names
    buffer_writeu32(b, offset, maybe_names_count_2)
    offset += 4
    for i = 1, maybe_names_count_2 do
      const maybe_names_element = value.maybe_names[i]
      const maybe_names_element_length = #maybe_names_element
      buffer_writeu32(b, offset, maybe_names_element_length)
      buffer_writestring(b, offset + 4, maybe_names_element)
      offset += 4 + maybe_names_element_length
    end
  end
  return { buffer = b }
end
const function Compositions_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Compositions, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 9 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const maybe_map_present = buffer_readbits(b, 0, 1) == 1
  local maybe_map = nil
  local offset = 1
  if maybe_map_present then
    const maybe_map_2_count = buffer_readu32(b, offset)
    const maybe_map_2 = {}
    offset += 4
    for i = 1, maybe_map_2_count do
      const maybe_map_key_length = buffer_readu32(b, offset)
      if b_len < offset + 4 + maybe_map_key_length then
        return { ok = false, error = { kind = "invalid_length", field = "maybe_map[k]" } }
      end
      const maybe_map_key = buffer_readstring(b, offset + 4, maybe_map_key_length)
      offset += 4 + maybe_map_key_length
      const maybe_map_value = buffer_readf32(b, offset)
      offset += 4
      maybe_map_2[maybe_map_key] = maybe_map_value
    end
    maybe_map = maybe_map_2
  end
  const map_list_count = buffer_readu32(b, offset)
  const map_list = {}
  offset += 4
  for i = 1, map_list_count do
    const map_list_element_count = buffer_readu32(b, offset)
    const map_list_element = {}
    offset += 4
    for i_2 = 1, map_list_element_count do
      const map_list_element_key_length = buffer_readu32(b, offset)
      if b_len < offset + 4 + map_list_element_key_length then
        return { ok = false, error = { kind = "invalid_length", field = "map_list[][k]" } }
      end
      const map_list_element_key = buffer_readstring(b, offset + 4, map_list_element_key_length)
      offset += 4 + map_list_element_key_length
      const n = buffer_readu8(b, offset)
      offset += 1
      map_list_element[map_list_element_key] = { n = n }
    end
    map_list[i] = map_list_element
  end
  const sparse_count = buffer_readu32(b, offset)
  offset += 4
  const sparse_bits = offset * 8
  offset += (sparse_count + 7) // 8
  const sparse = {}
  for i = 1, sparse_count do
    const sparse_key_length = buffer_readu32(b, offset)
    if b_len < offset + 4 + sparse_key_length then
      return { ok = false, error = { kind = "invalid_length", field = "sparse[k]" } }
    end
    const sparse_key = buffer_readstring(b, offset + 4, sparse_key_length)
    offset += 4 + sparse_key_length
    const sparse_value_present = buffer_readbits(b, sparse_bits + (i - 1), 1) == 1
    local sparse_value = nil
    if sparse_value_present then
      const sparse_value_2_length = buffer_readu32(b, offset)
      if b_len < offset + 4 + sparse_value_2_length then
        return { ok = false, error = { kind = "invalid_length", field = "sparse[v]" } }
      end
      const sparse_value_2 = buffer_readstring(b, offset + 4, sparse_value_2_length)
      offset += 4 + sparse_value_2_length
      sparse_value = sparse_value_2
    end
    sparse[sparse_key] = sparse_value
  end
  const maybe_leaf_present = buffer_readbits(b, 1, 1) == 1
  local maybe_leaf = nil
  if maybe_leaf_present then
    if b_len < offset + 1 then
      return { ok = false, error = { kind = "truncated", field = "maybe_leaf" } }
    end
    const n = buffer_readu8(b, offset)
    maybe_leaf = { n = n }
    offset += 1
  end
  const maybe_names_present = buffer_readbits(b, 2, 1) == 1
  local maybe_names = nil
  if maybe_names_present then
    const maybe_names_2_count = buffer_readu32(b, offset)
    const maybe_names_2 = {}
    offset += 4
    for i = 1, maybe_names_2_count do
      const maybe_names_element_length = buffer_readu32(b, offset)
      if b_len < offset + 4 + maybe_names_element_length then
        return { ok = false, error = { kind = "invalid_length", field = "maybe_names[]" } }
      end
      const maybe_names_element = buffer_readstring(b, offset + 4, maybe_names_element_length)
      offset += 4 + maybe_names_element_length
      maybe_names_2[i] = maybe_names_element
    end
    maybe_names = maybe_names_2
  end
  return { ok = true, value = { maybe_map = maybe_map, map_list = map_list, sparse = sparse, maybe_leaf = maybe_leaf, maybe_names = maybe_names } }
end
type Leaf = {
  read n: number,
}
type Compositions = {
  read maybe_map: Record<string, number>?,
  read map_list: { Record<string, Leaf> },
  read sparse: Record<string, string?>,
  read maybe_leaf: Leaf?,
  read maybe_names: { string }?,
}
const function _trigger_usage(): ()
  const leaf_payload = Leaf_serialize_binary((nil :: never) :: Leaf)
  const leaf_restored = Leaf_deserialize_binary(leaf_payload)
  const compositions_payload = Compositions_serialize_binary((nil :: never) :: Compositions)
  const compositions_restored = Compositions_deserialize_binary(compositions_payload)
end
return nil
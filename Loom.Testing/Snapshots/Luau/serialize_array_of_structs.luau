const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const buffer_create = buffer.create
const buffer_writeu32 = buffer.writeu32
const buffer_readu32 = buffer.readu32
const buffer_len = buffer.len
const buffer_writeu8 = buffer.writeu8
const buffer_readu8 = buffer.readu8
const function Bag_serialize_binary(value: Bag): Loom.Serialized
  const blobs = {}
  const b = buffer_create(4)
  const owners_count = #value.owners
  buffer_writeu32(b, 0, owners_count)
  for i = 1, owners_count do
    const owners_element = value.owners[i]
    table.insert(blobs, owners_element)
  end
  return { buffer = b, blobs = blobs }
end
const function Bag_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Bag, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const blobs = serialized.blobs
  if blobs == nil then
    return { ok = false, error = { kind = "missing_blob" } }
  end
  local blob_index = 1
  const owners_count = buffer_readu32(b, 0)
  const owners = {}
  for i = 1, owners_count do
    const owners_element_blob = blobs[blob_index]
    blob_index += 1
    if owners_element_blob == nil then
      return { ok = false, error = { kind = "missing_blob", field = "owners[]" } }
    end
    if typeof(owners_element_blob) ~= "Instance" or not owners_element_blob:IsA("Part") then
      return { ok = false, error = { kind = "invalid_blob", field = "owners[]" } }
    end
    owners[i] = owners_element_blob
  end
  return { ok = true, value = { owners = owners } }
end
const function Leaf_serialize_binary(value: Leaf): Loom.Serialized
  const b = buffer_create(1)
  buffer_writeu8(b, 0, value.v)
  return { buffer = b }
end
const function Leaf_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Leaf, Loom.DeserializeError>
  const b = serialized.buffer
  if b == nil or buffer_len(b) < 1 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  return { ok = true, value = { v = buffer_readu8(b, 0) } }
end
const function Branch_serialize_binary(value: Branch): Loom.Serialized
  const b = buffer_create(4 + #value.leaves)
  const leaves_count = #value.leaves
  buffer_writeu32(b, 0, leaves_count)
  local offset = 4
  for i = 1, leaves_count do
    const leaves_element = value.leaves[i]
    buffer_writeu8(b, offset, leaves_element.v)
    offset += 1
  end
  return { buffer = b }
end
const function Branch_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Branch, Loom.DeserializeError>
  const b = serialized.buffer
  const b_len = if b ~= nil then buffer_len(b) else 0
  if b == nil or b_len < 4 then
    return { ok = false, error = { kind = "truncated", offset = 0 } }
  end
  const leaves_count = buffer_readu32(b, 0)
  local offset = 4
  if b_len < offset + leaves_count then
    return { ok = false, error = { kind = "invalid_length", field = "leaves" } }
  end
  const leaves = {}
  for i = 1, leaves_count do
    const v = buffer_readu8(b, offset)
    offset += 1
    leaves[i] = { v = v }
  end
  return { ok = true, value = { leaves = leaves } }
end
type Bag = {
  read owners: { Part },
}
type Leaf = {
  read v: number,
}
type Branch = {
  read leaves: { Leaf },
}
const function _trigger_usage(): ()
  const bag_payload = Bag_serialize_binary((nil :: never) :: Bag)
  const bag_restored = Bag_deserialize_binary(bag_payload)
  const leaf_payload = Leaf_serialize_binary((nil :: never) :: Leaf)
  const leaf_restored = Leaf_deserialize_binary(leaf_payload)
  const branch_payload = Branch_serialize_binary((nil :: never) :: Branch)
  const branch_restored = Branch_deserialize_binary(branch_payload)
end
return nil
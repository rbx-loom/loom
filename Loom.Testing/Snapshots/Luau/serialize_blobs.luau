const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const function Marker_serialize_binary(value: Marker): Loom.Serialized
  const blobs = {}
  table.insert(blobs, value.target)
  return { blobs = blobs }
end
const function Marker_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Marker, Loom.DeserializeError>
  const blobs = serialized.blobs
  if blobs == nil then
    return { ok = false, error = { kind = "missing_blob" } }
  end
  local blob_index = 1
  const target_blob = blobs[blob_index]
  blob_index += 1
  if target_blob == nil then
    return { ok = false, error = { kind = "missing_blob", field = "target" } }
  end
  if typeof(target_blob) ~= "Instance" or not target_blob:IsA("Part") then
    return { ok = false, error = { kind = "invalid_blob", field = "target" } }
  end
  return { ok = true, value = { kind = "spawn", target = target_blob } }
end
type Marker = {
  read kind: "spawn",
  read target: Part,
}
const function _trigger_usage(): ()
  const payload = Marker_serialize_binary((nil :: never) :: Marker)
  const restored = Marker_deserialize_binary(payload)
end
return nil
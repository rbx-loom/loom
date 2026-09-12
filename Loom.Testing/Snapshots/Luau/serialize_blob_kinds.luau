const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const function Payload_serialize_binary(value: Payload): Loom.Serialized
  const blobs = {}
  table.insert(blobs, value.origin)
  table.insert(blobs, value.handler)
  table.insert(blobs, value.extra)
  table.insert(blobs, value.target)
  return { blobs = blobs }
end
const function Payload_deserialize_binary(serialized: Loom.Serialized): Loom.Result<Payload, Loom.DeserializeError>
  const blobs = serialized.blobs
  if blobs == nil then
    return { ok = false, error = { kind = "missing_blob" } }
  end
  local blob_index = 1
  const origin_blob = blobs[blob_index]
  blob_index += 1
  if origin_blob == nil then
    return { ok = false, error = { kind = "missing_blob", field = "origin" } }
  end
  if typeof(origin_blob) ~= "Instance" or not origin_blob:IsA("Part") then
    return { ok = false, error = { kind = "invalid_blob", field = "origin" } }
  end
  const handler_blob = blobs[blob_index]
  blob_index += 1
  if handler_blob == nil then
    return { ok = false, error = { kind = "missing_blob", field = "handler" } }
  end
  if typeof(handler_blob) ~= "function" then
    return { ok = false, error = { kind = "invalid_blob", field = "handler" } }
  end
  const extra_blob = blobs[blob_index]
  blob_index += 1
  if extra_blob == nil then
    return { ok = false, error = { kind = "missing_blob", field = "extra" } }
  end
  const target_blob = blobs[blob_index]
  blob_index += 1
  if target_blob == nil then
    return { ok = false, error = { kind = "missing_blob", field = "target" } }
  end
  if typeof(target_blob) ~= "Instance" then
    return { ok = false, error = { kind = "invalid_blob", field = "target" } }
  end
  return { ok = true, value = { origin = origin_blob, handler = handler_blob, extra = extra_blob, target = target_blob } }
end
type Payload = {
  read origin: Part,
  read handler: (number) -> (),
  read extra: unknown,
  read target: Instance,
}
const function _trigger_usage(): ()
  const payload = Payload_serialize_binary((nil :: never) :: Payload)
  const restored = Payload_deserialize_binary(payload)
end
return nil
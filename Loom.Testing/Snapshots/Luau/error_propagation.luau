const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const function some_other_unsafe_fn(): Loom.Result<number, string>
  return { ok = true, value = 1 }
end
const function unsafe_fn(): Loom.Result<number, string>
  const _result = some_other_unsafe_fn()
  if not _result.ok then
    return _result
  end
  const value = _result.value
  return { ok = true, value = 69 + value }
end
const function stored_unsafe_fn(): Loom.Result<number, string>
  const result: Loom.Result<number, string> = some_other_unsafe_fn()
  if not result.ok then
    return result
  end
  const value = result.value
  return { ok = true, value = 69 + value }
end
return nil
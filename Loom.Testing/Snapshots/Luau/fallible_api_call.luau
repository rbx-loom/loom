const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const data_store_service = game:GetService("DataStoreService")
const function load(key: string): Loom.Result<unknown, Loom.RobloxError>
  const _ok, _value = xpcall(data_store_service.GetDataStore, Loom.roblox_error, data_store_service, "players")
  if not _ok then
    return { ok = false, error = _value }
  end
  const store = _value
  const _ok_1, _value_1 = xpcall(store.GetAsync, Loom.roblox_error, store, key)
  if not _ok_1 then
    return { ok = false, error = _value_1 }
  end
  const value = _value_1
  return { ok = true, value = value }
end
const outcome = Loom.future(load, "player_1")
print(outcome.status)
return nil
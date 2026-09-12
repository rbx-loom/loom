const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type Entry<K, V> = {
  read key: K,
  read value: V,
}
const function find<K, V>(entries: { Entry<K, V> }, key: K): Loom.Result<V, string>
  for _, entry in entries do
    if entry.key == key then
      return { ok = true, value = entry.value }
    end
  end
  return { ok = false, error = "missing key" }
end
const entries = {{ key = "pi", value = 3.14159 }, { key = "e", value = 2.71828 }}
const result = find(entries, "pi")
print(if result.ok then result.value else result.error)
return nil
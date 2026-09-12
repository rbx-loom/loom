type WithIndexer = { read [string]: number } & GetValue<string, number>
type GetValue<K, V> = {
  get_value: (GetValue<K, V>, K) -> V,
}
local GetValue_string_number_for_WithIndexer = {}
GetValue_string_number_for_WithIndexer.__index = GetValue_string_number_for_WithIndexer
GetValue_string_number_for_WithIndexer = GetValue_string_number_for_WithIndexer :: WithIndexer
function GetValue_string_number_for_WithIndexer.get_value(self: WithIndexer, key)
  return self[key]
end
return nil
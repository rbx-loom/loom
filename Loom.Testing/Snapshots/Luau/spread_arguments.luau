const function sum(...: number): number
  local ns = {...}
  local total = 0
  for _, n in ns do
    total += n
  end
  return total
end
const xs = {1, 2, 3}
const alone = sum(table.unpack(xs))
const after_fixed = sum(10, 20, table.unpack(xs))
const _result = table.clone(xs)
local _count = #_result
_count += 1
_result[_count] = 100
const before_more = sum(table.unpack(_result))
const _result_1 = table.clone(xs)
local _count_1 = #_result_1
const _length = #xs
table.move(xs, 1, _length, _count_1 + 1, _result_1)
_count_1 += _length
const two_spreads = sum(table.unpack(_result_1))
const function labelled(label: string, ...: number): number
  local ns = {...}
  return #ns
end
const counted = labelled("hi", table.unpack(xs))
const function count_of<T>(...: T): number
  local items = {...}
  return #items
end
const generic = count_of(table.unpack(xs))
const function forward(...: number): number
  local args = {...}
  return count_of(table.unpack(args))
end
const forwarded = forward(table.unpack(xs))
const _result_2 = {}
for _, _element in xs do
  _result_2[_element] = true
end
const members = _result_2
const _result_3 = { [9] = true }
for _, _element_1 in xs do
  _result_3[_element_1] = true
end
const mixed_members = _result_3
return nil
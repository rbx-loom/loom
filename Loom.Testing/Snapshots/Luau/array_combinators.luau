const numbers = {1, 2, 3, 4, 5}
const doubled = table.create(#numbers)
for _index, n in numbers do
  doubled[_index] = n * 2
end
const weighted = table.create(#numbers)
for i, n in numbers do
  weighted[i] = n * i
end
const big = table.create(#numbers)
local _count = 0
for _, n in numbers do
  if n > 2 then
    _count += 1
    big[_count] = n
  end
end
local total = 0
for _, n in numbers do
  const sum = total
  total = sum + n
end
local chained = 0
for _, n in numbers do
  const n = n * 2
  if n > 4 then
    const sum = chained
    chained = sum + n
  end
end
const function triple(n: number): number
  return n * 3
end
const tripled = table.create(#numbers)
for _index_1, _element in numbers do
  tripled[_index_1] = triple(_element, _index_1)
end
local has_big = false
for _, n in numbers do
  if n > 4 then
    has_big = true
    break
  end
end
local all_positive = true
for _, n in numbers do
  if n > 0 then continue end
  all_positive = false
  break
end
local big_count = 0
for _, n in numbers do
  if n > 2 then
    big_count += 1
  end
end
const paired = {}
local _count_1 = 0
for _, n in numbers do
  const _segment = {n, n * 10}
  const _length = #_segment
  table.move(_segment, 1, _length, _count_1 + 1, paired)
  _count_1 += _length
end
const rows = {{1, 2}, {3}}
const flat = {}
local _count_2 = 0
for _, _segment_1 in rows do
  const _length_1 = #_segment_1
  table.move(_segment_1, 1, _length_1, _count_2 + 1, flat)
  _count_2 += _length_1
end
const flat_big = {}
local _count_3 = 0
for _, _segment_2 in rows do
  for _, n in _segment_2 do
    if n > 1 then
      _count_3 += 1
      flat_big[_count_3] = n
    end
  end
end
const flat_indexed = {}
local _count_4 = 0
for _, _segment_3 in rows do
  for _, n in _segment_3 do
    _count_4 += 1
    const i = _count_4
    flat_indexed[_count_4] = n * i
  end
end
local spread_total = 0
for _, r in rows do
  for _, n in r do
    if n > 1 then
      const a = spread_total
      spread_total = a + n
    end
  end
end
const _result = {}
local _count_5 = 0
for _, _segment_4 in rows do
  const _length_2 = #_segment_4
  table.move(_segment_4, 1, _length_2, _count_5 + 1, _result)
  _count_5 += _length_2
end
local flat_any = false
for _, n in _result do
  if n > 2 then
    flat_any = true
    break
  end
end
local kept_count = 0
for _, n in numbers do
  if n > 2 then
    kept_count += 1
  end
end
local flat_size = 0
for _, _segment_5 in rows do
  flat_size += #_segment_5
end
const _result_1 = table.create(#numbers)
for _index_2, n in numbers do
  _result_1[_index_2] = n * 2
end
const mapped_size = #_result_1
return nil
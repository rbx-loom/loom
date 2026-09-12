const letters = { ["a"] = true, ["b"] = true, ["c"] = true }
const vowels = { ["a"] = true, ["e"] = true }
local _count = 0
for _key in letters do
  _count += 1
end
print(_count, next(letters) == nil, letters["b"] == true)
const _result = table.clone(letters)
for _key_1 in vowels do
  _result[_key_1] = true
end
const _result_1 = {}
local _count_1 = 0
for _key_2 in _result do
  _count_1 += 1
  _result_1[_count_1] = _key_2
end
print(_result_1)
const _result_2 = {}
for _key_3 in letters do
  if vowels[_key_3] == true then
    _result_2[_key_3] = true
  end
end
const _result_3 = {}
for _key_4 in letters do
  if vowels[_key_4] ~= true then
    _result_3[_key_4] = true
  end
end
print(_result_2, _result_3)
local _subset = true
for _key_5 in vowels do
  if letters[_key_5] == true then continue end
  _subset = false
  break
end
print(_subset)
local seen = { [1] = true, [2] = true }
seen[3] = true
seen[1] = nil
local _count_2 = 0
for _key_6 in seen do
  _count_2 += 1
end
const _result_4 = {}
for _, _element in {4, 4, 5} do
  _result_4[_element] = true
end
local _count_3 = 0
for _key_7 in _result_4 do
  _count_3 += 1
end
print(_count_2, _count_3)
return nil
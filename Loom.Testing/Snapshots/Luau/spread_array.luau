const rest = {2, 3, 4}
const _result = {1}
local _count = 1
const _length = #rest
table.move(rest, 1, _length, _count + 1, _result)
_count += _length
const prepended = _result
const _result_1 = table.clone(rest)
local _count_1 = #_result_1
_count_1 += 1
_result_1[_count_1] = 5
const appended = _result_1
const _result_2 = {0}
local _count_2 = 1
const _length_1 = #rest
table.move(rest, 1, _length_1, _count_2 + 1, _result_2)
_count_2 += _length_1
_count_2 += 1
_result_2[_count_2] = 5
_count_2 += 1
_result_2[_count_2] = 6
const surrounded = _result_2
const _result_3 = table.clone(rest)
local _count_3 = #_result_3
const _length_2 = #rest
table.move(rest, 1, _length_2, _count_3 + 1, _result_3)
_count_3 += _length_2
const joined = _result_3
const copied = table.clone(rest)
const _result_4 = {1}
local _count_4 = 1
const _length_3 = #rest
table.move(rest, 1, _length_3, _count_4 + 1, _result_4)
_count_4 += _length_3
const mutable = _result_4
const function make(): { number }
  return {7, 8}
end
const _result_5 = table.clone(make())
local _count_5 = #_result_5
_count_5 += 1
_result_5[_count_5] = 9
const called = _result_5
const names = {"a"}
const _result_6 = table.clone(names)
local _count_6 = #_result_6
_count_6 += 1
_result_6[_count_6] = "b"
const _length_4 = #names
table.move(names, 1, _length_4, _count_6 + 1, _result_6)
_count_6 += _length_4
const mixed = _result_6
return nil
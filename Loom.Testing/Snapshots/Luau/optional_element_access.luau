type Inner = {
  read value: number,
}
const a: { { Inner }? }? = nil
local _result
if a ~= nil then
  _result = a[0]
end
local _result_1
if _result ~= nil then
  _result_1 = _result[0]
end
const x = _result_1
return nil
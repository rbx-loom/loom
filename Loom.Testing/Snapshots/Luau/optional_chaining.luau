type Inner = {
  read c: number,
}
type Outer = {
  read b: Inner?,
}
const a: Outer? = nil
local _result
if a ~= nil then
  const _target = a.b
  if _target ~= nil then
    _result = _target.c
  end
end
const x = _result
return nil
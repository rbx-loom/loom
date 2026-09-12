const function is_number(value: unknown): boolean
  local _match
  if typeof(value) == "number" then
    const n = value
    _match = true
  else
    _match = false
  end
  return _match
end
const v = 420 :: unknown
if is_number(v) then
  print(v + 69)
end
return nil
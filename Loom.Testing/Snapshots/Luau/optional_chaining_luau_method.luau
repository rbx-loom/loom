type Foo = {
  read DoFoo: (Foo) -> (),
}
type Bar = {
  read foo: Foo?,
}
const bar = (nil :: never) :: Bar?
local _result
if bar ~= nil then
  const _target = bar.foo
  if _target ~= nil then
    _result = _target:DoFoo()
  end
end
const _ = _result
return nil
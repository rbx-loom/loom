type A = typeof(69)
local x = 69
type B = typeof(x)
type Foo = {
  read foo: number,
  read bar: string,
}
const foo = { foo = 69, bar = "hi" }
type C = typeof(foo)
const y: typeof(x) = x
return nil
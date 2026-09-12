type Foo = {
  read bar: string,
}
const foo = { bar = "abc" }
print(foo.bar ~= nil)
return nil
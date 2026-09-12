type Foo = { read [string]: string }
const foo = { ["foo-bar"] = "abc" }
print(foo["foo-bar"] ~= nil)
print(foo["end"] ~= nil)
return nil
type Foo = {
  read do_foo: (Foo) -> (),
}
type Bar = Foo & {
  read foo: Foo,
}
type Baz = {
  read do_baz: (Baz, number) -> (),
}
const bar = (nil :: never) :: Bar
bar:do_foo()
bar.foo:do_foo()
const baz = (nil :: never) :: Baz
baz:do_baz(69)
return nil
type RenamedFoo = {
  read DoFoo: (RenamedFoo) -> (),
  read SuperGoob: number,
}
const goober = 420
const foo = { SuperGoob = goober, DoFoo = nil :: never }
const _ = foo.SuperGoob
foo:DoFoo()
foo:DoFoo()
return nil
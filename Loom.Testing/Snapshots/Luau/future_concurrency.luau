const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
const function identity(n: number): number
  return n
end
const function sum_of(a: number, b: number): number
  const first = Loom.future(identity, a)
  const second = Loom.future(identity, b)
  return Loom.await(first) + Loom.await(second)
end
const function all_of(a: number, b: number, c: number): { number }
  return Loom.await(Loom.future_all({Loom.future(identity, a), Loom.future(identity, b), Loom.future(identity, c)}))
end
const function first_of(a: number, b: number): number
  return Loom.await(Loom.future_race({Loom.future(identity, a), Loom.future(identity, b)}))
end
const function settled(n: number): number
  return Loom.await(Loom.future_resolved(n))
end
const function descendant(root: Instance): Instance
  return root:WaitForChild("a"):WaitForChild("b")
end
return nil
const numbers = {1, 2, 3, 4}
const function map<T, U>(array: { T }, converter: (T) -> U): { U }
  const new_array: { U } = {}
  for _, v in array do
    table.insert(new_array, converter(v))
  end
  return new_array
end
const function id<T>(n: T)
  return n
end
const function double(n: number)
  return n * 2
end
print(map(numbers, id))
print(map(numbers, double))
return nil
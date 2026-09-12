const function sum(...: number): number
  local values = {...}
  local total = 0
  for _, value in values do
    total += value
  end
  return total
end
const function combine(prefix: string, ...: string): string
  local rest = {...}
  local result = prefix
  for _, value in rest do
    result ..= value
  end
  return result
end
const s = sum(1, 2, 3, 4, 5)
print(s)
print(combine("a", "b", "c"))
return nil
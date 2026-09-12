const function factorial(n: number): number
  local result = 1
  for i = 2, n, if n < 2 then -1 else 1 do
    result *= i
  end
  return result
end
const result = factorial(5)
print(result)
return nil
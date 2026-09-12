const function returns_tuple(): (string, number)
  return "abc", 420
end
const one, two = returns_tuple()
print(one)
print(two)
return nil
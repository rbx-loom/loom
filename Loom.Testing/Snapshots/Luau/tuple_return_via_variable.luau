const function returns_tuple(): (string, number)
  const t = {"abc", 420}
  return table.unpack(t)
end
const result = returns_tuple()
print(result)
return nil
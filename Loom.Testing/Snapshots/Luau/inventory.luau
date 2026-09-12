type Item = {
  read name: string,
  read price: number,
  read quantity: number,
}
const function total(items: { Item }): number
  local sum = 0
  for _, item in items do
    sum += item.price * item.quantity
  end
  return sum
end
const items = {{ name = "Keyboard", price = 80, quantity = 2 }, { name = "Mouse", price = 25, quantity = 4 }, { name = "Monitor", price = 300, quantity = 1 }}
print(total(items))
return nil
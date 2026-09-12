type User = {
  read name: string,
  read age: number,
}
const user = { name = "Ada", age = 30 }
const name = user.name
const age = user.age
print(name)
print(age)
return nil
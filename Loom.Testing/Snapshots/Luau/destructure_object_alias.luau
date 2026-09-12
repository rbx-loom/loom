type User = {
  read name: string,
  read age: number,
}
const user = { name = "Ada", age = 30 }
const userAge = user.age
print(userAge)
return nil
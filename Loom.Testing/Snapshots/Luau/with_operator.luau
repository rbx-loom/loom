type User = {
  read name: string,
  read age: number,
}
const user = { name = "Poppy", age = 31 }
const older = { name = user.name, age = 32 }
return nil
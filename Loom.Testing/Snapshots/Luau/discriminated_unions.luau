type IAction<Kind> = {
  read kind: Kind & string,
}
type LogOutAction = IAction<"LogOut"> & {}
type SearchAction = IAction<"Search"> & {
  read query: string,
}
type ClickAction = IAction<"Click"> & {
  read x: number,
  read y: number,
}
type Action = LogOutAction | SearchAction | ClickAction
const a = (nil :: never) :: Action
if a.kind == "Search" then
  print(a.query)
elseif a.kind == "Click" then
  print(a.x)
  print(a.y)
elseif "LogOut" == "LogOut" then
  print(a)
end
return nil
type Shape = {
  read x: number,
  read y: number,
}
type ShapeStatic = {
  read create: (() -> Shape) & ((number, number) -> Shape),
}
const a = Shape.create()
const b = Shape.create(1, 2)
return nil
type Component = {
  read name: string,
}
type Entity = number
type ComponentStorage = { [Entity]: { Component } }
type WorldData = {
  read component_storage: ComponentStorage,
} & World
type Position = Component & {
  read x: number,
  read y: number,
}
type NameMap = {
  read Position: Position,
}
local eid: Entity = -1
type World = {
  spawn: (World, { Component }) -> Entity,
  query: <T>(World, Entity, T & keyof<NameMap>) -> { index<NameMap, T & keyof<NameMap>> },
}
local World_for_WorldData = {}
World_for_WorldData.__index = World_for_WorldData
World_for_WorldData = World_for_WorldData :: WorldData
function World_for_WorldData.spawn(self: WorldData, components)
  eid += 1
  const entity = eid
  if self.component_storage[entity] == nil then
    self.component_storage[entity] = components
  else
    for _, c in components do
      table.insert(self.component_storage[entity], c)
    end
  end
  return entity
end
function World_for_WorldData.query(self: WorldData, entity, name)
  const components = self.component_storage[entity]
  const result: { Component } = {}
  for _, c in components do
    if c.name == name then
      table.insert(result, c)
    end
  end
  return result :: never
end
const world = setmetatable({ component_storage = {} }, World_for_WorldData) :: WorldData
const player = world:spawn({{ name = "Position", x = 69, y = 420 }})
print(world:query(player, "Position"))
return nil
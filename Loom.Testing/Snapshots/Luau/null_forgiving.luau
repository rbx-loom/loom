const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
type Player = {
  read health: number?,
}
const player = { health = 100 }
const health = player.health :: Loom.NonNullable<typeof(player.health)>
return nil
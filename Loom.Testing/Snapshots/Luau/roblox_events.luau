const world = game:GetService("Workspace")
const part = world:FindFirstChildOfClass("Part")
const function on_touch(hit: BasePart): ()
  return print(hit)
end
if part ~= nil then
  const on_touch_conn = part.Touched:Connect(on_touch)
end
return nil
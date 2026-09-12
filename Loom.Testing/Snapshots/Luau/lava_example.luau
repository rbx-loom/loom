const collection_service = game:GetService("CollectionService")
for _, obj in collection_service:GetTagged("Lava") do
  if not (typeof(obj) == "Instance" and obj:IsA("BasePart")) then continue end
  obj.Touched:Connect(function(hit)
    local _result
    const _target = hit.parent
    if _target ~= nil then
      _result = _target:FindFirstChildOfClass("Humanoid")
    end
    local _result_1
    if _result ~= nil then
      _result_1 = _result:TakeDamage(100)
    end
    return _result_1
  end)
end
return nil
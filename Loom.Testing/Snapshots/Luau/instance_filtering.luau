const world = game:GetService("Workspace")
const children = world:GetChildren()
const _source = world:GetChildren()
const _result: { BasePart } = table.create(#_source)
local _count = 0
for _, child in _source do
  if not child:IsA("BasePart") then continue end
  _count += 1
  _result[_count] = child
end
const parts = _result
const descendants = world:GetDescendants()
const _source_1 = world:GetDescendants()
const _result_1: { BasePart } = table.create(#_source_1)
local _count_1 = 0
for _, child in _source_1 do
  if not child:IsA("BasePart") then continue end
  _count_1 += 1
  _result_1[_count_1] = child
end
const descendant_parts = _result_1
return nil
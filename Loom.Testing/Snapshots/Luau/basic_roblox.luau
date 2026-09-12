const collection = game:GetService("CollectionService")
const instances = collection:GetTagged("Lava")
for _, i in instances do
  if not i:IsA("BasePart") then continue end
  print(i.Name)
  print(i.ClassName)
end
return nil
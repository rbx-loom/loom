const function countdown(n: number): string
  local count = n
  local result = ""
  while n > 0 do
    result ..= tostring(count) .. " "
    count -= 1
  end
  return result .. "Go!"
end
print(countdown(10))
return nil
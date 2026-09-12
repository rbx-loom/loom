const function is_palindrome(original: number): boolean
  local n = original
  local reversed = 0
  while n > 0 do
    reversed = reversed * 10 + (n % 10)
    n //= 10
  end
  return original == reversed
end
print(is_palindrome(69))
print(is_palindrome(1991))
return nil
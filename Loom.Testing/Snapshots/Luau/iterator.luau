-- A type that says how to iterate itself. The loop calls 'next' until it answers none, so the
-- iterator carries its own position rather than the loop counting on its behalf.
type Countdown = {
  remaining: number,
} & Iterator<number>
local Iterator_number_for_Countdown = {}
Iterator_number_for_Countdown.__index = Iterator_number_for_Countdown
Iterator_number_for_Countdown = Iterator_number_for_Countdown :: Countdown
function Iterator_number_for_Countdown.next(self: Countdown): number?
  if self.remaining <= 0 then
    return nil
  end
  self.remaining -= 1
  return self.remaining + 1
end
const counter = setmetatable({ remaining = 3 }, Iterator_number_for_Countdown) :: Countdown
for value in function()
  return counter:next()
end do
  print(value)
end
const _iterator = setmetatable({ remaining = 2 }, Iterator_number_for_Countdown) :: Countdown
for other in function()
  return _iterator:next()
end do
  print(other)
end
return nil
const _subject = 404
local _match
if _subject == 0 then
  _match = "ok"
elseif _subject == 404 then
  _match = "missing"
else
  _match = "other"
end
const msg = _match
const _subject_1 = "lava"
local _match_1
if _subject_1 == "lava" then
  _match_1 = true
elseif _subject_1 == "water" then
  _match_1 = false
else
  _match_1 = false
end
const flag = _match_1
const _subject_2 = nil
local _match_2
if _subject_2 == nil then
  _match_2 = 1
else
  _match_2 = 0
end
const maybe = _match_2
const _subject_3 = true
local _match_3
if _subject_3 == true then
  const _subject_4 = 1
  local _match_4
  if _subject_4 == 1 then
    _match_4 = "one"
  else
    _match_4 = "other"
  end
  _match_3 = _match_4
else
  _match_3 = "no"
end
const nested = _match_3
return nil
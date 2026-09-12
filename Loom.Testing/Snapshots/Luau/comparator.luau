type Comparator<T> = {
  read less: (T, T) -> boolean,
}
const function max<T>(a: T, b: T, cmp: Comparator<T>): T
  return if cmp.less(a, b) then b else a
end
const function less(a: number, b: number): boolean
  return a < b
end
const numbers_comparator = { less = less }
print(max(5, 8, numbers_comparator))
return nil
type Matrix = { { number } }
const function rotate(matrix: Matrix): Matrix
  return {{matrix[2][0], matrix[1][0], matrix[0][0]}, {matrix[2][1], matrix[1][1], matrix[0][1]}, {matrix[2][2], matrix[1][2], matrix[0][2]}}
end
const matrix = {{1, 2, 3}, {4, 5, 6}, {7, 8, 9}}
print(rotate(matrix))
return nil
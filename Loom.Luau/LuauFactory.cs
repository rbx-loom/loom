using Loom.Luau.AST;

namespace Loom.Luau;

public static class LuauFactory
{
    public const string RuntimeImportName = "Loom";

    public static readonly Identifier Self = new("self");

    public static readonly HashSet<string> Keywords =
    [
        "and",
        "break",
        "do",
        "else",
        "elseif",
        "end",
        "false",
        "for",
        "function",
        "if",
        "in",
        "local",
        "nil",
        "not",
        "or",
        "repeat",
        "return",
        "then",
        "true",
        "until",
        "while",
        "continue"
    ];

    public static QualifiedTypeName QualifyRuntimeType(TypeName typeName) => new([RuntimeImportName], typeName);

    public static Call Bit32Call(string name, List<LuauExpression> arguments) => LibraryCall("bit32", [name], arguments);
    public static Call MathCall(string name, List<LuauExpression> arguments) => LibraryCall("math", [name], arguments);
    public static Call MathClampCall(LuauExpression value, LuauExpression minimum, LuauExpression maximum) => MathCall("clamp", [value, minimum, maximum]);
    public static Call TableCall(string name, List<LuauExpression> arguments) => LibraryCall("table", [name], arguments);

    public static LuauExpression Literal(object? value) =>
        value switch
        {
            long l => new NumberLiteral(l),
            int i => new NumberLiteral(i),
            double d => new NumberLiteral(d),
            string s => new StringLiteral(s),
            bool b => new BooleanLiteral(b),
            _ => new NilLiteral()
        };
    public static Call StringCall(string name, List<LuauExpression> arguments) => LibraryCall("string", [name], arguments);
    public static Call TaskCall(string name, List<LuauExpression> arguments) => LibraryCall("task", [name], arguments);
    public static Call RequireCall(string path) => new(new Identifier("require"), [new StringLiteral(path)]);
    public static Call SetMetatableCall(Table main, LuauExpression meta) => new(new Identifier("setmetatable"), [main, meta]);

    public static Call RuntimeLibraryCall(List<string> path, List<LuauExpression> arguments) =>
        new(new PropertyAccess(new Identifier(RuntimeImportName), path), arguments);

    public static Call LibraryCall(string libraryName, List<string> path, List<LuauExpression> arguments, bool isMethod = false) =>
        new(new PropertyAccess(new Identifier(libraryName), path), arguments, isMethod);

    public static ConstVariable RuntimeLibraryImport(string runtimeLibPath) => new(RuntimeImportName, null, RequireCall(runtimeLibPath));
    public static LuauNode EmptyVariable() => new ConstVariable("_", null, new NilLiteral());

    public static LuauExpression UnwrapParentheses(LuauExpression expression)
    {
        while (true)
        {
            if (expression is not Parenthesized parenthesized)
                return expression;

            expression = parenthesized.Expression;
        }
    }
}
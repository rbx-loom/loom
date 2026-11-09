using System.Text;
using Loom.Core.TypeChecking.Serialization;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Serialization;

/// <summary>Shared pieces: the offset cursor, local-name reservation, and expression builders.</summary>
internal sealed partial class SerializationEmitter
{
    private static readonly List<string> _cFramePositionComponents = ["X", "Y", "Z"];
    private static readonly List<string> _quaternionLocals = ["qx", "qy", "qz", "qw"];
    private static readonly NumberLiteral _zero = new(0);
    private static readonly NumberLiteral _one = new(1);

    /// <summary>Running header-bit and body-byte positions, both compile-time constants.</summary>
    /// <summary>
    ///     Tracks where the next write lands. Offsets stay compile-time constants until a value-dependent
    ///     field is reached; from there a runtime local carries the position, seeded with the constant
    ///     the cursor had already accumulated. A fully fixed schema never leaves the constant path.
    /// </summary>
    private sealed class Cursor(int startingByteOffset)
    {
        public int BitOffset;
        public bool IsDynamic;
        private int _byteOffset = startingByteOffset;

        /// <summary>
        ///     Fixed-width advances taken since the local was last written to. A run of them is a constant
        ///     the addressing can simply carry, so the local is stepped once at the end of the run rather
        ///     than after every field. Valid only along the straight line it accumulated on: a branch or a
        ///     loop body is a different path, and <see cref="Flush" /> settles the count before one starts
        ///     and again before one ends.
        /// </summary>
        private int _pending;

        public LuauExpression Position =>
            !IsDynamic
                ? new NumberLiteral(_byteOffset)
                : _pending == 0
                    ? new Identifier(OffsetLocal)
                    : Add(new Identifier(OffsetLocal), new NumberLiteral(_pending));

        /// <summary>
        ///     Writes the folded advances out to the local, leaving it holding the true position. Callers
        ///     that need the local itself rather than the position - anything reading <see cref="OffsetLocal" />
        ///     through something other than <see cref="Position" />, and every path in or out of a block -
        ///     have to settle it first.
        /// </summary>
        public void Flush(List<LuauStatement> body)
        {
            if (!IsDynamic || _pending == 0)
                return;

            body.Add(new ExpressionStatement(new BinaryOperator(new Identifier(OffsetLocal), "+=", new NumberLiteral(_pending))));
            _pending = 0;
        }

        /// <summary>
        ///     Runtime origin for bit positions, set while emitting one entry of a collection. Header bits
        ///     normally sit at compile-time positions, but a collection's entries each need their own slice
        ///     of a block whose location is only known once the length has been read.
        /// </summary>
        public LuauExpression? BitBase;

        public LuauExpression BitPosition => BitBase == null ? new NumberLiteral(BitOffset) : Add(BitBase, new NumberLiteral(BitOffset));

        public void Advance(int bytes)
        {
            if (!IsDynamic)
            {
                _byteOffset += bytes;
                return;
            }

            _pending += bytes;
        }

        /// <summary>
        ///     Advances by an amount only known at runtime. Whatever was folded so far rides along in the
        ///     same statement, since the local has to be written either way.
        /// </summary>
        public void AdvanceBy(List<LuauStatement> body, LuauExpression bytes)
        {
            GoDynamic(body);
            var amount = _pending == 0 ? bytes : Add(new NumberLiteral(_pending), bytes);
            _pending = 0;
            body.Add(new ExpressionStatement(new BinaryOperator(new Identifier(OffsetLocal), "+=", amount)));
        }

        public void GoDynamic(List<LuauStatement> body)
        {
            if (IsDynamic)
                return;

            body.Add(new LocalVariable(OffsetLocal, null, new NumberLiteral(_byteOffset)));
            IsDynamic = true;
        }
    }

    /// <summary>
    ///     Whether a field ever claims header bits or body bytes on its own. A collection whose element (or
    ///     key and value) is entirely blobs/constants writes nothing to the buffer regardless of how many
    ///     entries it has at runtime, so the cursor never needs to leave its compile-time constant behind.
    /// </summary>
    private static bool NeedsBufferSpace(SerializationField field) => field.BodyBytes != 0 || field.HeaderBits != 0;

    /// <summary>Binds an expression to a local when it is about to be read more than once.</summary>
    private LuauExpression BindIfReused(LuauExpression value, int uses, string preferred, List<LuauStatement> body)
    {
        if (uses < 2 || value is Identifier)
            return value;

        var local = ReserveLocal(preferred + "_value");
        body.Add(new ConstVariable(local, null, value));

        return new Identifier(local);
    }

    /// <summary>
    ///     Children of a composite, each paired with the expression that reaches it from the value the
    ///     composite occupies. Sizing and writing both walk these, and resolving them independently is
    ///     what let the two drift: each site that reached for the function's parameter instead of the
    ///     value it was handed produced a path naming a property that does not exist.
    /// </summary>
    private static IEnumerable<(SerializationField Field, LuauExpression Value)> ChildrenOf(SerializationField parent, LuauExpression value) =>
        parent switch
        {
            TupleField tuple => tuple.Elements.Select(element => (element, AccessRelative(value, element.Path, parent.Path))),
            OptionalField optional => [(optional.Inner, AccessRelative(value, optional.Inner.Path, parent.Path))],
            _ => []
        };

    /// <summary>One variant's fields, reached the same way. The variant shares the union's own path.</summary>
    private static IEnumerable<(SerializationField Field, LuauExpression Value)> ChildrenOf(
        UnionField union,
        SerializationVariant variant,
        LuauExpression value) =>
        variant.Fields.Select(field => (field, AccessRelative(value, field.Path, union.Path)));

    /// <summary>
    ///     Reaches a field from the value its enclosing path names. A runtime-kind union's variant carries
    ///     the union's own path rather than one beneath it - the value <em>is</em> the payload - so there
    ///     is nothing left to index.
    /// </summary>
    private static LuauExpression AccessRelative(LuauExpression value, string path, string enclosing) =>
        path == enclosing ? value : Access(value, RelativePath(path, enclosing));

    /// <summary>Strips an enclosing path, leaving the segments to index from the value that path names.</summary>
    private static string RelativePath(string path, string enclosing) =>
        path.StartsWith(enclosing + ".", StringComparison.Ordinal) ? path[(enclosing.Length + 1)..] : path;

    private static PropertyAccess Access(LuauExpression source, string path) => new(source, [..path.Split('.')]);

    /// <summary>
    ///     Last segment of a path, as a usable Luau identifier. Element paths carry brackets - <c>names[]</c>,
    ///     <c>pair[1]</c> - which are neither valid in a name nor distinct from the collection's own local,
    ///     so they become a suffix instead. The schema's one-letter subscripts are spelled out on the way
    ///     through, since these names reach the emitted Luau.
    /// </summary>
    private static string LeafName(string path)
    {
        var leaf = path[(path.LastIndexOf('.') + 1)..];
        if (!leaf.Contains('['))
            return leaf;

        var name = new StringBuilder();
        for (var index = 0; index < leaf.Length;)
        {
            if (leaf[index] != '[')
            {
                name.Append(leaf[index++]);
                continue;
            }

            var close = leaf.IndexOf(']', index);
            if (close < 0)
                break;

            name.Append('_').Append(SubscriptName(leaf[(index + 1)..close]));
            index = close + 1;
        }

        return name.ToString();
    }

    /// <summary>Names one component of a multipart value - a Vector3's X, a CFrame's quaternion terms.</summary>
    private static string ComponentName(string path, string component) => $"{LeafName(path)}_{component.ToLowerInvariant()}";

    private static string SubscriptName(string subscript) =>
        subscript switch
        {
            "" => "element",
            "k" => "key",
            "v" => "value",
            _ => subscript
        };

    private static LuauExpression Add(LuauExpression left, LuauExpression right) =>
        IsNumber(left, 0)
            ? right
            : IsNumber(right, 0)
                ? left
                : new BinaryOperator(Operand(left), "+", Operand(right));

    /// <summary>
    ///     Parenthesizes an if-expression used as an operand. Luau binds it loosely enough that the else
    ///     branch swallows whatever follows, so a sum of several would nest instead of adding up.
    /// </summary>
    private static LuauExpression Operand(LuauExpression expression) => expression is IfExpression ? new Parenthesized(expression) : expression;

    private static BinaryOperator Subtract(LuauExpression left, LuauExpression right) => new(left, "-", right);

    private static LuauExpression Multiply(LuauExpression left, LuauExpression right) =>
        IsNumber(left, 0) || IsNumber(right, 0)
            ? _zero
            : IsNumber(left, 1)
                ? right
                : IsNumber(right, 1)
                    ? left
                    : new BinaryOperator(left, "*", right);

    private static bool IsNumber(LuauExpression expression, double value) => expression is NumberLiteral literal && literal.Value.Equals(value);
    private static BinaryOperator Divide(LuauExpression left, LuauExpression right) => new(left, "/", right);
}
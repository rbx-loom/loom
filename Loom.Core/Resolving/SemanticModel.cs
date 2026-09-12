global using SymbolTable = System.Collections.Generic.Dictionary<Loom.Core.Parsing.AST.NodeId, System.Collections.Generic.List<Loom.Core.Resolving.Symbols.Symbol>>;
using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Solving;
using Loom.Core.TypeChecking.Types;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Resolving;

public sealed record SemanticModel(Tree Tree, DiagnosticBag Diagnostics, SymbolTable Declarations, SymbolTable References)
    : DiagnosedResult(Diagnostics)
{
    internal int RuntimeReferences = 0;

    /// <summary>
    ///     The intrinsics this file resolved against, shared with every other file of the project. The
    ///     declaration and type tables below fall back to it rather than each holding a copy.
    /// </summary>
    internal AmbientIntrinsics Ambient { get; set; } = AmbientIntrinsics.None;

    /// <summary>
    ///     Every symbol declared for this file: the ones it declares itself, then the intrinsics every file
    ///     of the project shares. Anything asking "what names exist here" wants this rather than
    ///     <see cref="Declarations" />, which holds only the file's own.
    /// </summary>
    public IEnumerable<Symbol> DeclaredSymbols => Declarations.Values.SelectMany(symbols => symbols).Concat(Ambient.Symbols);
    public List<ExportBinding> Exports { get; } = [];

    /// <summary>
    ///     Wire formats built by the type checker for each <c>[serializable]</c> interface, consumed by the
    ///     generator to emit serializers. Compile-time only - no schema reaches the output.
    /// </summary>
    public Dictionary<InterfaceSymbol, SerializationSchema> SerializationSchemas { get; } = [];

    /// <summary>
    ///     Mapping interfaces a <c>serializer_map</c> call asked for, in first-use order. Hoisted into
    ///     one table per interface by the generator rather than rebuilt at each call site.
    /// </summary>
    public List<InterfaceType> SerializerMaps { get; } = [];

    /// <summary>
    ///     Which of each <c>[serializable]</c> interface's codec pieces this file's own calls actually
    ///     reach, collected before generation so <c>EmitSerializers</c> only emits those. An interface
    ///     absent here has no local usage at all - not the same as <see cref="SerializationUsage.None" />,
    ///     which would mean "checked and found nothing" rather than "never checked".
    /// </summary>
    public Dictionary<InterfaceSymbol, SerializationUsage> SerializationUsages { get; } = [];

    /// <summary>
    ///     Member accesses whose receiver is a <c>Future</c> the enclosing <c>await</c> resolves on the way
    ///     past, mapped to the type it settles to - so a chain of yielding calls needs one <c>await</c>
    ///     rather than one set of parentheses per step. Recorded by the type checker where it reads through
    ///     (see <c>TypeChecker.Await</c>).
    /// </summary>
    /// <remarks>
    ///     The settled type is kept rather than a bare flag because <see cref="GetPropertySymbols(Expression)" />
    ///     needs it: it resolves a member against the receiver's recorded type, which is still the
    ///     <c>Future</c>, and would otherwise find no declaration - taking <c>[luau_name]</c> and
    ///     <c>[luau_method]</c> with it, so every link past the first would emit unrenamed.
    /// </remarks>
    public Dictionary<NodeId, Type> ChainAwaitedReceiverTypes { get; } = [];

    private Dictionary<string, List<ExportBinding>> ExportsByName { get; } = [];

    public List<ImportBinding> ImportBindings { get; } = [];

    private HashSet<Symbol> ImportedSymbols { get; } = [];
    public bool DisableRuntimeLibraryImport { get; set; }
    public bool MustImportRuntimeLibrary =>
        !DisableRuntimeLibraryImport
        && !Tree.File.IsIntrinsic
        && (
            RuntimeReferences > 0
            || References.Any(pair => NonIntrinsicReferenceNodes.Contains(pair.Key)
                && pair.Value.Any(s => s is { File.Name: "runtime.loom", IsIntrinsic: true, IsTypeSymbol: true })
            )
        );

    /// <summary>
    ///     Node IDs of reference-site nodes that originate from a non-intrinsic source file.
    ///     Populated by the resolver as references are recorded, so
    ///     <see cref="MustImportRuntimeLibrary" /> can filter references by the file of the
    ///     referencing node without holding on to the node instances themselves.
    /// </summary>
    internal HashSet<NodeId> NonIntrinsicReferenceNodes { get; } = [];

    /// <summary>
    ///     Node IDs of names the resolver could not bind, recorded once it has reported them. Every stage
    ///     after the resolver looks these up and finds no symbol; without this they cannot tell a name the
    ///     user misspelled - already reported, in the words the user can act on - from a symbol that went
    ///     missing between stages, which is a compiler bug and worth reporting.
    /// </summary>
    private HashSet<NodeId> UnresolvedNames { get; } = [];

    internal void MarkUnresolved(Node node) => UnresolvedNames.Add(node.Id);

    /// <summary>Whether the resolver already reported this name as one it could not bind.</summary>
    public bool IsUnresolved(Node node) => UnresolvedNames.Contains(node.Id);
    internal TypeSolver TypeSolver { get; } = new(new DiagnosticBag(options: Diagnostics.Options));
    private SymbolLookup DeclarationsByName =>
        field ??= Declarations.Values.SelectMany(symbols => symbols)
            .Concat(Ambient.Symbols)
            .GroupBy(symbol => symbol.Name)
            .ToDictionary(group => group.Key, group => group.ToList());

    public List<NamespaceImportBinding> NamespaceImports { get; } = [];

    internal void AddExport(ExportBinding binding)
    {
        Exports.Add(binding);
        if (!ExportsByName.TryGetValue(binding.Name, out var bindings))
            ExportsByName[binding.Name] = bindings = [];

        bindings.Add(binding);
    }

    public List<ExportBinding> FindExports(string name) => ExportsByName.GetValueOrDefault(name, []);

    public Symbol? GetNamespaceMemberSymbol(Expression expression)
    {
        var (namespaceExpression, memberName) = expression switch
        {
            QualifiedName { Names: [{ } member] } qualified => ((Expression?)qualified.Identifier, member.Name.Text),
            PropertyAccess { Names: [{ } member] } propertyAccess => (propertyAccess.Expression, member.Name.Text),
            _ => (null, "")
        };

        if (namespaceExpression == null || GetSymbol(namespaceExpression) is not { } namespaceSymbol)
            return null;

        var binding = NamespaceImports.Find(namespaceImport => namespaceImport.Symbol == namespaceSymbol);
        return binding?.ModuleModel.FindExports(memberName).Find(export => !export.Symbol.IsTypeSymbol)?.Symbol;
    }

    internal void AddImportBinding(ImportBinding binding)
    {
        ImportBindings.Add(binding);
        ImportedSymbols.Add(binding.Symbol);
    }

    internal void AddNamespaceImport(NamespaceImportBinding binding)
    {
        NamespaceImports.Add(binding);
        ImportedSymbols.Add(binding.Symbol);
    }

    /// <summary>
    ///     A symbol standing in for an import whose module could not be resolved. It has no binding, since
    ///     there is no module to bind it to, but it still names something declared elsewhere.
    /// </summary>
    internal void AddUnresolvedImport(Symbol symbol) => ImportedSymbols.Add(symbol);

    /// <summary>
    ///     Whether the symbol was declared by another module and brought in by an import. Its declaration
    ///     belongs to a tree this model never walked, so anything reasoning about the declaration's position —
    ///     flow analysis in particular — has to treat it as coming from outside.
    /// </summary>
    public bool IsImported(Symbol symbol) => ImportedSymbols.Contains(symbol);

    internal void MarkImportUsed(Symbol symbol)
    {
        if (!ImportedSymbols.Contains(symbol))
            return;

        foreach (var binding in ImportBindings.Where(binding => binding.Symbol == symbol))
            binding.MarkUsed();

        foreach (var binding in NamespaceImports.Where(binding => binding.Symbol == symbol))
            binding.MarkUsed();
    }

    public bool IsCompileTimeConstant(Expression expression) =>
        expression is Literal or NameOf
        || expression is QualifiedName name && GetDeclaringSymbol(name.Identifier) is { Declaration: EnumDeclaration }
        || expression is PropertyAccess access && GetDeclaringSymbol(access.Expression) is { Declaration: EnumDeclaration }
        || expression is ElementAccess elementAccess && GetDeclaringSymbol(elementAccess.Expression) is { Declaration: EnumDeclaration }
        || expression is UnaryOperator { Operator.Text: "-" } unary && IsCompileTimeConstant(unary.Operand)
        || expression is BinaryOperator { Operator.Text: "+" or "-" or "*" or "/" or "//" or "^" or "%" or "&" or "|" or "~" or "<<" or ">>" or ">>>" } binary
            && IsCompileTimeConstant(binary.Left) && IsCompileTimeConstant(binary.Right);

    public object? GetConstantValue(Expression expression) =>
        expression switch
        {
            QualifiedName qn when GetType(qn.Identifier) is ObjectType objectType
                && objectType.GetProperty(qn.Names[0].Name.Text) is { ValueType: LiteralType literalType } =>
                literalType.Value,
            UnaryOperator { Operator.Text: "-" } unary when ToDouble(GetConstantValue(unary.Operand)) is { } d => -d,
            BinaryOperator { Operator.Text: var op } binary
                when ToDouble(GetConstantValue(binary.Left)) is { } l && ToDouble(GetConstantValue(binary.Right)) is { } r =>
                EvaluateBinaryConstant(op, l, r),
            _ when GetType(expression) is LiteralType literalType => literalType.Value,
            _ => null
        };

    private static double? ToDouble(object? value) =>
        value switch
        {
            double d => d,
            long l => l,
            int i => i,
            _ => null
        };

    private static double? EvaluateBinaryConstant(string op, double l, double r) =>
        op switch
        {
            "+" => l + r,
            "-" => l - r,
            "*" => l * r,
            "/" => l / r,
            "//" => Math.Floor(l / r),
            "^" => Math.Pow(l, r),
            "%" => l % r,
            "&" => (double)((long)l & (long)r),
            "|" => (double)((long)l | (long)r),
            "~" => (double)((long)l ^ (long)r),
            "<<" => (double)((long)l << (int)r),
            ">>" => (double)((long)l >> (int)r),
            ">>>" => (double)((long)l >>> (int)r),
            _ => null
        };

    public List<Symbol> GetDeclarationSymbols(Node node)
    {
        while (true)
        {
            if (node is not Declare declare)
                return Declarations.TryGetValue(node.Id, out var declared) ? declared : Ambient.Declarations.GetValueOrDefault(node.Id, []);

            node = declare.Signature;
        }
    }

    public Symbol? GetSymbol(Node node, SymbolKind? kind = null) =>
        References.TryGetValue(node.Id, out var symbols) || Ambient.References.TryGetValue(node.Id, out symbols)
            ? Pick(symbols, kind)
            : null;

    public Symbol? GetDeclarationSymbol(Node node, SymbolKind? kind = null)
    {
        while (true)
        {
            if (node is not Declare declare)
                return FindDeclaration(node, kind);

            node = declare.Signature;
        }
    }

    public Symbol? GetDeclaringSymbol(Node node, SymbolKind? kind = null)
    {
        var referenceSymbol = GetSymbol(node, kind);
        return referenceSymbol == null ? null : GetDeclarationSymbol(referenceSymbol.Declaration, kind);
    }

    public bool TryGetIntrinsicAttribute(Expression expression, string name, [MaybeNullWhen(false)] out AttributeSymbol attribute)
    {
        attribute = null;
        var property = GetPropertySymbol(expression);
        return property != null && property.TryGetIntrinsicAttribute(name, out attribute);
    }

    public PropertySymbol? GetPropertySymbol(Expression expression) => GetPropertySymbols(expression).FirstOrDefault();

    /// <summary>Every declaration the expression's member name has, which is more than one only for an overload set.</summary>
    public IReadOnlyList<PropertySymbol> GetPropertySymbols(Expression expression)
    {
        var (objectExpression, names) = SplitAccessExpression(expression);
        if (objectExpression == null || names.Length == 0)
            return [];

        var receiverType = ChainAwaitedReceiverTypes.TryGetValue(expression.Id, out var settled) ? settled : GetType(objectExpression);
        return GetPropertySymbols(receiverType, names);
    }

    /// <summary>
    ///     One property symbol per step of a chained access like <c>a.b.c</c>, in order - unlike
    ///     <see cref="GetPropertySymbols(Expression)" />, which only exposes the last step's (overload)
    ///     symbols. <see cref="LuauGenerator.GenerateRenamedAccess" /> is the one caller: a chain can need a
    ///     <c>luau_name</c> rename at every step, not just the last.
    /// </summary>
    public IReadOnlyList<PropertySymbol?> GetPropertySymbolChain(Expression expression)
    {
        var (objectExpression, names) = SplitAccessExpression(expression);
        if (objectExpression == null || names.Length == 0)
            return [];

        var receiverType = ChainAwaitedReceiverTypes.TryGetValue(expression.Id, out var settled) ? settled : GetType(objectExpression);
        return GetPropertySymbolChain(receiverType, names);
    }

    private static (Expression? Object, string[] Names) SplitAccessExpression(Expression expression) =>
        expression switch
        {
            QualifiedName qualified => (qualified.Identifier, qualified.Names.Select(d => d.Name.Text).ToArray()),
            PropertyAccess propertyAccess => (propertyAccess.Expression, propertyAccess.Names.Select(d => d.Name.Text).ToArray()),
            ElementAccess { IndexExpression: Literal { Value: string propertyName } } elementAccess => (elementAccess.Expression, [propertyName]),
            _ => (expression, [])
        };

    /// <summary> Looks up a property by path directly off a type, for callers with no access-expression node to read it from (e.g. a match object pattern field). </summary>
    public PropertySymbol? GetPropertySymbol(Type objectType, IReadOnlyList<string> path) => GetPropertySymbols(objectType, path).FirstOrDefault();

    /// <inheritdoc cref="GetPropertySymbol(Type, IReadOnlyList{string})" />
    /// <remarks>Returns every declaration at the path, which is more than one only for an overload set.</remarks>
    public IReadOnlyList<PropertySymbol> GetPropertySymbols(Type objectType, IReadOnlyList<string> path)
    {
        objectType = objectType.NonNullable();
        objectType = objectType switch
        {
            InstantiatedType instantiated => instantiated.Expand(),
            // Bare, uninstantiated - a generic interface's own name used as a value (its static-holder
            // symbol) resolves to this. Only the interface's Name is read below, which its own
            // UnderlyingType already carries, so unwrapping this far - rather than filling defaults and
            // substituting, as a real access needs - is enough and cannot fail for a parameter with none.
            GenericType generic => generic.UnderlyingType,
            _ => objectType
        };

        if (objectType is not InterfaceType interfaceType)
            return [];

        var interfaceSymbol = FindDeclarationSymbol<InterfaceSymbol>(interfaceType.Name);
        return interfaceSymbol?.GetPropertiesAtPath(path) ?? [];
    }

    /// <inheritdoc cref="InterfaceSymbol.GetPropertyChainAtPath" />
    public IReadOnlyList<PropertySymbol?> GetPropertySymbolChain(Type objectType, IReadOnlyList<string> path)
    {
        objectType = objectType.NonNullable();
        objectType = objectType switch
        {
            InstantiatedType instantiated => instantiated.Expand(),
            GenericType generic => generic.UnderlyingType,
            _ => objectType
        };

        if (objectType is not InterfaceType interfaceType)
            return [];

        var interfaceSymbol = FindDeclarationSymbol<InterfaceSymbol>(interfaceType.Name);
        return interfaceSymbol?.GetPropertyChainAtPath(path) ?? [];
    }

    /// <summary>
    ///     The symbol a single member name resolves to: a plain property, or - when nothing declares one by
    ///     that name - a trait method the interface implements. <see cref="GetPropertySymbol(Type, IReadOnlyList{string})" />
    ///     alone only sees an interface's own <see cref="PropertySymbol" />s (<see cref="InterfaceSymbol.FullProperties" />);
    ///     a name a trait contributes is a <see cref="FunctionSymbol" /> declared on a separate <c>implement</c>
    ///     block (<see cref="InterfaceSymbol.FullImplementations" />), which this also considers - the same
    ///     source the type checker's own self-type merging (<c>VisitSelfExpression</c>) reads from.
    /// </summary>
    public Symbol? GetMemberSymbol(Type objectType, string name)
    {
        if (GetPropertySymbol(objectType, [name]) is { } property)
            return property;

        objectType = objectType.NonNullable();
        objectType = objectType switch
        {
            InstantiatedType instantiated => instantiated.Expand(),
            GenericType generic => generic.UnderlyingType,
            _ => objectType
        };

        if (objectType is not InterfaceType interfaceType)
            return null;

        var interfaceSymbol = FindDeclarationSymbol<InterfaceSymbol>(interfaceType.Name);
        var declaration = interfaceSymbol?.FullImplementations
            .SelectMany(implement => implement.Body.Implementations)
            .FirstOrDefault(function => function.Name.Text == name);

        return declaration == null ? null : GetDeclarationSymbol(declaration);
    }

    /// <summary>
    ///     Every member <paramref name="interfaceType" /> offers: its own <see cref="InterfaceType.Properties" />,
    ///     plus one entry per method a trait it implements contributes that is not already there.
    ///     <see cref="InterfaceType.Properties" /> does not have these for an interface's general type - a
    ///     trait implementation sits outside the interface's own declaration, so it never reaches
    ///     <see cref="ObjectType" /> the way the type checker's construction-site and self-type merging
    ///     (<c>BindInterfaceInvocation</c>, <c>VisitSelfExpression</c>) redoes per <c>new</c> expression or
    ///     <c>implement</c> block. Those two, though, bake trait properties directly into the bound type's own
    ///     <see cref="ObjectType" /> - so a name already present is skipped rather than added a second time.
    /// </summary>
    public IReadOnlyList<ObjectProperty> GetMembersOf(InterfaceType interfaceType)
    {
        if (FindDeclarationSymbol<InterfaceSymbol>(interfaceType.Name) is not { } interfaceSymbol || interfaceSymbol.FullImplementations.Count == 0)
            return interfaceType.Properties;

        var existing = interfaceType.Properties.Select(property => property.Name).ToHashSet();
        var traitProperties = interfaceSymbol.FullImplementations
            .SelectMany(implement => implement.Body.Implementations)
            .Where(declaration => !existing.Contains(declaration.Name.Text))
            .Select(declaration => new ObjectProperty(false, declaration.Name.Text, GetType(declaration)));

        return [..interfaceType.Properties, ..traitProperties];
    }

    public Type GetType(Node node) => TypeSolver.GetType(node);
    public Type? GetDeclarationType(Node node) => GetSymbol(node) is { } symbol ? TypeSolver.GetType(symbol.Declaration) : null;
    public T? FindIntrinsicDeclarationSymbol<T>(string name) where T : Symbol => FindDeclarationSymbol<T>(name, s => s.IsIntrinsic);

    /// <summary>The declaration of the named type, for navigating from a value to the type it was given.</summary>
    public Symbol? FindTypeDeclaration(string name) => FindDeclarationSymbol<Symbol>(name, symbol => symbol.IsTypeSymbol);
    private T? FindDeclarationSymbol<T>(string name) where T : Symbol => FindDeclarationSymbol<T>(name, static _ => true);

    /// <remarks>
    ///     A declaration of this file's own wins over an intrinsic of the same name. Both are in the table -
    ///     intrinsics reach every file - and a project interface named after a Roblox class would otherwise
    ///     resolve to the class, so its own members could not be found from its own type.
    /// </remarks>
    private T? FindDeclarationSymbol<T>(string name, Func<T, bool> predicate) where T : Symbol =>
        DeclarationsByName.TryGetValue(name, out var symbols)
            ? symbols.OfType<T>().Where(predicate).OrderBy(symbol => symbol.IsIntrinsic).FirstOrDefault()
            : null;

    private Symbol? FindDeclaration(Node node, SymbolKind? kind) =>
        Declarations.TryGetValue(node.Id, out var symbols) || Ambient.Declarations.TryGetValue(node.Id, out symbols)
            ? Pick(symbols, kind)
            : null;

    private static Symbol? Pick(List<Symbol> symbols, SymbolKind? kind) =>
        kind != null ? symbols.Find(s => s.Kind == kind) : symbols.FirstOrDefault();
}
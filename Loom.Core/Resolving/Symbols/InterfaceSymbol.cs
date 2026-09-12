using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

public sealed class InterfaceSymbol(InterfaceDeclaration declaration, string name, bool isSealed, List<InterfaceSymbol>? constraints)
    : TypeSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.Interface;
    public bool IsSealed { get; } = isSealed;
    public IReadOnlyList<InterfaceSymbol>? Constraints { get; } = constraints;
    public List<PropertySymbol> Properties { get; } = [];
    /// <summary> Current interface properties + all constraint properties </summary>
    public IReadOnlyList<PropertySymbol> FullProperties => field ??= [..Properties, ..GetFieldAndConstraintFields(i => i.Properties)];
    public List<TraitSymbol> Implements { get; } = [];
    public IReadOnlyList<Implement> FullImplementations => field ??= [..Implementations, ..GetFieldAndConstraintFields(i => i.Implementations)];
    public List<Implement> Implementations { get; } = [];
    public List<StaticBlock> StaticBlocks { get; } = [];

    /// <summary>Metamethod name (e.g. "__add") to property name, for own properties tagged with [luau_metamethod(...)].</summary>
    public IReadOnlyDictionary<string, string> Metamethods { get; } =
        MetamethodAttributes.Collect(declaration.Body?.Members.OfType<PropertyDeclaration>() ?? [], p => p.Name.Text, p => p.Attributes);

    public PropertySymbol? GetPropertyAtPath(IReadOnlyList<string> path) => GetPropertiesAtPath(path).FirstOrDefault();

    /// <summary>
    ///     Every declaration at <paramref name="path" />, in declaration order. More than one only for an
    ///     overload set, whose shapes the type checker merges into a single intersection-typed property - the
    ///     names its parameters were declared under survive only here.
    /// </summary>
    public IReadOnlyList<PropertySymbol> GetPropertiesAtPath(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return [];

        var firstName = path[0];
        var properties = FullProperties.Where(p => p.Name == firstName).ToArray();
        return properties is [{ PointsTo: { } pointsTo }, ..] && path.Count > 1 ? pointsTo.GetPropertiesAtPath(path.Skip(1).ToArray()) : properties;
    }

    /// <summary>
    ///     One property symbol per step of <paramref name="path" />, in declaration order - unlike
    ///     <see cref="GetPropertiesAtPath" />, which only returns the last step's (overload) symbols. A
    ///     chained access like <c>a.b.c</c> can need a <c>luau_name</c> rename at every step, not just the
    ///     last, so a renamer needs each step's own symbol to check. A step whose name resolves to nothing
    ///     stops the walk early - <c>null</c> for that step and every step after it, since there is no
    ///     further interface to keep walking into.
    /// </summary>
    public List<PropertySymbol?> GetPropertyChainAtPath(IReadOnlyList<string> path)
    {
        var chain = new List<PropertySymbol?>(path.Count);
        InterfaceSymbol? current = this;
        foreach (var name in path)
        {
            var property = current?.FullProperties.FirstOrDefault(p => p.Name == name);
            chain.Add(property);
            current = property?.PointsTo;
        }

        return chain;
    }

    public override string ToString() =>
        $"InterfaceSymbol({Name}, IsSealed: {IsSealed}, Properties: [{string.Join(", ", Properties.Select(s => s.Name))}] Implements: [{string.Join(", ", Implements.Select(s => s.Name))}], Constraints: [{string.Join(", ", Constraints?.Select(s => s.Name) ?? [])}])";

    private T[] GetFieldAndConstraintFields<T>(Func<InterfaceSymbol, IReadOnlyList<T>> selector) =>
        Constraints?.SelectMany(c => selector(c).Concat(c.GetFieldAndConstraintFields(selector))).ToArray() ?? [];
}
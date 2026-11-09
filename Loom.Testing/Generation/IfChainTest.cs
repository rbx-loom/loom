using Loom.Luau;
using Loom.Luau.AST;
using Identifier = Loom.Luau.AST.Identifier;

namespace Loom.Testing.Generation;

[Collection("Assembly")]
public class IfChainTest
{
    private static Chunk Body(string name) => new([new ExpressionStatement(new Identifier(name))]);

    [Fact]
    public void WithNoBranches_AppendsNothing()
    {
        var target = new List<LuauStatement>();
        _ = new IfChain(target);

        Assert.Empty(target);
    }

    [Fact]
    public void FirstBranch_AppendsTheIfStatementImmediately()
    {
        var target = new List<LuauStatement>();
        var chain = new IfChain(target);

        chain.Add(new Identifier("a"), Body("x"));

        var statement = Assert.IsType<IfStatement>(Assert.Single(target));
        Assert.Empty(statement.ElseIfBranches);
    }

    [Fact]
    public void LaterBranches_BecomeElseIfsOfTheSameStatement()
    {
        var target = new List<LuauStatement>();
        var chain = new IfChain(target);

        chain.Add(new Identifier("a"), Body("x"));
        chain.Add(new Identifier("b"), Body("y"));
        chain.Add(new Identifier("c"), Body("z"));

        var statement = Assert.IsType<IfStatement>(Assert.Single(target));
        Assert.Equal(2, statement.ElseIfBranches.Count);
    }

    [Fact]
    public void ElseBranch_IsAttachedToTheStatement()
    {
        var target = new List<LuauStatement>();
        var elseBranch = Body("fallback");
        var chain = new IfChain(target, elseBranch);

        chain.Add(new Identifier("a"), Body("x"));

        var statement = Assert.IsType<IfStatement>(Assert.Single(target));
        Assert.Same(elseBranch, statement.ElseBranch);
    }

    [Fact]
    public void Renders_AsOneIfElseIfLadder()
    {
        var target = new List<LuauStatement>();
        var chain = new IfChain(target, Body("fallback"));

        chain.Add(new Identifier("a"), Body("x"));
        chain.Add(new Identifier("b"), Body("y"));

        Assert.Equal("if a then\n  x\nelseif b then\n  y\nelse\n  fallback\nend", Assert.Single(target).Render());
    }
}

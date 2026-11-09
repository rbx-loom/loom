using Loom.Luau.AST;

namespace Loom.Luau;

/// <summary>
///     Builds an <c>if / elseif</c> ladder one branch at a time, appending the statement to
///     <paramref name="target" /> when the first branch arrives. The statement is created before the
///     later branches are known - <see cref="IfStatement" /> picks its single-line rendering at
///     construction - so this must not be rewritten to collect branches first.
/// </summary>
public sealed class IfChain(List<LuauStatement> target, Chunk? elseBranch = null)
{
    private readonly List<ElseIfBranch> _elseIfBranches = [];
    private bool _started;

    public void Add(LuauExpression condition, Chunk branch)
    {
        if (_started)
        {
            _elseIfBranches.Add(new ElseIfBranch(condition, branch));
            return;
        }

        _started = true;
        target.Add(new IfStatement(condition, branch, _elseIfBranches, elseBranch));
    }
}

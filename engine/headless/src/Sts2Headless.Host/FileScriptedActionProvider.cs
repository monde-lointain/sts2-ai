using System.Collections.Immutable;
using System.Globalization;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Host;

/// <summary>
/// File-driven <see cref="IScriptedActionProvider"/>. Reads the script file
/// once at construction; each <see cref="NextAction"/> call consumes one
/// directive in order.
///
/// <para>
/// <b>Script format (line-based, UTF-8):</b>
/// </para>
/// <list type="bullet">
///   <item><c>play &lt;cardModelId&gt; [target=&lt;enemyId&gt;]</c> — play the first
///         card in hand matching the canonical card-model id (e.g.
///         <c>StrikeSilent</c>). Optional <c>target=&lt;n&gt;</c> picks the
///         enemy id; for <c>AnyEnemy</c> cards the first living enemy is
///         used when omitted.</item>
///   <item><c>end_turn</c> — emit <see cref="PlayerAction.EndTurn.Instance"/>.</item>
///   <item>Lines starting with <c>#</c> or blank lines are ignored
///         (comments / spacing).</item>
/// </list>
///
/// <para>
/// <b>Error handling:</b> the parser is strict — unknown directives, malformed
/// fields, or a directive that can't be matched to any legal action surface a
/// <see cref="ScriptParseException"/>. This keeps script-authoring honest.
/// </para>
///
/// <para>
/// <b>Position semantics:</b> the provider holds a monotonic line cursor.
/// When the cursor passes the last directive, <see cref="NextAction"/>
/// returns <c>null</c>, which the main loop interprets as "script exhausted".
/// </para>
/// </summary>
public sealed class FileScriptedActionProvider : IScriptedActionProvider
{
    private readonly IReadOnlyList<ScriptDirective> _directives;
    private readonly CardCatalog _cards;
    private int _cursor;

    /// <summary>Number of directives parsed (incl. those already consumed).</summary>
    public int DirectiveCount => _directives.Count;

    /// <summary>Index of the next directive to be consumed (0-based).</summary>
    public int NextIndex => _cursor;

    /// <summary>
    /// Load the script and resolve card-model lookups against
    /// <paramref name="cards"/>. The catalog reference is held so directive →
    /// legal-action translation can match by <c>CardModel.Id</c>.
    /// </summary>
    public FileScriptedActionProvider(string path, CardCatalog cards)
        : this(ReadAllLines(path), cards) { }

    /// <summary>Constructor for in-memory script content (used by tests).</summary>
    public FileScriptedActionProvider(IEnumerable<string> scriptLines, CardCatalog cards)
    {
        ArgumentNullException.ThrowIfNull(scriptLines);
        ArgumentNullException.ThrowIfNull(cards);
        _cards = cards;
        _directives = Parse(scriptLines);
        _cursor = 0;
    }

    /// <inheritdoc/>
    public PlayerAction? NextAction(CombatState state, ImmutableArray<PlayerAction> legal)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_cursor >= _directives.Count)
            return null;
        ScriptDirective d = _directives[_cursor];
        _cursor++;
        return Resolve(d, state, legal);
    }

    // === Resolution ========================================================

    private PlayerAction Resolve(
        ScriptDirective d,
        CombatState state,
        ImmutableArray<PlayerAction> legal
    )
    {
        switch (d.Kind)
        {
            case ScriptDirectiveKind.EndTurn:
            {
                var endTurn = legal.OfType<PlayerAction.EndTurn>().FirstOrDefault();
                if (endTurn is null)
                {
                    throw new ScriptParseException(
                        $"line {d.Line}: end_turn directive issued but EndTurn is not legal in phase {state.Phase}."
                    );
                }
                return endTurn;
            }
            case ScriptDirectiveKind.Play:
            {
                string cardId = d.CardModelId!;
                if (!_cards.Contains(cardId))
                {
                    throw new ScriptParseException(
                        $"line {d.Line}: unknown card model id '{cardId}'."
                    );
                }
                // Find the legal PlayCard for THIS card model.
                var candidates = new List<PlayerAction.PlayCard>();
                foreach (PlayerAction a in legal)
                {
                    if (a is PlayerAction.PlayCard pc)
                    {
                        CardInstance? inst = state.HandPile.Cards.FirstOrDefault(c =>
                            c.InstanceId == pc.CardInstanceId
                        );
                        if (inst is null)
                            continue;
                        if (inst.ModelId == cardId)
                        {
                            candidates.Add(pc);
                        }
                    }
                }
                if (candidates.Count == 0)
                {
                    throw new ScriptParseException(
                        $"line {d.Line}: no legal play for card '{cardId}' in current hand "
                            + $"(hand: {string.Join(",", state.HandPile.Cards.Select(c => c.ModelId))})."
                    );
                }
                // Pick a candidate. If target= specified, prefer the matching one.
                // Otherwise (or if no match), pick the first.
                if (d.TargetEnemyId.HasValue)
                {
                    var matched = candidates.FirstOrDefault(pc =>
                        pc.TargetEnemyId == d.TargetEnemyId.Value
                    );
                    if (matched is null)
                    {
                        throw new ScriptParseException(
                            $"line {d.Line}: card '{cardId}' target={d.TargetEnemyId.Value} not legal "
                                + $"(legal targets: {string.Join(",", candidates.Select(c => c.TargetEnemyId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "self"))})."
                        );
                    }
                    return matched;
                }
                return candidates[0];
            }
            default:
                throw new ScriptParseException($"line {d.Line}: unknown directive kind {d.Kind}.");
        }
    }

    // === Parser ============================================================

    private static IReadOnlyList<string> ReadAllLines(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("path must be non-empty.", nameof(path));
        }
        if (!File.Exists(path))
        {
            throw new ScriptParseException($"script file not found: {path}");
        }
        return File.ReadAllLines(path);
    }

    private static IReadOnlyList<ScriptDirective> Parse(IEnumerable<string> lines)
    {
        var list = new List<ScriptDirective>();
        int n = 0;
        foreach (string rawLine in lines)
        {
            n++;
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith('#'))
                continue;

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            switch (tokens[0])
            {
                case "end_turn":
                    if (tokens.Length != 1)
                    {
                        throw new ScriptParseException(
                            $"line {n}: end_turn takes no arguments (got '{line}')."
                        );
                    }
                    list.Add(new ScriptDirective(ScriptDirectiveKind.EndTurn, null, null, n));
                    break;
                case "play":
                    if (tokens.Length < 2)
                    {
                        throw new ScriptParseException($"line {n}: 'play' requires a card id.");
                    }
                    string cardId = tokens[1];
                    uint? targetId = null;
                    for (int i = 2; i < tokens.Length; i++)
                    {
                        string kv = tokens[i];
                        if (kv.StartsWith("target=", StringComparison.Ordinal))
                        {
                            string value = kv["target=".Length..];
                            if (
                                !uint.TryParse(
                                    value,
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out uint tid
                                )
                            )
                            {
                                throw new ScriptParseException(
                                    $"line {n}: target= expected unsigned integer, got '{value}'."
                                );
                            }
                            targetId = tid;
                        }
                        else
                        {
                            throw new ScriptParseException(
                                $"line {n}: unknown play option '{kv}'."
                            );
                        }
                    }
                    list.Add(new ScriptDirective(ScriptDirectiveKind.Play, cardId, targetId, n));
                    break;
                default:
                    throw new ScriptParseException($"line {n}: unknown directive '{tokens[0]}'.");
            }
        }
        return list;
    }

    private enum ScriptDirectiveKind
    {
        Play,
        EndTurn,
    }

    private sealed record ScriptDirective(
        ScriptDirectiveKind Kind,
        string? CardModelId,
        uint? TargetEnemyId,
        int Line
    );
}

/// <summary>
/// Raised by <see cref="FileScriptedActionProvider"/> on malformed script
/// input or directives that can't be matched to a legal action.
/// </summary>
public sealed class ScriptParseException : Exception
{
    public ScriptParseException() { }

    public ScriptParseException(string message)
        : base(message) { }

    public ScriptParseException(string message, Exception inner)
        : base(message, inner) { }
}

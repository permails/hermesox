using System.Text;
using System.Text.RegularExpressions;

namespace HermesDesktop.Core.Markdown;

public enum MathTextStyle
{
    Normal,
    Italic,
    Bold,
    Roman,
    BlackboardBold,
    Monospace
}

public abstract record MathNode;

public sealed record MathSequenceNode(IReadOnlyList<MathNode> Items) : MathNode;

public sealed record MathTextNode(string Text, MathTextStyle Style = MathTextStyle.Normal) : MathNode;

public sealed record MathFractionNode(MathNode Numerator, MathNode Denominator, bool HasBar = true) : MathNode;

public sealed record MathRootNode(MathNode? Degree, MathNode Radicand) : MathNode;

public sealed record MathScriptNode(MathNode Base, MathNode? Subscript, MathNode? Superscript) : MathNode;

public sealed record MathAccentNode(MathNode Base, string AccentKind) : MathNode;

public sealed record MathDelimitedNode(string Left, string Right, MathNode Body) : MathNode;

public sealed record MathMatrixNode(
    string LeftDelimiter,
    string RightDelimiter,
    IReadOnlyList<IReadOnlyList<MathNode>> Rows,
    bool IsLeftAligned = false) : MathNode;

public sealed record MathStackNode(MathNode Top, MathNode Bottom) : MathNode;

public static class LatexMathEngine
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        // Lowercase Greek
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ϵ", ["varepsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η",
        ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ",
        ["varkappa"] = "ϰ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["omicron"] = "ο", ["pi"] = "π", ["varpi"] = "ϖ",
        ["rho"] = "ρ", ["varrho"] = "ϱ", ["sigma"] = "σ", ["varsigma"] = "ς",
        ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "ϕ", ["varphi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",

        // Uppercase Greek
        ["Alpha"] = "Α", ["Beta"] = "Β", ["Gamma"] = "Γ", ["Delta"] = "Δ",
        ["Epsilon"] = "Ε", ["Zeta"] = "Ζ", ["Eta"] = "Η", ["Theta"] = "Θ",
        ["Iota"] = "Ι", ["Kappa"] = "Κ", ["Lambda"] = "Λ", ["Mu"] = "Μ",
        ["Nu"] = "Ν", ["Xi"] = "Ξ", ["Omicron"] = "Ο", ["Pi"] = "Π",
        ["Rho"] = "Ρ", ["Sigma"] = "Σ", ["Tau"] = "Τ", ["Upsilon"] = "Υ",
        ["Phi"] = "Φ", ["Chi"] = "Χ", ["Psi"] = "Ψ", ["Omega"] = "Ω",

        // Physics constants, operators, units & geometry
        ["hbar"] = "ℏ", ["hslash"] = "ℏ", ["ell"] = "ℓ", ["nabla"] = "∇",
        ["partial"] = "∂", ["infty"] = "∞", ["aleph"] = "ℵ", ["beth"] = "ℶ",
        ["Re"] = "ℜ", ["Im"] = "ℑ", ["wp"] = "℘", ["mho"] = "℧",
        ["degree"] = "°", ["circ"] = "∘", ["celsius"] = "℃", ["angstrom"] = "Å",
        ["AA"] = "Å", ["ohm"] = "Ω", ["micro"] = "μ",
        ["angle"] = "∠", ["measuredangle"] = "∡", ["sphericalangle"] = "∢",
        ["triangle"] = "△", ["triangledown"] = "▽", ["square"] = "□", ["Box"] = "□",
        ["diamond"] = "◇", ["perp"] = "⊥", ["parallel"] = "∥", ["nparallel"] = "∦",
        ["prime"] = "′", ["backprime"] = "‵",

        // Big operators
        ["sum"] = "∑", ["prod"] = "∏", ["coprod"] = "∐",
        ["int"] = "∫", ["iint"] = "∬", ["iiint"] = "∭", ["iiiint"] = "⨌",
        ["oint"] = "∮", ["oiint"] = "∯", ["oiiint"] = "∰",
        ["bigcup"] = "⋃", ["bigcap"] = "⋂", ["bigsqcup"] = "⨆",
        ["bigvee"] = "⋁", ["bigwedge"] = "⋀", ["bigoplus"] = "⊕", ["bigotimes"] = "⊗",

        // Relations & comparisons
        ["equiv"] = "≡", ["nequiv"] = "≢", ["approx"] = "≈", ["cong"] = "≅",
        ["ncong"] = "≇", ["sim"] = "∼", ["simeq"] = "≃", ["asymp"] = "≍",
        ["propto"] = "∝", ["neq"] = "≠", ["ne"] = "≠", ["le"] = "≤", ["leq"] = "≤",
        ["leqq"] = "≦", ["ge"] = "≥", ["geq"] = "≥", ["geqq"] = "≧",
        ["ll"] = "≪", ["gg"] = "≫", ["lll"] = "⋘", ["ggg"] = "⋙",
        ["prec"] = "≺", ["succ"] = "≻", ["preceq"] = "≼", ["succeq"] = "≽",
        ["doteq"] = "≐", ["triangleq"] = "≜", ["coloneqq"] = "≔", ["eqqcolon"] = "≕",

        // Sets & logic
        ["in"] = "∈", ["notin"] = "∉", ["ni"] = "∋", ["owns"] = "∋",
        ["subset"] = "⊂", ["subseteq"] = "⊆", ["subsetneq"] = "⊊", ["nsubseteq"] = "⊈",
        ["supset"] = "⊃", ["supseteq"] = "⊇", ["supsetneq"] = "⊋", ["nsupseteq"] = "⊉",
        ["cup"] = "∪", ["cap"] = "∩", ["sqcup"] = "⊔", ["sqcap"] = "⊓",
        ["setminus"] = "∖", ["smallsetminus"] = "∖",
        ["emptyset"] = "∅", ["varnothing"] = "∅",
        ["forall"] = "∀", ["exists"] = "∃", ["nexists"] = "∄",
        ["neg"] = "¬", ["lnot"] = "¬", ["land"] = "∧", ["wedge"] = "∧",
        ["lor"] = "∨", ["vee"] = "∨", ["oplus"] = "⊕", ["ominus"] = "⊖",
        ["otimes"] = "⊗", ["oslash"] = "⊘", ["odot"] = "⊙",
        ["therefore"] = "∴", ["because"] = "∵", ["top"] = "⊤", ["bot"] = "⊥",
        ["vdash"] = "⊢", ["dashv"] = "⊣", ["models"] = "⊨", ["vDash"] = "⊨",
        ["implies"] = "⟹", ["iff"] = "⟺",

        // Binary operators & punctuation
        ["pm"] = "±", ["mp"] = "∓", ["times"] = "×", ["div"] = "÷",
        ["cdot"] = "·", ["bullet"] = "•", ["star"] = "⋆", ["ast"] = "∗",
        ["dagger"] = "†", ["dag"] = "†", ["ddagger"] = "‡", ["ddag"] = "‡",
        ["amalg"] = "⨿", ["wr"] = "≀", ["bowtie"] = "⋈",
        ["cdots"] = "⋯", ["ldots"] = "…", ["dots"] = "…", ["vdots"] = "⋮", ["ddots"] = "⋱",

        // Arrows
        ["to"] = "→", ["rightarrow"] = "→", ["gets"] = "←", ["leftarrow"] = "←",
        ["leftrightarrow"] = "↔", ["longrightarrow"] = "⟶", ["longleftarrow"] = "⟵",
        ["longleftrightarrow"] = "⟷",
        ["Rightarrow"] = "⇒", ["Leftarrow"] = "⇐", ["Leftrightarrow"] = "⇔",
        ["Longrightarrow"] = "⟹", ["Longleftarrow"] = "⟸", ["Longleftrightarrow"] = "⟺",
        ["mapsto"] = "↦", ["longmapsto"] = "⟼",
        ["uparrow"] = "↑", ["downarrow"] = "↓", ["updownarrow"] = "↕",
        ["Uparrow"] = "⇑", ["Downarrow"] = "⇓", ["Updownarrow"] = "⇕",
        ["nearrow"] = "↗", ["searrow"] = "↘", ["swarrow"] = "↙", ["nwarrow"] = "↖",
        ["rightleftharpoons"] = "⇌", ["leftharpoons"] = "⇋",
        ["hookrightarrow"] = "↪", ["hookleftarrow"] = "↩",

        // Delimiters
        ["langle"] = "⟨", ["rangle"] = "⟩",
        ["lfloor"] = "⌊", ["rfloor"] = "⌋",
        ["lceil"] = "⌈", ["rceil"] = "⌉",
        ["vert"] = "|", ["lvert"] = "|", ["rvert"] = "|", ["mid"] = "∣",
        ["Vert"] = "‖", ["lVert"] = "‖", ["rVert"] = "‖",
        ["lbrace"] = "{", ["rbrace"] = "}", ["backslash"] = "\\",

        // Whitespace
        [","] = "\u2009", [":"] = "\u2005", [";"] = "\u2004", [" "] = " ",
        ["quad"] = "\u2001", ["qquad"] = "\u2001\u2001", ["!"] = ""
    };

    private static readonly HashSet<string> RomanFunctions = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "cot", "sec", "csc",
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh", "coth",
        "ln", "log", "lg", "exp", "lim", "limsup", "liminf",
        "sup", "inf", "max", "min", "arg", "det", "dim", "ker",
        "deg", "gcd", "lcm", "hom", "Pr", "mod", "bmod", "pmod",
        "tr", "Tr", "rank", "span", "grad", "div", "curl", "rot"
    };

    private static readonly Dictionary<char, char> SuperscriptMap = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
        ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
        ['n'] = 'ⁿ', ['i'] = 'ⁱ', ['a'] = 'ᵃ', ['b'] = 'ᵇ', ['c'] = 'ᶜ',
        ['d'] = 'ᵈ', ['e'] = 'ᵉ', ['f'] = 'ᶠ', ['g'] = 'ᵍ', ['h'] = 'ʰ',
        ['j'] = 'ʲ', ['k'] = 'ᵏ', ['l'] = 'ˡ', ['m'] = 'ᵐ', ['o'] = 'ᵒ',
        ['p'] = 'ᵖ', ['r'] = 'ʳ', ['s'] = 'ˢ', ['t'] = 'ᵗ', ['u'] = 'ᵘ',
        ['v'] = 'ᵛ', ['w'] = 'ʷ', ['x'] = 'ˣ', ['y'] = 'ʸ', ['z'] = 'ᶻ',
        ['T'] = 'ᵀ'
    };

    private static readonly Dictionary<char, char> SubscriptMap = new()
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
        ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ', ['j'] = 'ⱼ',
        ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ', ['o'] = 'ₒ',
        ['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ',
        ['v'] = 'ᵥ', ['x'] = 'ₓ'
    };

    private static readonly Dictionary<char, char> BlackboardMap = new()
    {
        ['C'] = 'ℂ', ['H'] = 'ℍ', ['N'] = 'ℕ', ['P'] = 'ℙ',
        ['Q'] = 'ℚ', ['R'] = 'ℝ', ['Z'] = 'ℤ'
    };

    public static MathNode Parse(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return new MathTextNode("");

        var p = new Parser(latex.Trim());
        return p.ParseExpression(stopAt: null);
    }

    public static bool RequiresTwoDimensionalLayout(MathNode node) => node switch
    {
        MathFractionNode or MathRootNode or MathMatrixNode or MathAccentNode or MathStackNode => true,
        MathDelimitedNode d => RequiresTwoDimensionalLayout(d.Body),
        MathScriptNode s => RequiresTwoDimensionalLayout(s.Base)
            || (s.Subscript is not null && !CanRenderScriptInline(s.Subscript, isSuper: false))
            || (s.Superscript is not null && !CanRenderScriptInline(s.Superscript, isSuper: true)),
        MathSequenceNode seq => seq.Items.Any(RequiresTwoDimensionalLayout),
        _ => false
    };

    public static bool TryFormatSimpleInline(MathNode node, out string plainText)
    {
        if (RequiresTwoDimensionalLayout(node))
        {
            plainText = string.Empty;
            return false;
        }
        var sb = new StringBuilder();
        if (!AppendSimpleInline(node, sb))
        {
            plainText = string.Empty;
            return false;
        }
        plainText = sb.ToString();
        return true;
    }

    private static bool CanRenderScriptInline(MathNode node, bool isSuper)
    {
        var sb = new StringBuilder();
        if (!AppendSimpleInline(node, sb)) return false;
        var text = sb.ToString();
        if (text.Length is 0 or > 6) return false;
        var map = isSuper ? SuperscriptMap : SubscriptMap;
        foreach (var ch in text)
        {
            if (ch == ' ') continue;
            if (!map.ContainsKey(ch)) return false;
        }
        return true;
    }

    private static bool AppendSimpleInline(MathNode node, StringBuilder sb)
    {
        switch (node)
        {
            case MathTextNode t:
                sb.Append(t.Text);
                return true;
            case MathSequenceNode seq:
                foreach (var item in seq.Items)
                {
                    if (!AppendSimpleInline(item, sb)) return false;
                }
                return true;
            case MathDelimitedNode d:
                if (!string.IsNullOrEmpty(d.Left) && d.Left != ".") sb.Append(d.Left);
                if (!AppendSimpleInline(d.Body, sb)) return false;
                if (!string.IsNullOrEmpty(d.Right) && d.Right != ".") sb.Append(d.Right);
                return true;
            case MathScriptNode s:
                if (!AppendSimpleInline(s.Base, sb)) return false;
                if (s.Subscript is not null)
                {
                    var subSb = new StringBuilder();
                    if (!AppendSimpleInline(s.Subscript, subSb)) return false;
                    foreach (var ch in subSb.ToString())
                    {
                        if (ch == ' ') continue;
                        if (!SubscriptMap.TryGetValue(ch, out var mapped)) return false;
                        sb.Append(mapped);
                    }
                }
                if (s.Superscript is not null)
                {
                    var supSb = new StringBuilder();
                    if (!AppendSimpleInline(s.Superscript, supSb)) return false;
                    foreach (var ch in supSb.ToString())
                    {
                        if (ch == ' ') continue;
                        if (!SuperscriptMap.TryGetValue(ch, out var mapped)) return false;
                        sb.Append(mapped);
                    }
                }
                return true;
            default:
                return false;
        }
    }

    public static bool IsBigOperator(MathNode node) =>
        node is MathTextNode t && t.Text is "∑" or "∏" or "∐" or "∫" or "∬" or "∭" or "⨌" or "∮" or "∯" or "∰" or "⋃" or "⋂" or "lim" or "sup" or "inf" or "max" or "min";

    public static bool IsUnderscoreLimitOperator(MathNode node) =>
        node is MathTextNode t && t.Text is "∑" or "∏" or "∐" or "⋃" or "⋂" or "lim" or "sup" or "inf" or "max" or "min";

    public static bool IsInlineDollarOpeningAllowed(string text, int index)
    {
        if (index + 1 >= text.Length) return false;
        if (text[index + 1] == '$') return false;
        if (index > 0 && text[index - 1] == '\\') return false;
        var next = text[index + 1];
        if (char.IsWhiteSpace(next)) return false;
        return true;
    }

    public static int FindClosingInlineDollar(string text, int openIndex)
    {
        for (var i = openIndex + 1; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\n' || ch == '\r') return -1;
            if (ch == '\\')
            {
                i++; // skip escaped char
                continue;
            }
            if (ch == '$' && (i + 1 >= text.Length || text[i + 1] != '$'))
            {
                if (i == openIndex + 1) return -1;
                if (char.IsWhiteSpace(text[i - 1])) continue;
                var inner = text[(openIndex + 1)..i];
                if (LooksLikePlainCurrency(inner, text, i)) return -1;
                return i;
            }
        }
        return -1;
    }

    private static bool LooksLikePlainCurrency(string inner, string full, int closeIdx)
    {
        if (Regex.IsMatch(inner, @"^\d+(?:[.,]\d+)?\s*(?:k|m|b|USD|RMB|CNY)?$", RegexOptions.IgnoreCase))
            return true;
        // e.g. "$100 to $200" -> inner starts with digits and contains spaces + English words without TeX commands
        if (inner.Length > 0 && char.IsDigit(inner[0]) && !inner.Contains('\\') && !inner.Contains('^') && !inner.Contains('_') && !inner.Contains('='))
            return closeIdx + 1 < full.Length && char.IsDigit(full[closeIdx + 1]);
        return false;
    }

    private sealed class Parser(string src)
    {
        private int _pos;

        private bool IsAtEnd => _pos >= src.Length;
        private char Peek => IsAtEnd ? '\0' : src[_pos];

        public MathNode ParseExpression(string? stopAt = null)
        {
            var items = new List<MathNode>();
            while (!IsAtEnd)
            {
                SkipSpaces();
                if (IsAtEnd) break;
                if (stopAt is not null && src.AsSpan(_pos).StartsWith(stopAt, StringComparison.Ordinal))
                    break;
                if (Peek == '}' || Peek == '&' || src.AsSpan(_pos).StartsWith("\\\\", StringComparison.Ordinal))
                    break;
                if (src.AsSpan(_pos).StartsWith(@"\right", StringComparison.Ordinal))
                    break;
                if (src.AsSpan(_pos).StartsWith(@"\end{", StringComparison.Ordinal))
                    break;

                var atom = ParseAtomWithScripts();
                if (atom is not null) items.Add(atom);
            }

            return items.Count switch
            {
                0 => new MathTextNode(""),
                1 => items[0],
                _ => new MathSequenceNode(items)
            };
        }

        private MathNode? ParseAtomWithScripts()
        {
            var baseNode = ParseAtom();
            if (baseNode is null) return null;

            MathNode? sub = null;
            MathNode? sup = null;

            while (true)
            {
                SkipSpaces();
                if (Peek == '_' && sub is null)
                {
                    _pos++;
                    sub = ParseRequiredArgument();
                }
                else if (Peek == '^' && sup is null)
                {
                    _pos++;
                    sup = ParseRequiredArgument();
                }
                else
                {
                    break;
                }
            }

            return sub is not null || sup is not null
                ? new MathScriptNode(baseNode, sub, sup)
                : baseNode;
        }

        private MathNode? ParseAtom()
        {
            SkipSpaces();
            if (IsAtEnd) return null;

            var ch = Peek;
            if (ch == '{')
            {
                _pos++;
                var inner = ParseExpression();
                if (Peek == '}') _pos++;
                return inner;
            }

            if (ch == '\\')
            {
                return ParseCommand();
            }

            if (char.IsLetter(ch))
            {
                _pos++;
                return new MathTextNode(ch.ToString(), MathTextStyle.Italic);
            }

            if (char.IsDigit(ch))
            {
                var start = _pos;
                while (!IsAtEnd && (char.IsDigit(Peek) || (Peek == '.' && _pos + 1 < src.Length && char.IsDigit(src[_pos + 1]))))
                {
                    _pos++;
                }
                return new MathTextNode(src[start.._pos], MathTextStyle.Normal);
            }

            _pos++;
            return ch switch
            {
                '+' => new MathTextNode(" + ", MathTextStyle.Normal),
                '-' => new MathTextNode(" − ", MathTextStyle.Normal),
                '=' => new MathTextNode(" = ", MathTextStyle.Normal),
                '<' => new MathTextNode(" < ", MathTextStyle.Normal),
                '>' => new MathTextNode(" > ", MathTextStyle.Normal),
                ',' => new MathTextNode(", ", MathTextStyle.Normal),
                '*' => new MathTextNode(" · ", MathTextStyle.Normal),
                '\'' => new MathTextNode("′", MathTextStyle.Normal),
                _ => new MathTextNode(ch.ToString(), MathTextStyle.Normal)
            };
        }

        private MathNode ParseRequiredArgument()
        {
            SkipSpaces();
            if (IsAtEnd) return new MathTextNode("");
            if (Peek == '{')
            {
                _pos++;
                var inner = ParseExpression();
                if (Peek == '}') _pos++;
                return inner;
            }
            return ParseAtom() ?? new MathTextNode("");
        }

        private MathNode? ParseOptionalBracketArgument()
        {
            SkipSpaces();
            if (Peek != '[') return null;
            _pos++;
            var inner = ParseExpression(stopAt: "]");
            if (Peek == ']') _pos++;
            return inner;
        }

        private MathNode ParseCommand()
        {
            _pos++; // consume '\'
            if (IsAtEnd) return new MathTextNode("\\");

            var cmd = ReadCommandName();
            switch (cmd)
            {
                case "displaystyle":
                case "textstyle":
                case "scriptstyle":
                case "limits":
                case "nolimits":
                    return ParseAtomWithScripts() ?? new MathTextNode("");

                case "frac":
                case "dfrac":
                case "tfrac":
                case "cfrac":
                    return new MathFractionNode(ParseRequiredArgument(), ParseRequiredArgument(), HasBar: true);

                case "binom":
                case "dbinom":
                case "tbinom":
                    return new MathDelimitedNode("(", ")", new MathFractionNode(ParseRequiredArgument(), ParseRequiredArgument(), HasBar: false));

                case "sqrt":
                {
                    var deg = ParseOptionalBracketArgument();
                    var rad = ParseRequiredArgument();
                    return new MathRootNode(deg, rad);
                }

                case "vec":
                case "overrightarrow":
                    return new MathAccentNode(ParseRequiredArgument(), "vec");
                case "hat":
                case "widehat":
                    return new MathAccentNode(ParseRequiredArgument(), "hat");
                case "bar":
                case "overline":
                    return new MathAccentNode(ParseRequiredArgument(), "bar");
                case "tilde":
                case "widetilde":
                    return new MathAccentNode(ParseRequiredArgument(), "tilde");
                case "dot":
                    return new MathAccentNode(ParseRequiredArgument(), "dot");
                case "ddot":
                    return new MathAccentNode(ParseRequiredArgument(), "ddot");
                case "underline":
                    return new MathAccentNode(ParseRequiredArgument(), "underline");

                case "ket":
                    return new MathDelimitedNode("|", "⟩", ParseRequiredArgument());
                case "bra":
                    return new MathDelimitedNode("⟨", "|", ParseRequiredArgument());
                case "braket":
                    return new MathDelimitedNode("⟨", "⟩", ParseRequiredArgument());
                case "abs":
                    return new MathDelimitedNode("|", "|", ParseRequiredArgument());
                case "norm":
                    return new MathDelimitedNode("‖", "‖", ParseRequiredArgument());

                case "dv":
                {
                    var opt = ParseOptionalBracketArgument();
                    var y = ParseRequiredArgument();
                    var x = ParseRequiredArgument();
                    MathNode num = opt is null
                        ? new MathSequenceNode([new MathTextNode("d", MathTextStyle.Roman), y])
                        : new MathSequenceNode([new MathScriptNode(new MathTextNode("d", MathTextStyle.Roman), null, opt), y]);
                    MathNode den = opt is null
                        ? new MathSequenceNode([new MathTextNode("d", MathTextStyle.Roman), x])
                        : new MathScriptNode(new MathSequenceNode([new MathTextNode("d", MathTextStyle.Roman), x]), null, opt);
                    return new MathFractionNode(num, den, HasBar: true);
                }

                case "pdv":
                {
                    var opt = ParseOptionalBracketArgument();
                    var y = ParseRequiredArgument();
                    var x = ParseRequiredArgument();
                    MathNode num = opt is null
                        ? new MathSequenceNode([new MathTextNode("∂", MathTextStyle.Normal), y])
                        : new MathSequenceNode([new MathScriptNode(new MathTextNode("∂", MathTextStyle.Normal), null, opt), y]);
                    MathNode den = opt is null
                        ? new MathSequenceNode([new MathTextNode("∂", MathTextStyle.Normal), x])
                        : new MathScriptNode(new MathSequenceNode([new MathTextNode("∂", MathTextStyle.Normal), x]), null, opt);
                    return new MathFractionNode(num, den, HasBar: true);
                }

                case "overset":
                case "stackrel":
                    return new MathStackNode(ParseRequiredArgument(), ParseRequiredArgument());

                case "underset":
                {
                    var bottom = ParseRequiredArgument();
                    var top = ParseRequiredArgument();
                    return new MathStackNode(top, bottom);
                }

                case "left":
                {
                    var leftDelim = ReadDelimiterToken();
                    var body = ParseExpression();
                    var rightDelim = "";
                    if (src.AsSpan(_pos).StartsWith(@"\right", StringComparison.Ordinal))
                    {
                        _pos += @"\right".Length;
                        rightDelim = ReadDelimiterToken();
                    }
                    return new MathDelimitedNode(leftDelim, rightDelim, body);
                }

                case "text":
                case "textrm":
                case "textup":
                case "mbox":
                    return new MathTextNode(ReadRawGroupText(), MathTextStyle.Roman);

                case "mathrm":
                case "operatorname":
                case "unit":
                    return new MathTextNode(FormatStyledText(ReadRawGroupText()), MathTextStyle.Roman);

                case "SI":
                {
                    var val = ReadRawGroupText();
                    var unit = FormatStyledText(ReadRawGroupText());
                    return new MathTextNode($"{val}\u2009{unit}", MathTextStyle.Roman);
                }

                case "mathbf":
                case "boldsymbol":
                case "bm":
                {
                    var inner = ParseRequiredArgument();
                    return FlattenWithStyle(inner, MathTextStyle.Bold);
                }

                case "mathbb":
                {
                    var raw = ReadRawGroupText();
                    var sb = new StringBuilder();
                    foreach (var c in raw)
                    {
                        sb.Append(BlackboardMap.TryGetValue(c, out var bb) ? bb : c);
                    }
                    return new MathTextNode(sb.ToString(), MathTextStyle.Bold);
                }

                case "mathit":
                    return FlattenWithStyle(ParseRequiredArgument(), MathTextStyle.Italic);

                case "texttt":
                    return new MathTextNode(ReadRawGroupText(), MathTextStyle.Monospace);

                case "begin":
                    return ParseEnvironment();

                case "big":
                case "Big":
                case "bigg":
                case "Bigg":
                case "bigl":
                case "bigr":
                case "Bigl":
                case "Bigr":
                    return new MathTextNode(ReadDelimiterToken(), MathTextStyle.Normal);
            }

            if (Symbols.TryGetValue(cmd, out var sym))
            {
                var spaced = cmd is "approx" or "equiv" or "cong" or "sim" or "simeq" or "propto"
                    or "neq" or "ne" or "le" or "leq" or "ge" or "geq" or "ll" or "gg"
                    or "in" or "notin" or "subset" or "subseteq" or "supset" or "supseteq"
                    or "to" or "rightarrow" or "leftarrow" or "leftrightarrow"
                    or "Rightarrow" or "Leftarrow" or "Leftrightarrow" or "implies" or "iff" or "mapsto"
                    or "pm" or "mp" or "times" or "div" or "cdot" or "oplus" or "otimes"
                    or "cup" or "cap" or "land" or "lor"
                    ? $" {sym} "
                    : sym;
                return new MathTextNode(spaced, MathTextStyle.Normal);
            }

            if (RomanFunctions.Contains(cmd))
            {
                return new MathTextNode(cmd, MathTextStyle.Roman);
            }

            return new MathTextNode($"\\{cmd}", MathTextStyle.Normal);
        }

        private MathNode ParseEnvironment()
        {
            var env = ReadRawGroupText().Trim();
            var (left, right, isLeftAligned) = env switch
            {
                "pmatrix" => ("(", ")", false),
                "bmatrix" => ("[", "]", false),
                "Bmatrix" => ("{", "}", false),
                "vmatrix" => ("|", "|", false),
                "Vmatrix" => ("‖", "‖", false),
                "cases" or "dcases" => ("{", "", true),
                "align" or "align*" or "aligned" or "gather" or "gather*" or "split" or "equation" or "equation*" => ("", "", true),
                _ => ("", "", false)
            };

            if (env == "array")
            {
                _ = ReadRawGroupText(); // alignment spec like {cc}
            }

            var rows = new List<IReadOnlyList<MathNode>>();
            var currentRow = new List<MathNode>();

            while (!IsAtEnd)
            {
                SkipSpaces();
                if (src.AsSpan(_pos).StartsWith(@"\end{", StringComparison.Ordinal))
                {
                    _pos += @"\end".Length;
                    _ = ReadRawGroupText();
                    break;
                }

                var cell = ParseExpression();
                currentRow.Add(cell);

                if (Peek == '&')
                {
                    _pos++;
                    continue;
                }

                if (src.AsSpan(_pos).StartsWith("\\\\", StringComparison.Ordinal))
                {
                    _pos += 2;
                    _ = ParseOptionalBracketArgument(); // optional \\[4pt]
                    rows.Add(currentRow);
                    currentRow = [];
                    continue;
                }

                break;
            }

            if (currentRow.Count > 0)
            {
                rows.Add(currentRow);
            }

            if (rows.Count == 0) rows.Add([new MathTextNode("")]);
            return new MathMatrixNode(left, right, rows, isLeftAligned);
        }

        private string ReadCommandName()
        {
            if (IsAtEnd) return "";
            var ch = src[_pos];
            if (!char.IsLetter(ch))
            {
                _pos++;
                return ch.ToString();
            }
            var start = _pos;
            while (!IsAtEnd && (char.IsLetter(src[_pos]) || src[_pos] == '*')) _pos++;
            return src[start.._pos];
        }

        private string ReadDelimiterToken()
        {
            SkipSpaces();
            if (IsAtEnd) return "";
            if (Peek == '.') { _pos++; return ""; }
            if (Peek == '\\')
            {
                _pos++;
                var name = ReadCommandName();
                return Symbols.TryGetValue(name, out var s) ? s : name;
            }
            var c = Peek;
            _pos++;
            return c.ToString();
        }

        private string ReadRawGroupText()
        {
            SkipSpaces();
            if (IsAtEnd) return "";
            if (Peek != '{')
            {
                var c = src[_pos++];
                return c.ToString();
            }

            _pos++; // '{'
            var depth = 1;
            var start = _pos;
            while (!IsAtEnd && depth > 0)
            {
                if (src[_pos] == '\\' && _pos + 1 < src.Length)
                {
                    _pos += 2;
                    continue;
                }
                if (src[_pos] == '{') depth++;
                else if (src[_pos] == '}') depth--;
                if (depth > 0) _pos++;
            }
            var raw = src[start.._pos];
            if (Peek == '}') _pos++;
            return raw;
        }

        private static string FormatStyledText(string raw)
        {
            return raw
                .Replace("\\cdot", "·", StringComparison.Ordinal)
                .Replace("\\times", "×", StringComparison.Ordinal)
                .Replace("\\Omega", "Ω", StringComparison.Ordinal)
                .Replace("\\mu", "μ", StringComparison.Ordinal)
                .Replace("\\degree", "°", StringComparison.Ordinal);
        }

        private static MathNode FlattenWithStyle(MathNode node, MathTextStyle style) => node switch
        {
            MathTextNode t => t with { Style = style },
            MathSequenceNode s => new MathSequenceNode(s.Items.Select(i => FlattenWithStyle(i, style)).ToList()),
            _ => node
        };

        private void SkipSpaces()
        {
            while (!IsAtEnd && char.IsWhiteSpace(src[_pos])) _pos++;
        }
    }
}

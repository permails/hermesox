using HermesDesktop.Core.Markdown;
using Xunit;

namespace HermesDesktop.Core.Tests;

public sealed class LatexMathEngineTests
{
    [Fact]
    public void Parse_PhysicsSchrodingerAndMaxwell_Produces2dAst()
    {
        var schrodinger = LatexMathEngine.Parse(@"i\hbar \frac{\partial \psi}{\partial t} = \hat{H} \psi");
        Assert.True(LatexMathEngine.RequiresTwoDimensionalLayout(schrodinger));
        var seq = Assert.IsType<MathSequenceNode>(schrodinger);
        Assert.Contains(seq.Items, n => n is MathTextNode { Text: "ℏ" });
        Assert.Contains(seq.Items, n => n is MathFractionNode);
        Assert.Contains(seq.Items, n => n is MathAccentNode { AccentKind: "hat" });

        var maxwell = LatexMathEngine.Parse(@"\nabla \times \vec{E} = -\pdv{\vec{B}}{t}");
        var mSeq = Assert.IsType<MathSequenceNode>(maxwell);
        Assert.Contains(mSeq.Items, n => n is MathTextNode { Text: "∇" });
        Assert.Contains(mSeq.Items, n => n is MathAccentNode { AccentKind: "vec" });
        Assert.Contains(mSeq.Items, n => n is MathFractionNode);
    }

    [Fact]
    public void Parse_QuantumBraKetMatrixAndSiUnits_AreSupported()
    {
        var braket = LatexMathEngine.Parse(@"\bra{\psi}\hat{H}\ket{\psi}");
        var seq = Assert.IsType<MathSequenceNode>(braket);
        Assert.Contains(seq.Items, n => n is MathDelimitedNode { Left: "⟨", Right: "|" });
        Assert.Contains(seq.Items, n => n is MathDelimitedNode { Left: "|", Right: "⟩" });

        var mat = LatexMathEngine.Parse(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}");
        var matrixNode = Assert.IsType<MathMatrixNode>(mat);
        Assert.Equal("(", matrixNode.LeftDelimiter);
        Assert.Equal(")", matrixNode.RightDelimiter);
        Assert.Equal(2, matrixNode.Rows.Count);
        Assert.Equal(2, matrixNode.Rows[0].Count);

        var si = LatexMathEngine.Parse(@"\SI{9.8}{m/s^2}");
        var siText = Assert.IsType<MathTextNode>(si);
        Assert.Contains("9.8", siText.Text);
        Assert.Contains("m/s^2", siText.Text);
    }

    [Fact]
    public void TryFormatSimpleInline_FormatsGreekAndSuperscriptsWithout2dBox()
    {
        var mc2 = LatexMathEngine.Parse("E = mc^2");
        Assert.True(LatexMathEngine.TryFormatSimpleInline(mc2, out var rendered));
        Assert.Contains("²", rendered);

        var omega = LatexMathEngine.Parse(@"\Delta \phi + \varphi = 2\pi");
        Assert.True(LatexMathEngine.TryFormatSimpleInline(omega, out var greek));
        Assert.Contains("Δ", greek);
        Assert.Contains("ϕ", greek);
        Assert.Contains("φ", greek);
        Assert.Contains("π", greek);
    }

    [Fact]
    public void InlineDollarGuard_AllowsMathButIgnoresCurrency()
    {
        const string currency = "Price is $100 and tax is $20 today.";
        var openIdx = currency.IndexOf('$');
        Assert.Equal(-1, LatexMathEngine.FindClosingInlineDollar(currency, openIdx));

        const string math = "Given $\\alpha + \\beta = \\gamma$, compute $x^2$.";
        var mOpen = math.IndexOf('$');
        Assert.True(LatexMathEngine.IsInlineDollarOpeningAllowed(math, mOpen));
        var mClose = LatexMathEngine.FindClosingInlineDollar(math, mOpen);
        Assert.True(mClose > mOpen);
    }

    [Fact]
    public void MarkdownBlockParser_ExtractsMathBlocksAndTaskLists()
    {
        const string md = """
            Newton's second law:
            $$
            \vec{F} = m\vec{a}
            $$

            \[
            \oint_{\partial \Sigma} \vec{E} \cdot d\vec{l} = -\frac{d\Phi_B}{dt}
            \]

            \begin{cases} x^2, & x \ge 0 \\ 0, & x < 0 \end{cases}

            - [x] Verified equations
            - [ ] Pending derivation
            """;

        var blocks = MarkdownBlockParser.Parse(md);
        Assert.Equal(3, blocks.Count(b => b.Kind == MarkdownBlockKind.MathBlock));
        var list = Assert.Single(blocks, b => b.Kind == MarkdownBlockKind.List);
        Assert.Equal(2, list.ListItems!.Count);
        Assert.True(list.ListItems[0].IsTask && list.ListItems[0].IsChecked);
        Assert.True(list.ListItems[1].IsTask && !list.ListItems[1].IsChecked);
    }

    [Fact]
    public void NormalizeTypography_ConvertsKeycapEmojisToCircledDigits()
    {
        const string input = "4\uFE0F\u20E3 高斯级优雅\n5\uFE0F\u20E3 彩蛋";
        var blocks = MarkdownBlockParser.Parse(input);
        Assert.Single(blocks);
        Assert.Contains("④ 高斯级优雅", blocks[0].Text);
        Assert.Contains("⑤ 彩蛋", blocks[0].Text);
    }
}

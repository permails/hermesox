using HermesDesktop.Core.Markdown;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class MarkdownBlockParserTests
{
    [Fact]
    public void Parse_EveryCharacterStreamingPrefix_CompletesWithoutHanging()
    {
        const string sample = """
            勘验完成 ✅ 以下是修正后的 Unit 1 + 整本：

            #
            ###
            ##### 五级标题
            #hashtagWithoutSpace

            | Word | Phonetic | Meaning |
            | --- | --- | --- |
            | die out | /daɪ aʊt/ | 灭绝 |
            | a type of | /ə taɪp əv/ | 一种 |

            > 引用段落
            - 列表项 1
            1. 有序列表

            ```python
            print("hello")
            ```

            ::preview{file="/home/pi/demo.html"}
            MEDIA:/home/pi/words.md
            """;

        for (var len = 1; len <= sample.Length; len++)
        {
            var slice = sample[..len];
            var blocks = MarkdownBlockParser.Parse(slice);
            Assert.NotNull(blocks);
        }
    }

    [Fact]
    public void Parse_ExtractsPreviewMediaTableAndHeadings()
    {
        const string md = """
            ### Title
            #tag
            | A | B |
            | --- | --- |
            | 1 | 2 |

            ::preview{file="/tmp/test.html"}
            MEDIA:/tmp/out.md
            """;

        var blocks = MarkdownBlockParser.Parse(md);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Heading && b.HeadingLevel == 3 && b.Text == "Title");
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Paragraph && b.Text == "#tag");
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Table && b.TableRows?.Count == 1);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.PreviewFile && b.Text == "/tmp/test.html");
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.PreviewFile && b.Text == "/tmp/out.md");
    }

    [Fact]
    public void Parse_ExtractsMediaImageAndMarkdownImageBlocks()
    {
        const string md = """
            Here are the images:
            MEDIA:/tmp/diagram.png
            ![triangle diagram](/home/pi/plots/bar.jpg)
            Trailing text.
            """;

        var blocks = MarkdownBlockParser.Parse(md);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Paragraph && b.Text == "Here are the images:");
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Image && b.Text == "/tmp/diagram.png");
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Image && b.Text == "/home/pi/plots/bar.jpg");
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Paragraph && b.Text == "Trailing text.");
    }
}

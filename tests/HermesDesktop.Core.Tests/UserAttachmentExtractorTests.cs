using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rest;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class UserAttachmentExtractorTests
{
    [Fact]
    public void Extract_ParsesAtImageReferenceAndCleansMessage()
    {
        const string raw = "这题第二问无法做出来，你先分析下\n@image:/home/pi/.hermes/images/upload_imxk9760.jpg";

        var (clean, attachments) = UserAttachmentExtractor.Extract(raw, "msg-1");

        Assert.Equal("这题第二问无法做出来，你先分析下", clean);
        var att = Assert.Single(attachments);
        Assert.Equal(AttachmentKind.Image, att.Kind);
        Assert.Equal("/home/pi/.hermes/images/upload_imxk9760.jpg", att.Uri);
        Assert.Equal("upload_imxk9760.jpg", att.Name);
    }

    [Fact]
    public void Extract_ParsesImageAttachedHintAndFileTokensAndStripsFooter()
    {
        const string raw = """
            @file:/home/pi/notes/problem.pdf
            @file:"/home/pi/video clip.mp4"
            Check this photo [Image attached at: /tmp/shot.png]
            --- Attached Context ---
            expanded content that should be stripped
            """;

        var (clean, attachments) = UserAttachmentExtractor.Extract(raw, "msg-2");

        Assert.Equal("Check this photo", clean);
        Assert.Equal(3, attachments.Count);
        Assert.Contains(attachments, a => a.Kind == AttachmentKind.Image && a.Uri == "/tmp/shot.png");
        Assert.Contains(attachments, a => a.Kind == AttachmentKind.Document && a.Uri == "/home/pi/notes/problem.pdf");
        Assert.Contains(attachments, a => a.Kind == AttachmentKind.Video && a.Uri == "/home/pi/video clip.mp4");
    }

    [Fact]
    public void Extract_StripsCompactionRestatementBoilerplate()
    {
        const string raw = "[STILL IN PROGRESS — this is the active request, restated after the compaction boundary because it was not finished yet. Continue it; do not start over.]\n到什么状态了";

        var (clean, attachments) = UserAttachmentExtractor.Extract(raw, "msg-3");

        Assert.Equal("到什么状态了", clean);
        Assert.Empty(attachments);
    }
}

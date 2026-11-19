using System.Text.Json;
using HermesDesktop.Core.Rpc;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class AttachmentUploadHelperTests
{
    [Fact]
    public void BuildFileReferenceFromRpcResult_UsesRefTextOrFormatsPathWithQuotes()
    {
        using var doc1 = JsonDocument.Parse("""{"ref_text":"@file:/tmp/notes.md","path":"/tmp/notes.md"}""");
        Assert.Equal("@file:/tmp/notes.md", AttachmentUploadHelper.BuildFileReferenceFromRpcResult(doc1.RootElement));

        using var doc2 = JsonDocument.Parse("""{"path":"/home/pi/my docs/report (1).txt"}""");
        var ref2 = AttachmentUploadHelper.BuildFileReferenceFromRpcResult(doc2.RootElement);
        Assert.Equal("@file:`/home/pi/my docs/report (1).txt`", ref2);
        Assert.Equal("/home/pi/my docs/report (1).txt", AttachmentUploadHelper.ExtractPathFromFileReference(ref2));
    }

    [Fact]
    public void BuildPromptWithFileReferences_PrependsReferencesAheadOfTypedText()
    {
        var prompt = AttachmentUploadHelper.BuildPromptWithFileReferences(
            "Please summarize these files.",
            ["@file:/tmp/a.txt", "@file:`/tmp/b 2.md`"]);

        Assert.Equal("@file:/tmp/a.txt\n@file:`/tmp/b 2.md`\nPlease summarize these files.", prompt);
        Assert.Equal("@file:/tmp/a.txt", AttachmentUploadHelper.BuildPromptWithFileReferences("", ["@file:/tmp/a.txt"]));
        Assert.Equal("hello", AttachmentUploadHelper.BuildPromptWithFileReferences("hello", []));
    }

    [Fact]
    public void GuessMimeTypeAndImageRefusal_ClassifyExpectedValues()
    {
        Assert.Equal("image/png", AttachmentUploadHelper.GuessMimeType("screenshot.PNG"));
        Assert.Equal("text/markdown", AttachmentUploadHelper.GuessMimeType("README.md"));
        Assert.Equal("application/pdf", AttachmentUploadHelper.GuessMimeType("spec.pdf"));
        Assert.Equal("application/octet-stream", AttachmentUploadHelper.GuessMimeType("archive.unknown"));

        Assert.True(AttachmentUploadHelper.IsImageAttachRefusal(new RpcException(-32602, "Unsupported image format")));
        Assert.False(AttachmentUploadHelper.IsImageAttachRefusal(new RpcException(4001, "session not found")));
    }
}

using HermesDesktop.Core.Models;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class V2TabLayoutTests
{
    [Fact]
    public void StandardLayout_HasExpectedDefaultOrderAndHiddenOptionalTabs()
    {
        var layout = new V2TabLayout();
        Assert.Equal([V2Tab.Bots, V2Tab.Chats, V2Tab.Tasks, V2Tab.Settings], layout.Shown);
        Assert.Contains(V2Tab.Pulse, layout.Hidden);
        Assert.Contains(V2Tab.Artifacts, layout.Hidden);
    }

    [Fact]
    public void AlwaysShownTabs_CannotBeHidden()
    {
        var layout = new V2TabLayout(["-chats", "-settings", "bots"]);
        Assert.DoesNotContain(V2Tab.Chats, layout.Hidden);
        Assert.DoesNotContain(V2Tab.Settings, layout.Hidden);
        Assert.False(layout.SetHidden(V2Tab.Chats, true));
        Assert.False(layout.SetHidden(V2Tab.Settings, true));
    }

    [Fact]
    public void UnknownTokens_ArePreservedOnRoundTrip()
    {
        var layout = new V2TabLayout(["bots", "future_tab", "chats", "-tasks", "settings"]);
        var tokens = layout.ToStoredTokens();
        Assert.Contains("future_tab", tokens);
        Assert.Contains("-tasks", tokens);
    }
}

using System.Text.Json;
using NWOS.Cardinal;
using Xunit;

namespace NWOS.Cardinal.Tests;

public class CardinalIpcDispatcherTests
{
    [Fact]
    public async Task Alice_search_response_keeps_expected_camel_case_fields()
    {
        var dispatcher = new CardinalIpcDispatcher();

        var response = await dispatcher.DispatchAndExecuteAsync(
            """{"target":"nwos.alice","action":"search","payload":"security"}""");

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        var action = Assert.Single(root.GetProperty("actions").EnumerateArray());

        Assert.Contains("Quick-Scan verfügbar", root.GetProperty("message").GetString());
        Assert.Equal("security.quickscan", action.GetProperty("action").GetString());
        Assert.Equal("nwos.alice", action.GetProperty("target").GetString());
        Assert.Equal("{}", action.GetProperty("payload").GetString());
        Assert.Equal("", action.GetProperty("launchPath").GetString());
    }
}

using System.Text.Json.Nodes;
using Xunit;
using Zadeh;
using Zadeh.AI.Mcp;

namespace Zadeh.AI.Tests;

public class McpEngineServerTests
{
    private static McpEngineServer BuildServer()
    {
        var demand = new FuzzyVariable("Demand", 0, 100);
        demand.Set(FuzzySet.LeftShoulder("Low", 15, 35));
        demand.Set(FuzzySet.RightShoulder("High", 65, 85));

        var price = new FuzzyVariable("Price", 50, 150);
        price.Set(FuzzySet.LeftShoulder("Discount", 60, 80));
        price.Set(FuzzySet.RightShoulder("Premium", 120, 145));

        var engine = new MamdaniEngine()
            .Input(demand).Output(price)
            .Rule(FuzzyRule.If(demand.Is("High")).Then(price.Is("Premium")))
            .Rule(FuzzyRule.If(demand.Is("Low")).Then(price.Is("Discount")));

        return new McpEngineServer("zadeh-test", "1.0.0")
            .AddEngine("decide_price", "Computes the price multiplier from demand.", engine);
    }

    private static JsonObject Handle(McpEngineServer server, string json)
    {
        var response = server.HandleMessage(json);
        Assert.NotNull(response);
        return JsonNode.Parse(response!)!.AsObject();
    }

    [Fact]
    public void Initialize_ReportsServerInfoAndToolCapability()
    {
        var response = Handle(BuildServer(),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

        Assert.Equal(1, response["id"]!.GetValue<int>());
        var result = response["result"]!.AsObject();
        Assert.Equal("zadeh-test", result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
    }

    [Fact]
    public void Notification_GetsNoResponse()
    {
        var server = BuildServer();
        Assert.Null(server.HandleMessage("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public void ToolsList_DescribesEngineInputsAsSchema()
    {
        var response = Handle(BuildServer(),
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        var tools = response["result"]!["tools"]!.AsArray();
        Assert.Single(tools);

        var tool = tools[0]!.AsObject();
        Assert.Equal("decide_price", tool["name"]!.GetValue<string>());

        var demand = tool["inputSchema"]!["properties"]!["Demand"]!.AsObject();
        Assert.Equal("number", demand["type"]!.GetValue<string>());
        Assert.Equal(0, demand["minimum"]!.GetValue<double>());
        Assert.Equal(100, demand["maximum"]!.GetValue<double>());
        Assert.Contains("Demand", tool["inputSchema"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void ToolsCall_EvaluatesEngineAndExplains()
    {
        var response = Handle(BuildServer(),
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"decide_price","arguments":{"Demand":90}}}""");

        var result = response["result"]!.AsObject();
        Assert.False(result["isError"]!.GetValue<bool>());

        var text = result["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        var payload = JsonNode.Parse(text)!.AsObject();

        var price = payload["results"]!["Price"]!.AsObject();
        Assert.Equal("Premium", price["dominantSet"]!.GetValue<string>());
        Assert.True(price["value"]!.GetValue<double>() > 115);
        Assert.Contains("RULES", payload["explanation"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_UnknownTool_ReturnsError()
    {
        var response = Handle(BuildServer(),
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");

        Assert.NotNull(response["error"]);
        Assert.Contains("nope", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_MissingInput_ReturnsToolError()
    {
        var response = Handle(BuildServer(),
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"decide_price","arguments":{}}}""");

        var result = response["result"]!.AsObject();
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("Demand", result["content"]!.AsArray()[0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var response = Handle(BuildServer(),
            """{"jsonrpc":"2.0","id":6,"method":"resources/list"}""");

        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void MalformedJson_ReturnsParseError()
    {
        var server = BuildServer();
        var response = server.HandleMessage("{not json");

        Assert.NotNull(response);
        var parsed = JsonNode.Parse(response!)!.AsObject();
        Assert.Equal(-32700, parsed["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task RunAsync_ServesFullSessionOverStreams()
    {
        var server = BuildServer();
        var input = new StringReader(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""" + "\n" +
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n" +
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""" + "\n" +
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"decide_price","arguments":{"Demand":90}}}""" + "\n");
        var output = new StringWriter();

        await server.RunAsync(input, output);

        var lines = output.ToString().Trim().Split('\n');
        Assert.Equal(3, lines.Length); // notification produced no response
        Assert.Contains("zadeh-test", lines[0]);
        Assert.Contains("decide_price", lines[1]);
        Assert.Contains("Premium", lines[2]);
    }

    [Fact]
    public void DuplicateToolName_Throws()
    {
        var server = BuildServer();
        var engine = AdaptiveEngineStub();
        Assert.Throws<ArgumentException>(() => server.AddEngine("decide_price", "dup", engine));
    }

    private static MamdaniEngine AdaptiveEngineStub()
    {
        var x = new FuzzyVariable("X", 0, 1);
        x.Set(FuzzySet.RightShoulder("High", 0.4, 0.7));
        var y = new FuzzyVariable("Y", 0, 1);
        y.Set(FuzzySet.Triangle("Mid", 0.2, 0.5, 0.8));
        return new MamdaniEngine().Input(x).Output(y)
            .Rule(FuzzyRule.If(x.Is("High")).Then(y.Is("Mid")));
    }
}

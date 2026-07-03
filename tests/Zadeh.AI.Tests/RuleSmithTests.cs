using Xunit;
using Zadeh;
using Zadeh.AI;

namespace Zadeh.AI.Tests;

/// <summary>Fake LLM that returns scripted responses in order.</summary>
file sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _responses;
    public List<(string System, string User)> Calls { get; } = new();

    public FakeChatClient(params string[] responses) => _responses = new Queue<string>(responses);

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        Calls.Add((systemPrompt, userPrompt));
        return Task.FromResult(_responses.Dequeue());
    }
}

public class RuleSmithTests
{
    private const string ValidEngineJson = """
    {
      "inputs": [
        { "name": "CustomerActivity", "min": 0, "max": 1,
          "sets": [
            { "name": "Low",  "type": "leftShoulder",  "params": [0.2, 0.4] },
            { "name": "High", "type": "rightShoulder", "params": [0.6, 0.8] } ] }
      ],
      "outputs": [
        { "name": "Priority", "min": 0, "max": 100,
          "sets": [
            { "name": "Normal", "type": "leftShoulder",  "params": [30, 50] },
            { "name": "Top",    "type": "rightShoulder", "params": [60, 85] } ] }
      ],
      "rules": [
        { "if": { "CustomerActivity": "High" }, "then": { "Priority": "Top" } },
        { "if": { "CustomerActivity": "Low" },  "then": { "Priority": "Normal" } }
      ]
    }
    """;

    [Fact]
    public async Task ValidResponse_BuildsWorkingEngine()
    {
        var smith = new RuleSmith(new FakeChatClient(ValidEngineJson));
        var result = await smith.GenerateAsync("Prioritize active customers.");

        Assert.Equal(1, result.Attempts);
        Assert.Single(result.Engine.Outputs);
        var priority = result.Engine.EvaluateSingle(new Dictionary<string, double> { ["CustomerActivity"] = 0.9 });
        Assert.True(priority > 60, $"active customer should get top priority, got {priority:F1}");
    }

    [Fact]
    public async Task MarkdownFencedResponse_IsStripped()
    {
        var fenced = $"Here is your engine:\n```json\n{ValidEngineJson}\n```\nHope this helps!";
        var smith = new RuleSmith(new FakeChatClient(fenced));

        var result = await smith.GenerateAsync("Prioritize active customers.");
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task InvalidThenValid_RetriesWithErrorFeedback()
    {
        var invalid = ValidEngineJson.Replace("\"rightShoulder\"", "\"sigmoid\"");
        var chat = new FakeChatClient(invalid, ValidEngineJson);
        var smith = new RuleSmith(chat, maxAttempts: 2);

        var result = await smith.GenerateAsync("Prioritize active customers.");

        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, chat.Calls.Count);
        // The repair prompt must carry the validation error back to the LLM
        Assert.Contains("sigmoid", chat.Calls[1].User);
        Assert.Contains("failed validation", chat.Calls[1].User);
    }

    [Fact]
    public async Task PersistentlyInvalid_ThrowsWithLastError()
    {
        var invalid = ValidEngineJson.Replace("\"rightShoulder\"", "\"sigmoid\"");
        var smith = new RuleSmith(new FakeChatClient(invalid, invalid), maxAttempts: 2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => smith.GenerateAsync("Prioritize active customers."));
        Assert.Contains("sigmoid", ex.Message);
    }

    [Fact]
    public async Task EmptyPolicy_Throws()
    {
        var smith = new RuleSmith(new FakeChatClient(ValidEngineJson));
        await Assert.ThrowsAsync<ArgumentException>(() => smith.GenerateAsync("   "));
    }

    [Fact]
    public async Task ResultJson_RoundTripsToSameBehavior()
    {
        var smith = new RuleSmith(new FakeChatClient(ValidEngineJson));
        var result = await smith.GenerateAsync("Prioritize active customers.");

        var reloaded = MamdaniEngine.FromJson(result.Json);
        var input = new Dictionary<string, double> { ["CustomerActivity"] = 0.7 };
        Assert.Equal(
            result.Engine.EvaluateSingle(input),
            reloaded.EvaluateSingle(input),
            precision: 10);
    }

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("prose before {\"a\":1} prose after", "{\"a\":1}")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    public void ExtractJson_FindsOutermostObject(string response, string expected)
    {
        Assert.Equal(expected, RuleSmith.ExtractJson(response));
    }
}

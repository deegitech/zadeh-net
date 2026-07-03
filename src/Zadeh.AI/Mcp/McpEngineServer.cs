using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zadeh.AI.Mcp;

/// <summary>
/// Exposes Mamdani fuzzy engines as Model Context Protocol (MCP) tools over stdio,
/// so any MCP-capable AI agent (Claude, etc.) can call deterministic, explainable
/// fuzzy judgment mid-conversation instead of improvising a number.
///
/// Each registered engine becomes one MCP tool whose input schema is derived from
/// the engine's input variables (numbers with min/max bounds). A tool call runs
/// <see cref="MamdaniEngine.EvaluateWithTrace"/> and returns the crisp results,
/// dominant sets, and the full explanation.
///
/// Implements the MCP JSON-RPC 2.0 surface needed for tools: initialize,
/// tools/list, tools/call. Line-delimited JSON over any TextReader/TextWriter —
/// use <see cref="RunOnStdioAsync"/> for a standard stdio server. Zero dependencies.
///
/// <code>
/// var server = new McpEngineServer("pricing-judge")
///     .AddEngine("decide_price", "Computes the price multiplier from demand and stock.", pricingEngine);
/// await server.RunOnStdioAsync();
/// </code>
/// </summary>
public sealed class McpEngineServer
{
    private const string ProtocolVersion = "2024-11-05";

    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly Dictionary<string, (string Description, MamdaniEngine Engine)> _tools =
        new(StringComparer.Ordinal);

    /// <summary>Registered tool names.</summary>
    public IReadOnlyCollection<string> ToolNames => _tools.Keys;

    /// <summary>
    /// Creates an MCP server.
    /// </summary>
    /// <param name="serverName">Server name reported to clients (default "zadeh").</param>
    /// <param name="serverVersion">Server version reported to clients.</param>
    public McpEngineServer(string serverName = "zadeh", string serverVersion = "1.0.0")
    {
        _serverName = serverName;
        _serverVersion = serverVersion;
    }

    /// <summary>
    /// Registers an engine as an MCP tool.
    /// </summary>
    /// <param name="toolName">Tool name (e.g. "decide_price"). Must be unique.</param>
    /// <param name="description">
    /// What the tool decides and when the agent should call it — be prescriptive;
    /// agents pick tools by this text.
    /// </param>
    /// <param name="engine">The engine to run on each call.</param>
    public McpEngineServer AddEngine(string toolName, string description, MamdaniEngine engine)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        ArgumentNullException.ThrowIfNull(engine);
        if (_tools.ContainsKey(toolName))
            throw new ArgumentException($"A tool named '{toolName}' is already registered.");

        _tools[toolName] = (description, engine);
        return this;
    }

    /// <summary>Runs the server over standard input/output until stdin closes.</summary>
    public Task RunOnStdioAsync(CancellationToken cancellationToken = default) =>
        RunAsync(Console.In, Console.Out, cancellationToken);

    /// <summary>
    /// Runs the server over the given line-delimited JSON-RPC streams until the
    /// reader is exhausted. Exposed for testing and custom transports.
    /// </summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var response = HandleMessage(line);
            if (response is not null)
            {
                await output.WriteLineAsync(response).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Handles one JSON-RPC message and returns the response JSON,
    /// or null for notifications (which get no response).
    /// </summary>
    internal string? HandleMessage(string json)
    {
        JsonObject message;
        try
        {
            message = JsonNode.Parse(json)?.AsObject()
                ?? throw new JsonException("not an object");
        }
        catch (JsonException ex)
        {
            return Error(null, -32700, $"Parse error: {ex.Message}");
        }

        var id = message["id"];
        var method = message["method"]?.GetValue<string>();

        // Notifications (no id) never get a response.
        if (id is null)
            return null;

        return method switch
        {
            "initialize" => Result(id, new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = _serverName, ["version"] = _serverVersion }
            }),
            "ping" => Result(id, new JsonObject()),
            "tools/list" => Result(id, new JsonObject { ["tools"] = BuildToolList() }),
            "tools/call" => HandleToolCall(id, message["params"]?.AsObject()),
            _ => Error(id, -32601, $"Method not found: {method}")
        };
    }

    // ─── tools/list ──────────────────────────────────────────────────────

    private JsonArray BuildToolList()
    {
        var tools = new JsonArray();
        foreach (var (name, (description, engine)) in _tools)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var input in engine.Inputs)
            {
                var sets = string.Join(", ", input.Sets.Keys);
                properties[input.Name] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = input.Min,
                    ["maximum"] = input.Max,
                    ["description"] = $"Range [{input.Min}, {input.Max}]. Linguistic sets: {sets}."
                };
                required.Add(input.Name);
            }

            tools.Add(new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required
                }
            });
        }
        return tools;
    }

    // ─── tools/call ──────────────────────────────────────────────────────

    private string HandleToolCall(JsonNode id, JsonObject? parameters)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        if (toolName is null || !_tools.TryGetValue(toolName, out var tool))
            return Error(id, -32602, $"Unknown tool: {toolName ?? "(missing name)"}");

        var inputs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (parameters?["arguments"] is JsonObject arguments)
        {
            foreach (var (key, value) in arguments)
            {
                if (value is null) continue;
                try
                {
                    inputs[key] = value.GetValue<double>();
                }
                catch (Exception)
                {
                    return ToolError(id, $"Argument '{key}' must be a number.");
                }
            }
        }

        try
        {
            var trace = tool.Engine.EvaluateWithTrace(inputs);

            var results = new JsonObject();
            foreach (var (name, result) in trace.Results)
            {
                results[name] = new JsonObject
                {
                    ["value"] = result.CrispValue,
                    ["dominantSet"] = result.DominantSet,
                    ["anyRuleFired"] = result.AnyRuleFired
                };
            }

            var payload = new JsonObject
            {
                ["results"] = results,
                ["explanation"] = trace.Explain()
            };

            return Result(id, new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                    }
                },
                ["isError"] = false
            });
        }
        catch (ArgumentException ex)
        {
            // e.g. missing input variable — report as a tool-level error the agent can react to
            return ToolError(id, ex.Message);
        }
    }

    private static string ToolError(JsonNode id, string message) =>
        Result(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = message }
            },
            ["isError"] = true
        });

    // ─── JSON-RPC envelopes ──────────────────────────────────────────────

    private static string Result(JsonNode id, JsonObject result) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result
        }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString();
}

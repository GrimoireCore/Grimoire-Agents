using AgentLearning.Core.Skills;
using ModelContextProtocol.Client;

namespace AgentLearning.App;

/// <summary>
/// Owns one MCP client connection and the agent skills discovered through it.
/// </summary>
public sealed class McpSkillClient : IAsyncDisposable
{
    private readonly McpClient _client;

    private McpSkillClient(McpClient client, IReadOnlyList<IAgentSkill> skills)
    {
        _client = client;
        Skills = skills;
    }

    public IReadOnlyList<IAgentSkill> Skills { get; }

    public static async Task<McpSkillClient> ConnectStdioAsync(
        string serverName,
        string command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, McpToolPolicy> toolPolicies,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(toolPolicies);

        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Name = serverName.Trim(),
            Command = command.Trim(),
            Arguments = arguments.ToArray()
        });

        McpClient client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        try
        {
            IList<McpClientTool> listedTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            McpClientTool[] tools = listedTools.ToArray();
            ValidateToolPolicies(tools, toolPolicies);

            IAgentSkill[] skills = tools
                .Select(tool => new McpAgentSkill(tool, toolPolicies[tool.Name]))
                .ToArray();
            return new McpSkillClient(client, skills);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
    }

    private static void ValidateToolPolicies(
        IReadOnlyCollection<McpClientTool> tools,
        IReadOnlyDictionary<string, McpToolPolicy> toolPolicies)
    {
        string[] discoveredNames = tools
            .Select(tool => tool.Name)
            .ToArray();
        string[] toolsWithoutPolicy = discoveredNames
            .Except(toolPolicies.Keys, StringComparer.Ordinal)
            .ToArray();
        if (toolsWithoutPolicy.Length > 0)
        {
            throw new InvalidOperationException(
                $"MCP tools require local security policies: {string.Join(", ", toolsWithoutPolicy)}");
        }

        string[] missingTools = toolPolicies.Keys
            .Except(discoveredNames, StringComparer.Ordinal)
            .ToArray();
        if (missingTools.Length > 0)
        {
            throw new InvalidOperationException(
                $"Configured MCP tools were not discovered: {string.Join(", ", missingTools)}");
        }
    }
}

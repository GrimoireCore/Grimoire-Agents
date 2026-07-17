# 第 9 章：MCP，把工具变成独立服务

[上一章：审批与恢复](08-approval-and-checkpoint.md) | [下一章：RAG](10-rag.md)

## 本章起点与终点

| 项目 | 内容 |
|---|---|
| 起点 | 所有 Skill 都在 Agent 应用进程中直接 `new` |
| 终点 | Agent 通过 MCP 动态发现并调用独立进程中的工具 |
| 自动化验收 | 104 tests |

## 9.1 MCP 改变了什么

迁移前：

```mermaid
flowchart LR
    H["AgentRunner"] --> R["AgentSkillRegistry"] --> W["WriteNoteSkill C# object"]
```

迁移后：

```mermaid
flowchart LR
    H["AgentRunner"] --> R["AgentSkillRegistry"]
    R --> A["McpAgentSkill adapter"]
    A --> C["MCP Client"]
    C <-->|"JSON-RPC over stdio"| S["MCP Server process"]
    S --> W["WriteNoteSkill"]
    S --> K["Knowledge Search"]
```

对 Runner 来说，仍然是 `IAgentSkill.ExecuteAsync`。MCP 被 Adapter 隔离在注册表后面，所以审批、超时、Checkpoint、幂等上下文都能继续复用。

## 9.2 MCP 是协议，不是 AI

MCP 主要标准化：

- Client 如何连接 Server。
- 如何列出 Tools。
- Tool 名称、描述和 JSON Schema 如何发布。
- 如何发送 Call Tool 请求。
- 如何返回文本、结构化结果或错误。

它不负责：

- 判断用户意图。
- 决定选哪个工具。
- 执行人工审批。
- 控制 Agent 循环。

这些仍属于模型与 Harness。

## 9.3 创建独立 MCP Server 项目

```text
src/
└── AgentLearning.McpServer/
    ├── AgentLearning.McpServer.csproj
    ├── Program.cs
    ├── LearningProgressTools.cs
    ├── NoteTools.cs
    ├── KnowledgeTools.cs
    └── LmStudioEmbeddingClient.cs
```

项目依赖：

```xml
<ItemGroup>
  <ProjectReference Include="..\AgentLearning.Core\AgentLearning.Core.csproj" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.9" />
  <PackageReference Include="ModelContextProtocol" Version="1.4.1" />
</ItemGroup>
```

## 9.4 Server 启动代码

核心注册：

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new WriteNoteSkill(notesFilePath));
builder.Services.AddSingleton(knowledgeIndex);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

`WithToolsFromAssembly` 扫描 `[McpServerToolType]` 与 `[McpServerTool]`，自动发布工具元数据。

## 9.5 stdio 的关键规则

使用 stdio transport 时：

```text
stdin  = Client 发来的 MCP JSON-RPC
stdout = Server 返回的 MCP JSON-RPC
stderr = 人类日志
```

所以 Server 不能随便：

```csharp
Console.WriteLine("server started");
```

这会污染协议流。日志必须写 `stderr`：

```csharp
Console.Error.WriteLine("Knowledge index loaded.");

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
```

## 9.6 发布 write_note

```csharp
[McpServerToolType]
public static class NoteTools
{
    [McpServerTool(
        Name = "write_note",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Appends a note to the learner's local Markdown notes file.")]
    public static Task<string> WriteNoteAsync(
        [Description("The note content to append.")] string note,
        WriteNoteSkill writeNoteSkill,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        AgentToolExecutionContext executionContext =
            ReadExecutionContext(requestContext);

        string argumentsJson = JsonSerializer.Serialize(new { note });
        return writeNoteSkill.ExecuteAsync(
            argumentsJson,
            executionContext,
            cancellationToken);
    }
}
```

SDK 会从参数和 Attribute 生成 JSON Schema。`WriteNoteSkill` 由 DI 注入，不会作为模型参数公开。

## 9.7 幂等元数据如何跨协议传递

客户端发送 MCP `_meta`：

```json
{
  "io.grimoire/run-id": "run_123",
  "io.grimoire/tool-call-id": "call_456",
  "io.grimoire/idempotency-key": "run_123:call_456"
}
```

Server 重新构建：

```csharp
AgentToolExecutionContext executionContext = new(runId, toolCallId);

if (!string.Equals(
        receivedIdempotencyKey,
        executionContext.IdempotencyKey,
        StringComparison.Ordinal))
{
    throw new McpException(
        "The MCP idempotency metadata does not match the tool call identity.");
}
```

远程边界不能让客户端随意声明一个与身份不一致的幂等键。

## 9.8 Client 启动子进程并发现工具

```csharp
StdioClientTransport transport = new(new StdioClientTransportOptions
{
    Name = serverName.Trim(),
    Command = "dotnet",
    Arguments = [mcpServerAssemblyPath, "--notes-file", notesPath, ...]
});

McpClient client = await McpClient.CreateAsync(
    transport,
    cancellationToken: cancellationToken);

IList<McpClientTool> listedTools = await client.ListToolsAsync(
    cancellationToken: cancellationToken);
```

此时工具不是在代码里一个个 `new`，而是由 Server 在运行时发布。

## 9.9 动态工具如何变回 IAgentSkill

`McpAgentSkill` 是适配器：

```csharp
public string Name => _tool.Name;
public string Description { get; }
public string ParametersJson => _tool.JsonSchema.GetRawText();
public AgentSkillRiskLevel RiskLevel { get; }
public bool RequiresConfirmation { get; }
```

执行：

```csharp
public async Task<string> ExecuteAsync(
    string argumentsJson,
    AgentToolExecutionContext executionContext,
    CancellationToken cancellationToken = default)
{
    Dictionary<string, object?> arguments = ParseArguments(argumentsJson);

    RequestOptions requestOptions = new()
    {
        Meta = new JsonObject
        {
            ["io.grimoire/run-id"] = executionContext.RunId,
            ["io.grimoire/tool-call-id"] = executionContext.ToolCallId,
            ["io.grimoire/idempotency-key"] = executionContext.IdempotencyKey
        }
    };

    CallToolResult result = await _tool.CallAsync(
        arguments,
        options: requestOptions,
        cancellationToken: cancellationToken);

    return ReadResultText(result);
}
```

Harness 不需要知道这次工具调用跨了进程。

## 9.10 远程工具的安全策略必须在本地

Server 可以声明 Tool Annotations，但 Client 不能把远程描述直接当作安全授权。课程要求为每个发现的工具配置本地策略：

```csharp
Dictionary<string, McpToolPolicy> policies = new(StringComparer.Ordinal)
{
    ["get_learning_progress"] = new(AgentSkillRiskLevel.Low, false),
    ["search_knowledge"] = new(AgentSkillRiskLevel.Low, false),
    ["write_note"] = new(AgentSkillRiskLevel.Medium, true)
};
```

发现了未配置工具或配置了不存在的工具，启动直接失败。这样远端新增一个危险 Tool 时，不会自动获得执行权限。

## 9.11 注册本地与 MCP Skill

```csharp
AgentSkillRegistry skillRegistry = new([
    new TimeSkill(),
    new CalculatorSkill(),
    .. mcpSkillClient.Skills
]);
```

从这里往后：

```text
calculate        -> 本地 C# Skill
get_current_time -> 本地 C# Skill
write_note       -> MCP Tool Adapter
search_knowledge -> MCP Tool Adapter
```

它们对 Tool Router 和 AgentRunner 都是同一种 `IAgentSkill`。

## 9.12 A 电脑能否让 B 电脑写文件

概念上可以，但当前实现还不是：

- 当前 `stdio` Server 是 Agent 在同一台电脑启动的子进程。
- `write_note` 写的是启动 Server 那台电脑的路径。

若 MCP Server 部署在 B 电脑，并通过网络可达 transport 提供服务，那么 A 电脑的 MCP Client 调用 `write_note`，文件会写在 B 电脑。你还必须增加 TLS、认证、网络授权、目录限制和审计。

所以“本地函数改成远程调用”是正确方向，但是否远程取决于 transport 和部署位置，不是只看它叫 MCP。

## 9.13 本章为什么也出现 RAG 基础代码

真实提交 `ac0c387` 同时加入了：

- Markdown Loader 与 Chunk。
- Keyword Index。
- LM Studio Embedding Client。
- Vector Index 与缓存。
- `search_knowledge` MCP Tool。

本章先把它们当作 MCP Server 提供的第三个工具，下一章再完整拆解 RAG 算法，并在其上加入 Hybrid Search。

## 9.14 MCP 流程测试

测试真正启动独立 Server 子进程，并完成：

1. `ListToolsAsync` 发现三个工具。
2. 调用 `get_learning_progress`。
3. 调用 `search_knowledge`。
4. 两次调用相同 `write_note`，验证只写一次。
5. 验证风险策略与 JSON Schema。

```bash
dotnet test AgentLearning.sln
```


## 9.15 macOS `/tmp` 路径排错实例

课程重放时第一次把临时项目放在 `/tmp/...`，MCP 子进程报：

```text
Could not load file or assembly 'AgentLearning.Core'
```

文件其实存在。原因是 macOS 的 `/tmp` 是 `/private/tmp` 的别名，预览版 .NET 在同一构建中混用了两个规范化路径，生成的 `.deps.json` 漏掉 ProjectReference。改用真实 `/private/tmp/...` 后 104 个测试全部通过。

排查方法不是猜 MCP 协议，而是比较：

```bash
rg 'AgentLearning.Core' \
  src/AgentLearning.McpServer/bin/Debug/net8.0/AgentLearning.McpServer.deps.json
```

这个例子说明“程序集在目录中”不等于运行时依赖清单正确。

<!-- BEGIN SELF-CONTAINED CODE -->
## 本章完整文件代码

这一节是本章的**完整代码依据**。前面的代码用于解释概念；真正动手时，请从上一章完成后的目录继续，并按下表逐项操作。`新建` 表示创建此前不存在的文件，`完整覆盖` 表示把旧文件全部替换成这里的内容。不要只复制局部片段。

> 下面已经包含本章所需的全部新增和变更文件，不需要再查找其他代码文件。

先在项目根目录执行下面的命令，确保本章需要的目录存在：

```bash
mkdir -p src/AgentLearning.App src/AgentLearning.App/knowledge src/AgentLearning.Core src/AgentLearning.Core/Knowledge src/AgentLearning.Core/Skills src/AgentLearning.McpServer tests/AgentLearning.Core.Tests
```

### 文件操作清单

| 操作 | 文件 |
|---|---|
| 新建 | `src/AgentLearning.App/McpAgentSkill.cs` |
| 新建 | `src/AgentLearning.App/McpSkillClient.cs` |
| 新建 | `src/AgentLearning.App/McpToolPolicy.cs` |
| 新建 | `src/AgentLearning.App/knowledge/agent-harness.md` |
| 新建 | `src/AgentLearning.App/knowledge/memory-mcp-rag.md` |
| 新建 | `src/AgentLearning.Core/Knowledge/ITextEmbeddingClient.cs` |
| 新建 | `src/AgentLearning.Core/Knowledge/KeywordKnowledgeIndex.cs` |
| 新建 | `src/AgentLearning.Core/Knowledge/KnowledgeChunk.cs` |
| 新建 | `src/AgentLearning.Core/Knowledge/KnowledgeSearchResult.cs` |
| 新建 | `src/AgentLearning.Core/Knowledge/MarkdownKnowledgeLoader.cs` |
| 新建 | `src/AgentLearning.Core/Knowledge/VectorKnowledgeIndex.cs` |
| 新建 | `src/AgentLearning.Core/Knowledge/VectorKnowledgeIndexCacheStore.cs` |
| 新建 | `src/AgentLearning.Core/Skills/AgentToolExecutionContext.cs` |
| 新建 | `src/AgentLearning.McpServer/AgentLearning.McpServer.csproj` |
| 新建 | `src/AgentLearning.McpServer/KnowledgeTools.cs` |
| 新建 | `src/AgentLearning.McpServer/LearningProgressTools.cs` |
| 新建 | `src/AgentLearning.McpServer/LmStudioEmbeddingClient.cs` |
| 新建 | `src/AgentLearning.McpServer/NoteTools.cs` |
| 新建 | `src/AgentLearning.McpServer/Program.cs` |
| 新建 | `tests/AgentLearning.Core.Tests/AgentToolExecutionContextTests.cs` |
| 新建 | `tests/AgentLearning.Core.Tests/KeywordKnowledgeIndexTests.cs` |
| 新建 | `tests/AgentLearning.Core.Tests/McpSkillClientTests.cs` |
| 新建 | `tests/AgentLearning.Core.Tests/VectorKnowledgeIndexTests.cs` |
| 完整覆盖 | `AgentLearning.sln` |
| 完整覆盖 | `src/AgentLearning.App/AgentLearning.App.csproj` |
| 完整覆盖 | `src/AgentLearning.App/AgentRunner.cs` |
| 完整覆盖 | `src/AgentLearning.App/Program.cs` |
| 完整覆盖 | `src/AgentLearning.App/agent.json` |
| 完整覆盖 | `src/AgentLearning.Core/AgentCheckpointResumer.cs` |
| 完整覆盖 | `src/AgentLearning.Core/AgentProfile.cs` |
| 完整覆盖 | `src/AgentLearning.Core/AgentProfileLoader.cs` |
| 完整覆盖 | `src/AgentLearning.Core/Skills/AgentSkillRegistry.cs` |
| 完整覆盖 | `src/AgentLearning.Core/Skills/CalculatorSkill.cs` |
| 完整覆盖 | `src/AgentLearning.Core/Skills/IAgentSkill.cs` |
| 完整覆盖 | `src/AgentLearning.Core/Skills/TimeSkill.cs` |
| 完整覆盖 | `src/AgentLearning.Core/Skills/WriteNoteSkill.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/AgentCheckpointResumerTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/AgentProfileLoaderTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/AgentRunnerPauseTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/AgentRunnerResumeTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/AgentSkillRegistryTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/AgentToolPermissionPolicyTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/CalculatorSkillTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/TimeSkillTests.cs` |
| 完整覆盖 | `tests/AgentLearning.Core.Tests/WriteNoteSkillTests.cs` |

<!-- FILE: ADD src/AgentLearning.App/McpAgentSkill.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.App/McpAgentSkill.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentLearning.App;

/// <summary>
/// Adapts one dynamically discovered MCP tool to the existing agent skill contract.
/// </summary>
public sealed class McpAgentSkill : IAgentSkill
{
    private readonly McpClientTool _tool;

    public McpAgentSkill(McpClientTool tool, McpToolPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(tool.Description))
        {
            throw new InvalidOperationException($"MCP tool '{tool.Name}' must provide a description.");
        }

        _tool = tool;
        Description = tool.Description;
        RiskLevel = policy.RiskLevel;
        RequiresConfirmation = policy.RequiresConfirmation;
    }

    public string Name => _tool.Name;

    public string Description { get; }

    public string ParametersJson => _tool.JsonSchema.GetRawText();

    public AgentSkillRiskLevel RiskLevel { get; }

    public bool RequiresConfirmation { get; }

    public async Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        Dictionary<string, object?> arguments = ParseArguments(argumentsJson);
        RequestOptions requestOptions = new()
        {
            Meta = new JsonObject
            {
                ["io.grimoire/run-id"] = executionContext.RunId,
                ["io.grimoire/tool-call-id"] = executionContext.ToolCallId,
                ["io.grimoire/idempotency-key"] = executionContext.IdempotencyKey
            }
        };

        CallToolResult result = await _tool.CallAsync(
            arguments,
            options: requestOptions,
            cancellationToken: cancellationToken);
        string resultText = ReadResultText(result);

        if (result.IsError is true)
        {
            throw new InvalidOperationException($"MCP tool '{Name}' failed: {resultText}");
        }

        return resultText;
    }

    private static Dictionary<string, object?> ParseArguments(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("MCP tool arguments must be a JSON object.");
        }

        return document.RootElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
    }

    private static string ReadResultText(CallToolResult result)
    {
        string text = string.Join(
            Environment.NewLine,
            result.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text));

        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (result.StructuredContent is JsonElement structuredContent)
        {
            return structuredContent.GetRawText();
        }

        throw new InvalidOperationException("MCP tool returned no text or structured content.");
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.App/McpSkillClient.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.App/McpSkillClient.cs</code></summary>

`````csharp
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
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.App/McpToolPolicy.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.App/McpToolPolicy.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.App;

/// <summary>
/// Local security policy for one remotely discovered MCP tool.
/// </summary>
public sealed record McpToolPolicy(
    AgentSkillRiskLevel RiskLevel,
    bool RequiresConfirmation);
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.App/knowledge/agent-harness.md -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.App/knowledge/agent-harness.md</code></summary>

`````markdown
# Agent Harness

Agent Harness 是运行和约束 Agent 的执行框架。模型可以提出工具调用建议，但 Harness 决定是否允许执行、何时停止，以及发生异常时如何处理。

当前项目中的 Harness 负责工具循环次数限制、工具超时、结果长度限制、风险判断、人工确认、Checkpoint 保存和恢复。高风险或有副作用的工具不能因为模型提出调用就立即执行。

当工具需要确认时，Harness 会保存模型消息、工具名称、参数、runId 和 toolCallId，然后暂停运行。用户输入 yes 后，程序从 Checkpoint 恢复并执行原来的工具调用；输入 no 时，程序把拒绝结果返回模型。

幂等键由 runId 和 toolCallId 计算得到。同一个逻辑工具调用即使因为重试或进程重启被执行多次，也应当只产生一次副作用。
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.App/knowledge/memory-mcp-rag.md -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.App/knowledge/memory-mcp-rag.md</code></summary>

`````markdown
# Memory、MCP 与 RAG

Memory 用于保留对话历史，让 Agent 知道用户和模型之前聊过什么。聊天记录太多时，程序会按照窗口大小和字符限制筛选最近的对话。

MCP 是连接工具提供方和工具调用方的标准协议。当前项目使用 stdio 启动本机 MCP 子进程，客户端通过 tools/list 发现工具，通过 tools/call 执行工具。将来可以改为 HTTP，让 MCP Server 运行在另一台电脑上。

RAG 用于从外部文档中检索与问题相关的内容，再把检索结果和用户问题一起交给模型。它解决的是外部知识检索问题，不等同于聊天记忆，也不等同于 MCP。

当前 RAG 会加载 Markdown 文件并切成带重叠的 Chunk，再通过本机 LM Studio 的 Granite 多语言 Embedding 模型生成 768 维向量。用户提问时，程序为问题生成向量，使用余弦相似度返回最相关的三段，因此能够检索用词不同但语义接近的内容。

向量相似度低于阈值时，工具返回没有找到相关知识，避免把明显无关的文档交给模型。文档向量会保存到本地 JSON 索引；启动时模型、切块参数和文档指纹都没有变化，程序会直接加载缓存，只有知识文档变化时才重新生成向量。
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/ITextEmbeddingClient.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/ITextEmbeddingClient.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Converts text inputs into numeric embedding vectors.
/// </summary>
public interface ITextEmbeddingClient
{
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/KeywordKnowledgeIndex.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/KeywordKnowledgeIndex.cs</code></summary>

`````csharp
using System.Text;

namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Loads Markdown documents, splits them into chunks, and performs keyword retrieval.
/// </summary>
public sealed class KeywordKnowledgeIndex
{
    private const int MaximumResultCount = 10;

    private readonly IReadOnlyList<KnowledgeChunk> _chunks;

    private KeywordKnowledgeIndex(IReadOnlyList<KnowledgeChunk> chunks)
    {
        _chunks = chunks;
    }

    public int ChunkCount => _chunks.Count;

    public static async Task<KeywordKnowledgeIndex> LoadFromDirectoryAsync(
        string directoryPath,
        int chunkSize = MarkdownKnowledgeLoader.DefaultChunkSize,
        int chunkOverlap = MarkdownKnowledgeLoader.DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeChunk> chunks = await MarkdownKnowledgeLoader.LoadFromDirectoryAsync(
            directoryPath,
            chunkSize,
            chunkOverlap,
            cancellationToken);
        return new KeywordKnowledgeIndex(chunks);
    }

    public IReadOnlyList<KnowledgeSearchResult> Search(string query, int maxResults = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > MaximumResultCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Result count must be between 1 and {MaximumResultCount}.");
        }

        HashSet<string> queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            throw new InvalidOperationException("The knowledge query contains no searchable words.");
        }

        string compactQuery = RemoveWhitespace(query);
        return _chunks
            .Select(chunk => CreateSearchResult(chunk, queryTokens, compactQuery))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.SourcePath, StringComparer.Ordinal)
            .ThenBy(result => result.Chunk.ChunkNumber)
            .Take(maxResults)
            .ToArray();
    }

    private static KnowledgeSearchResult CreateSearchResult(
        KnowledgeChunk chunk,
        IReadOnlySet<string> queryTokens,
        string compactQuery)
    {
        HashSet<string> chunkTokens = Tokenize($"{chunk.SourcePath}\n{chunk.Content}");
        int matchingTokenCount = queryTokens.Count(chunkTokens.Contains);
        if (matchingTokenCount == 0)
        {
            return new KnowledgeSearchResult(chunk, 0);
        }

        double coverageScore = matchingTokenCount / (double)queryTokens.Count;
        double exactPhraseBonus = RemoveWhitespace(chunk.Content)
            .Contains(compactQuery, StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        return new KnowledgeSearchResult(
            chunk,
            matchingTokenCount + coverageScore + exactPhraseBonus);
    }

    private static HashSet<string> Tokenize(string text)
    {
        HashSet<string> tokens = new(StringComparer.Ordinal);
        StringBuilder latinWord = new();
        StringBuilder chineseSequence = new();

        foreach (char character in text)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_')
            {
                FlushChineseSequence(chineseSequence, tokens);
                latinWord.Append(char.ToLowerInvariant(character));
                continue;
            }

            FlushLatinWord(latinWord, tokens);
            if (IsChineseCharacter(character))
            {
                chineseSequence.Append(character);
            }
            else
            {
                FlushChineseSequence(chineseSequence, tokens);
            }
        }

        FlushLatinWord(latinWord, tokens);
        FlushChineseSequence(chineseSequence, tokens);
        return tokens;
    }

    private static void FlushLatinWord(StringBuilder word, ISet<string> tokens)
    {
        if (word.Length > 0)
        {
            tokens.Add(word.ToString());
            word.Clear();
        }
    }

    private static void FlushChineseSequence(StringBuilder sequence, ISet<string> tokens)
    {
        if (sequence.Length == 1)
        {
            tokens.Add(sequence.ToString());
        }
        else
        {
            for (int index = 0; index < sequence.Length - 1; index++)
            {
                tokens.Add(sequence.ToString(index, 2));
            }
        }

        sequence.Clear();
    }

    private static bool IsChineseCharacter(char character)
    {
        return character is >= '\u4e00' and <= '\u9fff';
    }

    private static string RemoveWhitespace(string text)
    {
        return string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    }

}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/KnowledgeChunk.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/KnowledgeChunk.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Knowledge;

/// <summary>
/// One searchable piece of a source document.
/// </summary>
public sealed record KnowledgeChunk(
    string SourcePath,
    int ChunkNumber,
    string Content);
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/KnowledgeSearchResult.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/KnowledgeSearchResult.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Knowledge;

/// <summary>
/// A knowledge chunk and its relevance score for one query.
/// </summary>
public sealed record KnowledgeSearchResult(
    KnowledgeChunk Chunk,
    double Score);
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/MarkdownKnowledgeLoader.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/MarkdownKnowledgeLoader.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Loads Markdown files and splits their text into overlapping chunks.
/// </summary>
public static class MarkdownKnowledgeLoader
{
    public const int DefaultChunkSize = 800;
    public const int DefaultChunkOverlap = 120;

    public static async Task<IReadOnlyList<KnowledgeChunk>> LoadFromDirectoryAsync(
        string directoryPath,
        int chunkSize = DefaultChunkSize,
        int chunkOverlap = DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ValidateChunkSettings(chunkSize, chunkOverlap);

        string fullDirectoryPath = Path.GetFullPath(directoryPath.Trim());
        if (!Directory.Exists(fullDirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Knowledge directory '{fullDirectoryPath}' was not found.");
        }

        string[] markdownFiles = Directory
            .EnumerateFiles(fullDirectoryPath, "*.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (markdownFiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"Knowledge directory '{fullDirectoryPath}' contains no Markdown files.");
        }

        List<KnowledgeChunk> chunks = [];
        foreach (string filePath in markdownFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            string sourcePath = Path
                .GetRelativePath(fullDirectoryPath, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            chunks.AddRange(SplitIntoChunks(sourcePath, content, chunkSize, chunkOverlap));
        }

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException(
                $"Knowledge directory '{fullDirectoryPath}' contains no searchable text.");
        }

        return chunks;
    }

    private static IReadOnlyList<KnowledgeChunk> SplitIntoChunks(
        string sourcePath,
        string content,
        int chunkSize,
        int chunkOverlap)
    {
        string normalizedContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalizedContent.Length == 0)
        {
            return [];
        }

        List<KnowledgeChunk> chunks = [];
        int stepSize = chunkSize - chunkOverlap;
        int chunkNumber = 1;
        for (int startIndex = 0; startIndex < normalizedContent.Length; startIndex += stepSize)
        {
            int length = Math.Min(chunkSize, normalizedContent.Length - startIndex);
            string chunkContent = normalizedContent.Substring(startIndex, length).Trim();
            if (chunkContent.Length > 0)
            {
                chunks.Add(new KnowledgeChunk(sourcePath, chunkNumber++, chunkContent));
            }

            if (startIndex + length >= normalizedContent.Length)
            {
                break;
            }
        }

        return chunks;
    }

    private static void ValidateChunkSettings(int chunkSize, int chunkOverlap)
    {
        if (chunkSize < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be at least 20 characters.");
        }

        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkOverlap),
                "Chunk overlap must be non-negative and smaller than the chunk size.");
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/VectorKnowledgeIndex.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/VectorKnowledgeIndex.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Retrieves Markdown chunks by comparing normalized embedding vectors.
/// </summary>
public sealed class VectorKnowledgeIndex
{
    private const int MaximumResultCount = 10;
    private const double DefaultMinimumSimilarity = 0.45;

    private readonly ITextEmbeddingClient _embeddingClient;
    private readonly IReadOnlyList<VectorKnowledgeChunk> _chunks;
    private readonly int _embeddingDimensions;

    private VectorKnowledgeIndex(
        ITextEmbeddingClient embeddingClient,
        IReadOnlyList<VectorKnowledgeChunk> chunks,
        int embeddingDimensions,
        bool loadedFromCache)
    {
        _embeddingClient = embeddingClient;
        _chunks = chunks;
        _embeddingDimensions = embeddingDimensions;
        LoadedFromCache = loadedFromCache;
    }

    public int ChunkCount => _chunks.Count;

    public int EmbeddingDimensions => _embeddingDimensions;

    public bool LoadedFromCache { get; }

    public static async Task<VectorKnowledgeIndex> LoadFromDirectoryAsync(
        string directoryPath,
        ITextEmbeddingClient embeddingClient,
        int chunkSize = MarkdownKnowledgeLoader.DefaultChunkSize,
        int chunkOverlap = MarkdownKnowledgeLoader.DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddingClient);

        IReadOnlyList<KnowledgeChunk> chunks = await MarkdownKnowledgeLoader.LoadFromDirectoryAsync(
            directoryPath,
            chunkSize,
            chunkOverlap,
            cancellationToken);
        return await CreateFromChunksAsync(
            chunks,
            embeddingClient,
            loadedFromCache: false,
            cancellationToken);
    }

    public static async Task<VectorKnowledgeIndex> LoadOrCreateAsync(
        string directoryPath,
        string cacheFilePath,
        string embeddingModel,
        ITextEmbeddingClient embeddingClient,
        int chunkSize = MarkdownKnowledgeLoader.DefaultChunkSize,
        int chunkOverlap = MarkdownKnowledgeLoader.DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentNullException.ThrowIfNull(embeddingClient);

        IReadOnlyList<KnowledgeChunk> chunks = await MarkdownKnowledgeLoader.LoadFromDirectoryAsync(
            directoryPath,
            chunkSize,
            chunkOverlap,
            cancellationToken);
        string documentFingerprint = VectorKnowledgeIndexCacheStore.BuildDocumentFingerprint(chunks);
        VectorKnowledgeIndexCache? cache = await VectorKnowledgeIndexCacheStore.LoadAsync(
            cacheFilePath,
            cancellationToken);

        if (cache is not null && IsCurrentCache(
                cache,
                embeddingModel.Trim(),
                chunkSize,
                chunkOverlap,
                documentFingerprint))
        {
            return CreateFromCache(chunks, embeddingClient, cache);
        }

        VectorKnowledgeIndex index = await CreateFromChunksAsync(
            chunks,
            embeddingClient,
            loadedFromCache: false,
            cancellationToken);
        VectorKnowledgeIndexCache newCache = index.CreateCache(
            embeddingModel.Trim(),
            chunkSize,
            chunkOverlap,
            documentFingerprint);
        await VectorKnowledgeIndexCacheStore.SaveAsync(
            cacheFilePath,
            newCache,
            cancellationToken);

        return index;
    }

    private static async Task<VectorKnowledgeIndex> CreateFromChunksAsync(
        IReadOnlyList<KnowledgeChunk> chunks,
        ITextEmbeddingClient embeddingClient,
        bool loadedFromCache,
        CancellationToken cancellationToken)
    {
        string[] inputs = chunks.Select(chunk => chunk.Content).ToArray();
        IReadOnlyList<float[]> embeddings = await embeddingClient.CreateEmbeddingsAsync(
            inputs,
            cancellationToken);

        ValidateEmbeddingCount(embeddings, chunks.Count);
        int embeddingDimensions = embeddings[0].Length;
        VectorKnowledgeChunk[] vectorChunks = chunks
            .Select((chunk, index) => new VectorKnowledgeChunk(
                chunk,
                NormalizeEmbedding(embeddings[index], embeddingDimensions)))
            .ToArray();

        return new VectorKnowledgeIndex(
            embeddingClient,
            vectorChunks,
            embeddingDimensions,
            loadedFromCache);
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int maxResults = 3,
        double minimumSimilarity = DefaultMinimumSimilarity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > MaximumResultCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Result count must be between 1 and {MaximumResultCount}.");
        }

        if (minimumSimilarity is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSimilarity),
                "Minimum similarity must be between -1 and 1.");
        }

        IReadOnlyList<float[]> queryEmbeddings = await _embeddingClient.CreateEmbeddingsAsync(
            [query.Trim()],
            cancellationToken);
        ValidateEmbeddingCount(queryEmbeddings, expectedCount: 1);
        float[] normalizedQuery = NormalizeEmbedding(
            queryEmbeddings[0],
            _embeddingDimensions);

        return _chunks
            .Select(chunk => new KnowledgeSearchResult(
                chunk.Chunk,
                CalculateDotProduct(normalizedQuery, chunk.NormalizedEmbedding)))
            .Where(result => result.Score >= minimumSimilarity)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.SourcePath, StringComparer.Ordinal)
            .ThenBy(result => result.Chunk.ChunkNumber)
            .Take(maxResults)
            .ToArray();
    }

    private static void ValidateEmbeddingCount(
        IReadOnlyList<float[]> embeddings,
        int expectedCount)
    {
        if (embeddings.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Embedding service returned {embeddings.Count} vectors for {expectedCount} inputs.");
        }

        if (embeddings.Any(embedding => embedding.Length == 0))
        {
            throw new InvalidOperationException("Embedding service returned an empty vector.");
        }
    }

    private static bool IsCurrentCache(
        VectorKnowledgeIndexCache cache,
        string embeddingModel,
        int chunkSize,
        int chunkOverlap,
        string documentFingerprint)
    {
        return cache.FormatVersion == VectorKnowledgeIndexCacheStore.CurrentFormatVersion
            && string.Equals(cache.EmbeddingModel, embeddingModel, StringComparison.Ordinal)
            && cache.ChunkSize == chunkSize
            && cache.ChunkOverlap == chunkOverlap
            && string.Equals(
                cache.DocumentFingerprint,
                documentFingerprint,
                StringComparison.Ordinal);
    }

    private static VectorKnowledgeIndex CreateFromCache(
        IReadOnlyList<KnowledgeChunk> currentChunks,
        ITextEmbeddingClient embeddingClient,
        VectorKnowledgeIndexCache cache)
    {
        if (cache.EmbeddingDimensions <= 0 || cache.Chunks is null)
        {
            throw new InvalidOperationException("Vector index cache metadata is invalid.");
        }

        if (cache.Chunks.Count != currentChunks.Count)
        {
            throw new InvalidOperationException("Vector index cache chunk count is invalid.");
        }

        VectorKnowledgeChunk[] vectorChunks = new VectorKnowledgeChunk[currentChunks.Count];
        for (int index = 0; index < currentChunks.Count; index++)
        {
            KnowledgeChunk currentChunk = currentChunks[index];
            VectorKnowledgeIndexCacheEntry cachedChunk = cache.Chunks[index];
            if (!string.Equals(cachedChunk.SourcePath, currentChunk.SourcePath, StringComparison.Ordinal)
                || cachedChunk.ChunkNumber != currentChunk.ChunkNumber
                || !string.Equals(cachedChunk.Content, currentChunk.Content, StringComparison.Ordinal)
                || cachedChunk.Embedding is null)
            {
                throw new InvalidOperationException(
                    $"Vector index cache chunk #{index + 1} does not match the current document content.");
            }

            vectorChunks[index] = new VectorKnowledgeChunk(
                currentChunk,
                NormalizeEmbedding(cachedChunk.Embedding, cache.EmbeddingDimensions));
        }

        return new VectorKnowledgeIndex(
            embeddingClient,
            vectorChunks,
            cache.EmbeddingDimensions,
            loadedFromCache: true);
    }

    private VectorKnowledgeIndexCache CreateCache(
        string embeddingModel,
        int chunkSize,
        int chunkOverlap,
        string documentFingerprint)
    {
        VectorKnowledgeIndexCacheEntry[] entries = _chunks
            .Select(chunk => new VectorKnowledgeIndexCacheEntry(
                chunk.Chunk.SourcePath,
                chunk.Chunk.ChunkNumber,
                chunk.Chunk.Content,
                chunk.NormalizedEmbedding))
            .ToArray();

        return new VectorKnowledgeIndexCache(
            VectorKnowledgeIndexCacheStore.CurrentFormatVersion,
            embeddingModel,
            chunkSize,
            chunkOverlap,
            documentFingerprint,
            _embeddingDimensions,
            entries);
    }

    private static float[] NormalizeEmbedding(float[] embedding, int expectedDimensions)
    {
        if (embedding.Length != expectedDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension changed from {expectedDimensions} to {embedding.Length}.");
        }

        double sumOfSquares = 0;
        foreach (float value in embedding)
        {
            sumOfSquares += value * value;
        }

        double length = Math.Sqrt(sumOfSquares);
        if (length == 0)
        {
            throw new InvalidOperationException("Embedding service returned a zero-length vector.");
        }

        return embedding.Select(value => (float)(value / length)).ToArray();
    }

    private static double CalculateDotProduct(
        IReadOnlyList<float> first,
        IReadOnlyList<float> second)
    {
        double sum = 0;
        for (int index = 0; index < first.Count; index++)
        {
            sum += first[index] * second[index];
        }

        return sum;
    }

    private sealed record VectorKnowledgeChunk(
        KnowledgeChunk Chunk,
        float[] NormalizedEmbedding);
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Knowledge/VectorKnowledgeIndexCacheStore.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Knowledge/VectorKnowledgeIndexCacheStore.cs</code></summary>

`````csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Reads and atomically writes the persisted vector index cache.
/// </summary>
internal static class VectorKnowledgeIndexCacheStore
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static async Task<VectorKnowledgeIndexCache?> LoadAsync(
        string cacheFilePath,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(cacheFilePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(fullPath);
            VectorKnowledgeIndexCache? cache = await JsonSerializer.DeserializeAsync<VectorKnowledgeIndexCache>(
                stream,
                JsonOptions,
                cancellationToken);
            return cache
                ?? throw new InvalidOperationException($"Vector index cache '{fullPath}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Vector index cache '{fullPath}' contains invalid JSON.",
                exception);
        }
    }

    public static async Task SaveAsync(
        string cacheFilePath,
        VectorKnowledgeIndexCache cache,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(cacheFilePath);
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Vector index cache file must have a parent directory.");
        Directory.CreateDirectory(directoryPath);

        string tempFilePath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken);
            }

            File.Move(tempFilePath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public static string BuildDocumentFingerprint(IReadOnlyList<KnowledgeChunk> chunks)
    {
        StringBuilder canonicalText = new();
        foreach (KnowledgeChunk chunk in chunks)
        {
            AppendLengthPrefixed(canonicalText, chunk.SourcePath);
            canonicalText.Append(chunk.ChunkNumber).Append(':');
            AppendLengthPrefixed(canonicalText, chunk.Content);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText.ToString())));
    }

    private static void AppendLengthPrefixed(StringBuilder output, string value)
    {
        output.Append(value.Length).Append(':').Append(value).Append(';');
    }
}

internal sealed record VectorKnowledgeIndexCache(
    int FormatVersion,
    string? EmbeddingModel,
    int ChunkSize,
    int ChunkOverlap,
    string? DocumentFingerprint,
    int EmbeddingDimensions,
    IReadOnlyList<VectorKnowledgeIndexCacheEntry>? Chunks);

internal sealed record VectorKnowledgeIndexCacheEntry(
    string? SourcePath,
    int ChunkNumber,
    string? Content,
    float[]? Embedding);
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.Core/Skills/AgentToolExecutionContext.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.Core/Skills/AgentToolExecutionContext.cs</code></summary>

`````csharp
using System.Security.Cryptography;
using System.Text;

namespace AgentLearning.Core.Skills;

/// <summary>
/// Trusted metadata that identifies one logical tool call across retries.
/// </summary>
public sealed class AgentToolExecutionContext
{
    public AgentToolExecutionContext(string runId, string toolCallId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);

        RunId = runId.Trim();
        ToolCallId = toolCallId.Trim();
        IdempotencyKey = BuildIdempotencyKey(RunId, ToolCallId);
    }

    public string RunId { get; }

    public string ToolCallId { get; }

    public string IdempotencyKey { get; }

    /// <summary>
    /// Creates a unique context for a local command that is not part of an agent run.
    /// </summary>
    public static AgentToolExecutionContext CreateLocalCommand()
    {
        string id = Guid.NewGuid().ToString("N");
        return new AgentToolExecutionContext($"local_{id}", $"call_{id}");
    }

    private static string BuildIdempotencyKey(string runId, string toolCallId)
    {
        byte[] source = Encoding.UTF8.GetBytes($"{runId}\n{toolCallId}");
        return Convert.ToHexString(SHA256.HashData(source));
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.McpServer/AgentLearning.McpServer.csproj -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.McpServer/AgentLearning.McpServer.csproj</code></summary>

`````xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AgentLearning.Core\AgentLearning.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.9" />
    <PackageReference Include="ModelContextProtocol" Version="1.4.1" />
  </ItemGroup>

</Project>
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.McpServer/KnowledgeTools.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.McpServer/KnowledgeTools.cs</code></summary>

`````csharp
using AgentLearning.Core.Knowledge;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace AgentLearning.McpServer;

/// <summary>
/// Read-only tools for retrieving relevant chunks from the local knowledge base.
/// </summary>
[McpServerToolType]
public static class KnowledgeTools
{
    [McpServerTool(
        Name = "search_knowledge",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Searches the learner's Markdown knowledge base and returns relevant source chunks.")]
    public static async Task<string> SearchKnowledgeAsync(
        [Description("The question or topic to search for.")] string query,
        VectorKnowledgeIndex knowledgeIndex,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<KnowledgeSearchResult> results;
        try
        {
            results = await knowledgeIndex.SearchAsync(query, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new McpException(exception.Message);
        }

        if (results.Count == 0)
        {
            return "No relevant knowledge was found.";
        }

        StringBuilder output = new("Knowledge search results:");
        for (int index = 0; index < results.Count; index++)
        {
            KnowledgeChunk chunk = results[index].Chunk;
            output.AppendLine();
            output.AppendLine();
            output.AppendLine($"[{index + 1}] Source: {chunk.SourcePath} (chunk {chunk.ChunkNumber})");
            output.Append(chunk.Content);
        }

        return output.ToString();
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.McpServer/LearningProgressTools.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.McpServer/LearningProgressTools.cs</code></summary>

`````csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AgentLearning.McpServer;

/// <summary>
/// Read-only learning tools exposed by the standalone MCP server.
/// </summary>
[McpServerToolType]
public static class LearningProgressTools
{
    [McpServerTool(Name = "get_learning_progress", ReadOnly = true)]
    [Description("Returns the learner's current C# Agent development progress and the next recommended topic.")]
    public static string GetLearningProgress()
    {
        return """
            Agent 学习进度：
            - 已完成：角色设定、记忆、原生 Tool Calling、Tool Router。
            - 已完成：Agent Harness、状态机、保护规则、人工确认、Checkpoint 恢复。
            - 已完成：工具幂等键与重复执行保护。
            - 已完成：通过 stdio 连接独立 MCP Server，并动态发现和调用工具。
            - 已完成：write_note 已迁移到 MCP，同时保留审批、恢复和幂等保护。
            - 已完成：第一版 RAG，包含 Markdown 加载、Chunk 切分和关键词检索。
            - 已完成：通过 LM Studio 生成 Embedding，并使用余弦相似度检索。
            - 当前学习：向量索引持久化与文档指纹校验。
            - 下一步：组合关键词与向量分数，实现混合检索。
            """;
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.McpServer/LmStudioEmbeddingClient.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.McpServer/LmStudioEmbeddingClient.cs</code></summary>

`````csharp
using AgentLearning.Core.Knowledge;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AgentLearning.McpServer;

/// <summary>
/// Calls an LM Studio OpenAI-compatible embeddings endpoint.
/// </summary>
public sealed class LmStudioEmbeddingClient : ITextEmbeddingClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public LmStudioEmbeddingClient(string baseUrl, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        string normalizedBaseUrl = baseUrl.Trim().TrimEnd('/') + "/";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _model = model.Trim();
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0 || inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Embedding inputs must contain non-empty text.", nameof(inputs));
        }

        EmbeddingRequest request = new(_model, inputs);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Could not connect to the LM Studio embeddings endpoint '{_httpClient.BaseAddress}'.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"LM Studio embeddings request failed with HTTP {(int)response.StatusCode}: {errorBody}");
            }

            EmbeddingResponse? result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
                cancellationToken: cancellationToken);
            if (result?.Data is null)
            {
                throw new InvalidOperationException("LM Studio returned an invalid embeddings response.");
            }

            EmbeddingData[] orderedData = result.Data
                .OrderBy(item => item.Index)
                .ToArray();
            if (orderedData.Length != inputs.Count)
            {
                throw new InvalidOperationException(
                    $"LM Studio returned {orderedData.Length} vectors for {inputs.Count} inputs.");
            }

            return orderedData.Select(item => item.Embedding).ToArray();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.McpServer/NoteTools.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.McpServer/NoteTools.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentLearning.McpServer;

/// <summary>
/// Note tools that modify the configured local Markdown file.
/// </summary>
[McpServerToolType]
public static class NoteTools
{
    private const string RunIdMetaKey = "io.grimoire/run-id";
    private const string ToolCallIdMetaKey = "io.grimoire/tool-call-id";
    private const string IdempotencyKeyMetaKey = "io.grimoire/idempotency-key";

    [McpServerTool(
        Name = "write_note",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Appends a note to the learner's local Markdown notes file.")]
    public static Task<string> WriteNoteAsync(
        [Description("The note content to append.")] string note,
        WriteNoteSkill writeNoteSkill,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new McpException("write_note requires a non-empty note.");
        }

        AgentToolExecutionContext executionContext = ReadExecutionContext(requestContext);
        string argumentsJson = JsonSerializer.Serialize(new { note });

        return writeNoteSkill.ExecuteAsync(argumentsJson, executionContext, cancellationToken);
    }

    private static AgentToolExecutionContext ReadExecutionContext(
        RequestContext<CallToolRequestParams> requestContext)
    {
        JsonObject? meta = requestContext.Params?.Meta;
        string runId = ReadRequiredMeta(meta, RunIdMetaKey);
        string toolCallId = ReadRequiredMeta(meta, ToolCallIdMetaKey);
        string receivedIdempotencyKey = ReadRequiredMeta(meta, IdempotencyKeyMetaKey);
        AgentToolExecutionContext executionContext = new(runId, toolCallId);

        if (!string.Equals(
                receivedIdempotencyKey,
                executionContext.IdempotencyKey,
                StringComparison.Ordinal))
        {
            throw new McpException("The MCP idempotency metadata does not match the tool call identity.");
        }

        return executionContext;
    }

    private static string ReadRequiredMeta(JsonObject? meta, string propertyName)
    {
        if (meta?[propertyName] is not JsonValue value
            || !value.TryGetValue(out string? text)
            || string.IsNullOrWhiteSpace(text))
        {
            throw new McpException($"Missing required MCP metadata '{propertyName}'.");
        }

        return text.Trim();
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD src/AgentLearning.McpServer/Program.cs -->
<details>
<summary><strong>新建</strong> <code>src/AgentLearning.McpServer/Program.cs</code></summary>

`````csharp
using AgentLearning.Core.Knowledge;
using AgentLearning.Core.Skills;
using AgentLearning.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string notesFilePath = builder.Configuration["notes-file"]
    ?? throw new InvalidOperationException("The MCP server requires the --notes-file option.");
string knowledgeDirectoryPath = builder.Configuration["knowledge-directory"]
    ?? throw new InvalidOperationException("The MCP server requires the --knowledge-directory option.");
string knowledgeIndexFilePath = builder.Configuration["knowledge-index-file"]
    ?? throw new InvalidOperationException("The MCP server requires the --knowledge-index-file option.");
string embeddingBaseUrl = builder.Configuration["embedding-base-url"]
    ?? throw new InvalidOperationException("The MCP server requires the --embedding-base-url option.");
string embeddingModel = builder.Configuration["embedding-model"]
    ?? throw new InvalidOperationException("The MCP server requires the --embedding-model option.");
using LmStudioEmbeddingClient embeddingClient = new(embeddingBaseUrl, embeddingModel);
VectorKnowledgeIndex knowledgeIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
    knowledgeDirectoryPath,
    knowledgeIndexFilePath,
    embeddingModel,
    embeddingClient);
Console.Error.WriteLine(
    $"Knowledge vector index: {(knowledgeIndex.LoadedFromCache ? "loaded from cache" : "rebuilt")}, "
    + $"{knowledgeIndex.ChunkCount} chunks, {knowledgeIndex.EmbeddingDimensions} dimensions.");

// stdout is reserved for MCP JSON-RPC messages when using the stdio transport.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(new WriteNoteSkill(notesFilePath));
builder.Services.AddSingleton(knowledgeIndex);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD tests/AgentLearning.Core.Tests/AgentToolExecutionContextTests.cs -->
<details>
<summary><strong>新建</strong> <code>tests/AgentLearning.Core.Tests/AgentToolExecutionContextTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolExecutionContextTests
{
    [Fact]
    public void Constructor_builds_same_key_for_same_logical_tool_call()
    {
        AgentToolExecutionContext first = new("run_123", "call_456");
        AgentToolExecutionContext second = new("run_123", "call_456");

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(64, first.IdempotencyKey.Length);
    }

    [Fact]
    public void Constructor_builds_different_key_for_different_tool_call()
    {
        AgentToolExecutionContext first = new("run_123", "call_456");
        AgentToolExecutionContext second = new("run_123", "call_789");

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD tests/AgentLearning.Core.Tests/KeywordKnowledgeIndexTests.cs -->
<details>
<summary><strong>新建</strong> <code>tests/AgentLearning.Core.Tests/KeywordKnowledgeIndexTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Knowledge;

namespace AgentLearning.Core.Tests;

public sealed class KeywordKnowledgeIndexTests
{
    [Fact]
    public async Task Search_returns_the_most_relevant_markdown_chunk()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "refund-policy.md"),
                "购买超过七天后，退款请求需要人工审核。");
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "office-hours.md"),
                "客服工作时间是工作日上午九点到下午五点。");
            KeywordKnowledgeIndex index = await KeywordKnowledgeIndex.LoadFromDirectoryAsync(
                directoryPath,
                chunkSize: 40,
                chunkOverlap: 10);

            IReadOnlyList<KnowledgeSearchResult> results = index.Search("超过七天退款怎么办");

            KnowledgeSearchResult firstResult = Assert.IsType<KnowledgeSearchResult>(results[0]);
            Assert.Equal("refund-policy.md", firstResult.Chunk.SourcePath);
            Assert.Contains("人工审核", firstResult.Chunk.Content);
            Assert.True(firstResult.Score > 0);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task Search_returns_empty_when_no_keyword_matches()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "agent.md"),
                "Agent Harness controls tool execution and approval.");
            KeywordKnowledgeIndex index = await KeywordKnowledgeIndex.LoadFromDirectoryAsync(directoryPath);

            IReadOnlyList<KnowledgeSearchResult> results = index.Search("量子纠缠实验");

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_rejects_directory_without_markdown_files()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => KeywordKnowledgeIndex.LoadFromDirectoryAsync(directoryPath));

            Assert.Contains("contains no Markdown files", exception.Message);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"keyword-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD tests/AgentLearning.Core.Tests/McpSkillClientTests.cs -->
<details>
<summary><strong>新建</strong> <code>tests/AgentLearning.Core.Tests/McpSkillClientTests.cs</code></summary>

`````csharp
using AgentLearning.App;
using AgentLearning.Core;
using AgentLearning.Core.Skills;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class McpSkillClientTests
{
    [Fact]
    public async Task ConnectStdioAsync_discovers_and_calls_registered_tools()
    {
        string serverAssemblyPath = ResolveMcpServerAssemblyPath();
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"mcp-tools-{Guid.NewGuid():N}");
        string notesFilePath = Path.Combine(tempDirectory, "notes.md");
        string knowledgeDirectoryPath = Path.Combine(tempDirectory, "knowledge");
        string knowledgeIndexFilePath = Path.Combine(tempDirectory, "knowledge-vector-index.json");
        Directory.CreateDirectory(knowledgeDirectoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(knowledgeDirectoryPath, "refund-policy.md"),
            "购买超过七天后，需要人工审核退款请求。");
        await using FakeEmbeddingHttpServer embeddingServer = FakeEmbeddingHttpServer.Start();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        Dictionary<string, McpToolPolicy> policies = new(StringComparer.Ordinal)
        {
            ["get_learning_progress"] = new(
                AgentSkillRiskLevel.Low,
                RequiresConfirmation: false),
            ["search_knowledge"] = new(
                AgentSkillRiskLevel.Low,
                RequiresConfirmation: false),
            ["write_note"] = new(
                AgentSkillRiskLevel.Medium,
                RequiresConfirmation: true)
        };

        await using McpSkillClient client = await McpSkillClient.ConnectStdioAsync(
            serverName: "test-learning-tools",
            command: "dotnet",
            arguments:
            [
                serverAssemblyPath,
                "--notes-file",
                notesFilePath,
                "--knowledge-directory",
                knowledgeDirectoryPath,
                "--knowledge-index-file",
                knowledgeIndexFilePath,
                "--embedding-base-url",
                embeddingServer.BaseUrl,
                "--embedding-model",
                "test-embedding-model"
            ],
            toolPolicies: policies,
            cancellationToken: timeout.Token);

        Assert.Equal(3, client.Skills.Count);

        IAgentSkill progressSkill = Assert.Single(
            client.Skills.Where(skill => skill.Name == "get_learning_progress"));
        Assert.Equal(AgentSkillRiskLevel.Low, progressSkill.RiskLevel);
        Assert.False(progressSkill.RequiresConfirmation);
        Assert.Contains("\"type\":\"object\"", progressSkill.ParametersJson);

        string progressResult = await progressSkill.ExecuteAsync(
            "{}",
            new AgentToolExecutionContext("run_mcp_test", "call_mcp_test"),
            timeout.Token);

        Assert.Contains("向量索引持久化", progressResult);
        Assert.Contains("实现混合检索", progressResult);

        IAgentSkill searchKnowledgeSkill = Assert.Single(
            client.Skills.Where(skill => skill.Name == "search_knowledge"));
        Assert.Equal(AgentSkillRiskLevel.Low, searchKnowledgeSkill.RiskLevel);
        Assert.False(searchKnowledgeSkill.RequiresConfirmation);
        Assert.Contains("\"query\"", searchKnowledgeSkill.ParametersJson);
        Assert.DoesNotContain("knowledgeIndex", searchKnowledgeSkill.ParametersJson);

        string searchResult = await searchKnowledgeSkill.ExecuteAsync(
            """{"query":"超过七天退款怎么办"}""",
            new AgentToolExecutionContext("run_mcp_search", "call_mcp_search"),
            timeout.Token);

        Assert.Contains("Source: refund-policy.md", searchResult);
        Assert.Contains("人工审核退款请求", searchResult);

        IAgentSkill writeNoteSkill = Assert.Single(
            client.Skills.Where(skill => skill.Name == "write_note"));
        Assert.Equal(AgentSkillRiskLevel.Medium, writeNoteSkill.RiskLevel);
        Assert.True(writeNoteSkill.RequiresConfirmation);
        Assert.Contains("\"note\"", writeNoteSkill.ParametersJson);
        Assert.DoesNotContain("writeNoteSkill", writeNoteSkill.ParametersJson);
        Assert.DoesNotContain("requestContext", writeNoteSkill.ParametersJson);
        Assert.DoesNotContain("cancellationToken", writeNoteSkill.ParametersJson);
        Assert.True(AgentToolPermissionPolicy.RequiresConfirmation(writeNoteSkill));

        AgentToolExecutionContext writeContext = new("run_mcp_note", "call_mcp_note");
        const string noteArguments = """{"note":"MCP writes this note exactly once."}""";
        string firstWriteResult = await writeNoteSkill.ExecuteAsync(
            noteArguments,
            writeContext,
            timeout.Token);
        string secondWriteResult = await writeNoteSkill.ExecuteAsync(
            noteArguments,
            writeContext,
            timeout.Token);

        string savedText = await File.ReadAllTextAsync(notesFilePath, timeout.Token);
        Assert.Equal(firstWriteResult, secondWriteResult);
        Assert.Contains("Note saved to", firstWriteResult);
        Assert.Equal(1, CountOccurrences(savedText, writeContext.IdempotencyKey));
        Assert.Equal(1, CountOccurrences(savedText, "MCP writes this note exactly once."));
    }

    private static string ResolveMcpServerAssemblyPath()
    {
        DirectoryInfo solutionDirectory = FindSolutionDirectory();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        string assemblyPath = Path.Combine(
            solutionDirectory.FullName,
            "src",
            "AgentLearning.McpServer",
            "bin",
            configuration,
            "net8.0",
            "AgentLearning.McpServer.dll");

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("The MCP server test assembly was not found.", assemblyPath);
        }

        return assemblyPath;
    }

    private static DirectoryInfo FindSolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentLearning.sln")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate AgentLearning.sln from the test output directory.");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private sealed class FakeEmbeddingHttpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serverTask;

        private FakeEmbeddingHttpServer(int port)
        {
            BaseUrl = $"http://127.0.0.1:{port}/v1";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _serverTask = RunAsync();
        }

        public string BaseUrl { get; }

        public static FakeEmbeddingHttpServer Start()
        {
            TcpListener portFinder = new(IPAddress.Loopback, 0);
            portFinder.Start();
            int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
            portFinder.Stop();
            return new FakeEmbeddingHttpServer(port);
        }

        public async ValueTask DisposeAsync()
        {
            _stopping.Cancel();
            _listener.Stop();
            _listener.Close();

            try
            {
                await _serverTask;
            }
            catch (HttpListenerException) when (_stopping.IsCancellationRequested)
            {
            }

            _stopping.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(_stopping.Token);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }

                await WriteEmbeddingResponseAsync(context);
            }
        }

        private static async Task WriteEmbeddingResponseAsync(HttpListenerContext context)
        {
            using JsonDocument request = await JsonDocument.ParseAsync(context.Request.InputStream);
            JsonElement input = request.RootElement.GetProperty("input");
            string[] texts = input.ValueKind == JsonValueKind.Array
                ? input.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : [input.GetString() ?? string.Empty];
            object[] data = texts
                .Select((text, index) => new
                {
                    index,
                    embedding = text.Contains("退款", StringComparison.Ordinal)
                        || text.Contains("七天", StringComparison.Ordinal)
                        ? new[] { 1f, 0f }
                        : new[] { 0f, 1f }
                })
                .Cast<object>()
                .ToArray();
            byte[] responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { data }));

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes);
            context.Response.Close();
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: ADD tests/AgentLearning.Core.Tests/VectorKnowledgeIndexTests.cs -->
<details>
<summary><strong>新建</strong> <code>tests/AgentLearning.Core.Tests/VectorKnowledgeIndexTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Knowledge;
using System.Text.Json;

namespace AgentLearning.Core.Tests;

public sealed class VectorKnowledgeIndexTests
{
    [Fact]
    public async Task SearchAsync_returns_chunk_with_highest_cosine_similarity()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "approval.md"),
                "Harness requires approval before risky tools execute.");
            await File.WriteAllTextAsync(
                Path.Combine(directoryPath, "weather.md"),
                "Sunny weather is suitable for walking outside.");
            QueueEmbeddingClient embeddingClient = new(
            [
                [[1, 0], [0, 1]],
                [[0.9f, 0.1f]]
            ]);
            VectorKnowledgeIndex index = await VectorKnowledgeIndex.LoadFromDirectoryAsync(
                directoryPath,
                embeddingClient);

            IReadOnlyList<KnowledgeSearchResult> results = await index.SearchAsync(
                "How are dangerous tools controlled?");

            KnowledgeSearchResult result = Assert.Single(results);
            Assert.Equal("approval.md", result.Chunk.SourcePath);
            Assert.True(result.Score > 0.9);
            Assert.Equal(2, index.EmbeddingDimensions);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_rejects_query_vector_with_changed_dimensions()
    {
        string directoryPath = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            QueueEmbeddingClient embeddingClient = new(
            [
                [[1, 0]],
                [[1, 0, 0]]
            ]);
            VectorKnowledgeIndex index = await VectorKnowledgeIndex.LoadFromDirectoryAsync(
                directoryPath,
                embeddingClient);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => index.SearchAsync("Agent question"));

            Assert.Contains("dimension changed", exception.Message);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_second_load_uses_persisted_vectors()
    {
        string directoryPath = CreateTempDirectory();
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            QueueEmbeddingClient firstClient = new([[[1, 0]]]);

            VectorKnowledgeIndex firstIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                firstClient);
            QueueEmbeddingClient secondClient = new([]);
            VectorKnowledgeIndex secondIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                secondClient);

            Assert.False(firstIndex.LoadedFromCache);
            Assert.True(secondIndex.LoadedFromCache);
            Assert.Equal(1, firstClient.CallCount);
            Assert.Equal(0, secondClient.CallCount);
            Assert.True(File.Exists(cacheFilePath));

            using JsonDocument cache = JsonDocument.Parse(await File.ReadAllTextAsync(cacheFilePath));
            Assert.Equal("embedding-model-v1", cache.RootElement.GetProperty("embedding_model").GetString());
            Assert.Equal(1, cache.RootElement.GetProperty("chunks").GetArrayLength());
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_document_change_rebuilds_cache()
    {
        string directoryPath = CreateTempDirectory();
        string documentPath = Path.Combine(directoryPath, "agent.md");
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(documentPath, "Original agent knowledge.");
            await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                new QueueEmbeddingClient([[[1, 0]]]));
            await File.WriteAllTextAsync(documentPath, "Updated agent knowledge.");
            QueueEmbeddingClient updatedClient = new([[[0, 1]]]);

            VectorKnowledgeIndex updatedIndex = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                updatedClient);

            Assert.False(updatedIndex.LoadedFromCache);
            Assert.Equal(1, updatedClient.CallCount);
            Assert.Contains("Updated agent knowledge", await File.ReadAllTextAsync(cacheFilePath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_embedding_model_change_rebuilds_cache()
    {
        string directoryPath = CreateTempDirectory();
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v1",
                new QueueEmbeddingClient([[[1, 0]]]));
            QueueEmbeddingClient newModelClient = new([[[0, 1]]]);

            VectorKnowledgeIndex index = await VectorKnowledgeIndex.LoadOrCreateAsync(
                directoryPath,
                cacheFilePath,
                "embedding-model-v2",
                newModelClient);

            Assert.False(index.LoadedFromCache);
            Assert.Equal(1, newModelClient.CallCount);
            Assert.Contains("embedding-model-v2", await File.ReadAllTextAsync(cacheFilePath));
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_invalid_cache_json_fails_clearly()
    {
        string directoryPath = CreateTempDirectory();
        string cacheFilePath = Path.Combine(directoryPath, "index.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "agent.md"), "Agent knowledge.");
            await File.WriteAllTextAsync(cacheFilePath, "{invalid-json");

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => VectorKnowledgeIndex.LoadOrCreateAsync(
                    directoryPath,
                    cacheFilePath,
                    "embedding-model-v1",
                    new QueueEmbeddingClient([])));

            Assert.Contains("contains invalid JSON", exception.Message);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"vector-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private sealed class QueueEmbeddingClient(
        IEnumerable<IReadOnlyList<float[]>> responses) : ITextEmbeddingClient
    {
        private readonly Queue<IReadOnlyList<float[]>> _responses = new(responses);

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected embedding request in test.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE AgentLearning.sln -->
<details>
<summary><strong>完整覆盖</strong> <code>AgentLearning.sln</code></summary>

`````text

Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{827E0CD3-B72D-47B6-A68D-7590B98EB39B}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AgentLearning.App", "src\AgentLearning.App\AgentLearning.App.csproj", "{2879E30F-C422-4DDE-BF83-5E485002EC3B}"
EndProject
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", "tests", "{0AB3BF05-4346-4AA6-1389-037BE0695223}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AgentLearning.Core.Tests", "tests\AgentLearning.Core.Tests\AgentLearning.Core.Tests.csproj", "{36E36975-E72D-4819-B439-D6B61B749D83}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AgentLearning.Core", "src\AgentLearning.Core\AgentLearning.Core.csproj", "{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AgentLearning.McpServer", "src\AgentLearning.McpServer\AgentLearning.McpServer.csproj", "{662AF317-0A82-4F02-8076-5FDBA7B87C53}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Debug|x64 = Debug|x64
		Debug|x86 = Debug|x86
		Release|Any CPU = Release|Any CPU
		Release|x64 = Release|x64
		Release|x86 = Release|x86
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Debug|x64.ActiveCfg = Debug|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Debug|x64.Build.0 = Debug|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Debug|x86.ActiveCfg = Debug|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Debug|x86.Build.0 = Debug|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Release|Any CPU.Build.0 = Release|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Release|x64.ActiveCfg = Release|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Release|x64.Build.0 = Release|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Release|x86.ActiveCfg = Release|Any CPU
		{2879E30F-C422-4DDE-BF83-5E485002EC3B}.Release|x86.Build.0 = Release|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Debug|x64.ActiveCfg = Debug|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Debug|x64.Build.0 = Debug|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Debug|x86.ActiveCfg = Debug|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Debug|x86.Build.0 = Debug|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Release|Any CPU.Build.0 = Release|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Release|x64.ActiveCfg = Release|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Release|x64.Build.0 = Release|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Release|x86.ActiveCfg = Release|Any CPU
		{36E36975-E72D-4819-B439-D6B61B749D83}.Release|x86.Build.0 = Release|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Debug|x64.ActiveCfg = Debug|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Debug|x64.Build.0 = Debug|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Debug|x86.ActiveCfg = Debug|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Debug|x86.Build.0 = Debug|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Release|Any CPU.Build.0 = Release|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Release|x64.ActiveCfg = Release|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Release|x64.Build.0 = Release|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Release|x86.ActiveCfg = Release|Any CPU
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF}.Release|x86.Build.0 = Release|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Debug|x64.ActiveCfg = Debug|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Debug|x64.Build.0 = Debug|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Debug|x86.ActiveCfg = Debug|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Debug|x86.Build.0 = Debug|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Release|Any CPU.Build.0 = Release|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Release|x64.ActiveCfg = Release|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Release|x64.Build.0 = Release|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Release|x86.ActiveCfg = Release|Any CPU
		{662AF317-0A82-4F02-8076-5FDBA7B87C53}.Release|x86.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
	GlobalSection(NestedProjects) = preSolution
		{2879E30F-C422-4DDE-BF83-5E485002EC3B} = {827E0CD3-B72D-47B6-A68D-7590B98EB39B}
		{36E36975-E72D-4819-B439-D6B61B749D83} = {0AB3BF05-4346-4AA6-1389-037BE0695223}
		{C4A85CBC-FD8B-4CF8-814F-1C7F511110EF} = {827E0CD3-B72D-47B6-A68D-7590B98EB39B}
		{662AF317-0A82-4F02-8076-5FDBA7B87C53} = {827E0CD3-B72D-47B6-A68D-7590B98EB39B}
	EndGlobalSection
EndGlobal
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.App/AgentLearning.App.csproj -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.App/AgentLearning.App.csproj</code></summary>

`````xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\AgentLearning.Core\AgentLearning.Core.csproj" />
    <ProjectReference Include="..\AgentLearning.McpServer\AgentLearning.McpServer.csproj" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.4.1" />
    <PackageReference Include="OpenAI" Version="2.12.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="agent.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="agent.local.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="knowledge/**/*.md">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <Target Name="CopyMcpServerOutput" AfterTargets="Build">
    <ItemGroup>
      <McpServerOutput Include="$(MSBuildProjectDirectory)/../AgentLearning.McpServer/bin/$(Configuration)/net8.0/**/*" />
    </ItemGroup>
    <Copy
      SourceFiles="@(McpServerOutput)"
      DestinationFiles="@(McpServerOutput->'$(OutDir)mcp-server/%(RecursiveDir)%(Filename)%(Extension)')"
      SkipUnchangedFiles="true" />
  </Target>

</Project>
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.App/AgentRunner.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.App/AgentRunner.cs</code></summary>

`````csharp
using AgentLearning.Core;
using AgentLearning.Core.Diagnostics;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI.Chat;
using System.Text;

namespace AgentLearning.App;

/// <summary>
/// Agent 的运行骨架。
/// 它把“记忆、上下文、模型调用、工具调用、工具观察、最终回答”放进一个可控循环里。
/// </summary>
public sealed class AgentRunner
{
    private readonly AgentProfile _profile;
    private readonly IAgentChatClient _client;
    private readonly ChatMemory _memory;
    private readonly string _memoryPath;
    private readonly AgentSkillRegistry _skillRegistry;

    public AgentRunner(
        AgentProfile profile,
        ChatClient client,
        ChatMemory memory,
        string memoryPath,
        AgentSkillRegistry skillRegistry)
        : this(profile, new OpenAIChatClientAdapter(client), memory, memoryPath, skillRegistry)
    {
    }

    public AgentRunner(
        AgentProfile profile,
        IAgentChatClient client,
        ChatMemory memory,
        string memoryPath,
        AgentSkillRegistry skillRegistry)
    {
        _profile = profile;
        _client = client;
        _memory = memory;
        _memoryPath = memoryPath;
        _skillRegistry = skillRegistry;
    }

    /// <summary>创建工作流步骤时触发，Program.cs 可以选择打印到控制台。</summary>
    public event Action<AgentWorkflowStep>? WorkflowStepCreated;

    /// <summary>创建调试文本时触发，Program.cs 可以选择打印到控制台。</summary>
    public event Action<string>? DebugMessageCreated;

    /// <summary>Agent 运行状态变化时触发，Program.cs 可以选择展示给用户或 UI。</summary>
    public event Action<AgentRunSnapshot>? StateChanged;

    /// <summary>Raised when the runner creates or updates a checkpoint.</summary>
    public Func<AgentRunCheckpoint, Task>? CheckpointCreatedAsync { get; set; }

    /// <summary>Raised when a checkpoint is fully completed and can be removed.</summary>
    public Func<AgentRunCheckpoint, Task>? CheckpointConsumedAsync { get; set; }

    /// <summary>
    /// 运行一轮 Agent。
    /// 这里是 Harness 的核心：模型可以决定调用工具，但循环边界和记忆保存由代码控制。
    /// </summary>
    public async Task<AgentRunResult> RunAsync(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("User input cannot be empty.", nameof(userInput));
        }

        AgentWorkflowTrace workflowTrace = new();
        AgentRunState runState = new();
        string runId = $"run_{Guid.NewGuid():N}";

        AgentMemoryWritePolicy memoryWritePolicy = new(_profile.MaxMemoryContentChars);
        bool shouldSaveUserInput = memoryWritePolicy.ShouldWrite(userInput);

        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.ReceiveInput,
            "Receive user input",
            shouldSaveUserInput
                ? "User message is eligible for memory."
                : "User message will only be used in this turn.");

        IReadOnlyList<ChatTurn> contextTurns = ChatMemoryWindow.GetRecentTurns(_memory, _profile.MaxMemoryTurns);
        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.BuildContext,
            "Build context window",
            $"Sending {contextTurns.Count} of {_memory.Turns.Count} saved memory turns plus current input.");

        List<ChatMessage> messages = BuildMessages(contextTurns);
        List<AgentDebugMessage> debugMessages = BuildDebugMessages(contextTurns);
        AddCurrentUserInput(messages, debugMessages, userInput);

        AgentLoopResult loopResult = _profile.Stream
            ? AgentLoopResult.Completed(await CompleteStreamingAsync(messages))
            : await CompleteOnceAsync(runId, userInput, messages, debugMessages, workflowTrace, runState);

        if (loopResult.PendingApproval is not null)
        {
            return new AgentRunResult(
                AgentRunOutcome.WaitingForApproval,
                AssistantReply: null,
                loopResult.PendingApproval,
                workflowTrace,
                runState.ToSnapshot());
        }

        string assistantReply = loopResult.AssistantReply
            ?? throw new InvalidOperationException("A completed agent run must contain an assistant reply.");

        if (string.IsNullOrWhiteSpace(assistantReply))
        {
            throw new InvalidOperationException("The model returned no text content.");
        }

        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.Finish,
            "Finish",
            "Final answer was produced.");

        bool shouldSaveAssistantReply = memoryWritePolicy.ShouldWrite(assistantReply);
        if (shouldSaveUserInput && shouldSaveAssistantReply)
        {
            _memory.AddUserMessage(userInput);
            _memory.AddAssistantMessage(assistantReply);
            await ChatMemoryStore.SaveAsync(_memoryPath, _memory);
        }

        return new AgentRunResult(
            AgentRunOutcome.Completed,
            assistantReply,
            PendingApproval: null,
            workflowTrace,
            runState.ToSnapshot());
    }

    /// <summary>
    /// Resume a run from a checkpoint and continue through the normal model/tool loop.
    /// </summary>
    public async Task<AgentRunResult> ResumeAsync(AgentRunCheckpoint checkpoint, bool approved)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (checkpoint.Kind == AgentCheckpointKind.PendingToolApproval && CheckpointCreatedAsync is null)
        {
            throw new InvalidOperationException(
                "A checkpoint persistence handler is required before a pending tool can be resumed.");
        }

        AgentWorkflowTrace workflowTrace = new();
        AgentRunState runState = new();

        AgentCheckpointResumeResult resumeResult = await AgentCheckpointResumer.ResumeAsync(
            checkpoint,
            approved,
            _skillRegistry);

        AddWorkflowStep(
            workflowTrace,
            runState,
            resumeResult.ToolExecuted
                ? AgentWorkflowStepKind.ToolExecuted
                : AgentWorkflowStepKind.ToolRejected,
            resumeResult.ToolExecuted ? "Resume approved tool" : "Resume rejected tool",
            resumeResult.Observation,
            toolName: resumeResult.ToolName);

        AgentRunCheckpoint checkpointToConsume = checkpoint;
        if (checkpoint.Kind == AgentCheckpointKind.PendingToolApproval)
        {
            checkpointToConsume = await SaveToolResolvedCheckpointAsync(
                checkpoint,
                resumeResult,
                runState);
        }

        List<ChatMessage> messages = BuildResumeMessages(checkpoint.Messages);
        List<AgentDebugMessage> debugMessages = BuildResumeDebugMessages(checkpoint.Messages);

        // The restored tool observation must match the assistant tool_call_id in the checkpoint.
        messages.Add(new ToolChatMessage(resumeResult.ToolCallId, resumeResult.Observation));
        debugMessages.Add(new AgentDebugMessage
        {
            Role = "tool",
            ToolCallId = resumeResult.ToolCallId,
            Content = resumeResult.Observation
        });

        IReadOnlyList<IAgentSkill> selectedSkills = ResolveSelectedSkills(checkpoint.SelectedToolNames);
        ChatCompletionOptions? options = selectedSkills.Count > 0
            ? BuildChatOptions(selectedSkills)
            : null;

        AgentLoopResult loopResult = await CompleteToolLoopAsync(
            checkpoint.RunId,
            messages,
            debugMessages,
            selectedSkills,
            options,
            workflowTrace,
            runState);

        if (loopResult.PendingApproval is not null)
        {
            return new AgentRunResult(
                AgentRunOutcome.WaitingForApproval,
                AssistantReply: null,
                loopResult.PendingApproval,
                workflowTrace,
                runState.ToSnapshot());
        }

        string assistantReply = loopResult.AssistantReply
            ?? throw new InvalidOperationException("A completed resumed run must contain an assistant reply.");

        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.Finish,
            "Finish",
            "Final answer was produced after resume.");

        await TrySaveResumedMemoryAsync(checkpoint, assistantReply);

        if (CheckpointConsumedAsync is not null)
        {
            await CheckpointConsumedAsync(checkpointToConsume);
        }

        return new AgentRunResult(
            AgentRunOutcome.Completed,
            assistantReply,
            PendingApproval: null,
            workflowTrace,
            runState.ToSnapshot());
    }

    private async Task<AgentRunCheckpoint> SaveToolResolvedCheckpointAsync(
        AgentRunCheckpoint sourceCheckpoint,
        AgentCheckpointResumeResult resumeResult,
        AgentRunState runState)
    {
        AgentRunCheckpoint resolvedCheckpoint = AgentRunCheckpoint.CreateToolResolved(
            sourceCheckpoint.RunId,
            DateTimeOffset.Now,
            runState.ToSnapshot(),
            sourceCheckpoint.Messages,
            sourceCheckpoint.SelectedToolNames,
            ResolvedToolCall.FromResumeResult(resumeResult));

        Func<AgentRunCheckpoint, Task> checkpointHandler = CheckpointCreatedAsync
            ?? throw new InvalidOperationException("A checkpoint persistence handler is required during resume.");
        await checkpointHandler(resolvedCheckpoint);

        return resolvedCheckpoint;
    }

    private async Task<AgentLoopResult> CompleteOnceAsync(
        string runId,
        string userInput,
        List<ChatMessage> messages,
        List<AgentDebugMessage> debugMessages,
        AgentWorkflowTrace workflowTrace,
        AgentRunState runState)
    {
        // native_tool_calling 打开时，先让 AI Tool Router 从轻量目录里选工具。
        // 主 Agent 只会收到被选中的工具完整 Schema。
        IReadOnlyList<IAgentSkill> selectedSkills = await SelectSkillsForCurrentTurnAsync(userInput, workflowTrace, runState);
        ChatCompletionOptions? options = selectedSkills.Count > 0
            ? BuildChatOptions(selectedSkills)
            : null;

        return await CompleteToolLoopAsync(
            runId,
            messages,
            debugMessages,
            selectedSkills,
            options,
            workflowTrace,
            runState);
    }

    private async Task<AgentLoopResult> CompleteToolLoopAsync(
        string runId,
        List<ChatMessage> messages,
        List<AgentDebugMessage> debugMessages,
        IReadOnlyList<IAgentSkill> selectedSkills,
        ChatCompletionOptions? options,
        AgentWorkflowTrace workflowTrace,
        AgentRunState runState)
    {
        AgentToolIterationGuard toolIterationGuard = new(_profile.MaxToolIterations);
        AgentToolResultLimiter toolResultLimiter = new(_profile.MaxToolResultChars);
        AgentToolTimeoutRunner toolTimeoutRunner = new(_profile.ToolTimeoutSeconds);
        int requestNumber = 1;
        while (true)
        {
            AddWorkflowStep(
                workflowTrace,
                runState,
                AgentWorkflowStepKind.AskModel,
                "Ask model",
                $"Request #{requestNumber} sent to the model.");

            EmitChatRequestPreview(debugMessages, requestNumber, selectedSkills);
            ChatCompletion completion = await _client.CompleteChatAsync(messages, options);
            EmitChatResponsePreview(completion);

            // 有些 OpenAI-compatible Router 会返回 tool_calls，但 finish_reason 仍然是 stop。
            // 所以这里优先看 ToolCalls 本身，避免漏掉真正的工具调用请求。
            if (completion.ToolCalls.Count > 0)
            {
                if (!_profile.NativeToolCalling)
                {
                    throw new InvalidOperationException("The model returned tool calls, but native tool calling is disabled.");
                }

                if (completion.ToolCalls.Count > 1)
                {
                    throw new InvalidOperationException(
                        "The model returned multiple tool calls even though parallel tool calls are disabled.");
                }

                toolIterationGuard.RecordToolIteration();
                AgentToolConfirmationRequest? pendingApproval = await ResolveToolCallsAsync(
                    runId,
                    messages,
                    debugMessages,
                    selectedSkills,
                    completion,
                    workflowTrace,
                    runState,
                    toolResultLimiter,
                    toolTimeoutRunner);

                if (pendingApproval is not null)
                {
                    return AgentLoopResult.Paused(pendingApproval);
                }

                requestNumber++;
                continue;
            }

            switch (completion.FinishReason)
            {
                case ChatFinishReason.Stop:
                    return AgentLoopResult.Completed(ReadTextContent(completion));

                case ChatFinishReason.ToolCalls:
                    throw new InvalidOperationException(
                        "The model returned finish_reason 'tool_calls' without any tool calls.");

                case ChatFinishReason.Length:
                    throw new InvalidOperationException("Model output was cut off because it reached the token limit.");

                case ChatFinishReason.ContentFilter:
                    throw new InvalidOperationException("Model output was blocked by the content filter.");

                case ChatFinishReason.FunctionCall:
                    throw new InvalidOperationException("Deprecated function_call was returned. Use tool_calls instead.");

                default:
                    throw new InvalidOperationException($"Unsupported finish reason: {completion.FinishReason}");
            }
        }
    }

    private static string ReadTextContent(ChatCompletion completion)
    {
        return string.Concat(completion.Content.Select(part => part.Text));
    }

    private async Task<IReadOnlyList<IAgentSkill>> SelectSkillsForCurrentTurnAsync(
        string userInput,
        AgentWorkflowTrace workflowTrace,
        AgentRunState runState)
    {
        if (!_profile.NativeToolCalling || _skillRegistry.Skills.Count == 0)
        {
            return [];
        }

        if (!_profile.ToolRouterEnabled)
        {
            AddWorkflowStep(
                workflowTrace,
                runState,
                AgentWorkflowStepKind.RouteTools,
                "Route tools",
                $"Tool router is disabled. Sending all {_skillRegistry.Skills.Count} tools to the main agent.");

            return _skillRegistry.Skills.ToArray();
        }

        string catalogJson = AgentToolCatalogBuilder.BuildJson(_skillRegistry.Skills);
        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.RouteTools,
            "Route tools",
            $"Sending lightweight catalog with {_skillRegistry.Skills.Count} tools to the AI Tool Router.");

        List<ChatMessage> routerMessages = BuildToolRouterMessages(userInput, catalogJson);
        EmitToolRouterRequestPreview(userInput, catalogJson);

        ChatCompletion completion = await _client.CompleteChatAsync(routerMessages);
        string routerJson = ReadRouterTextContent(completion);
        EmitToolRouterResponsePreview(completion, routerJson);

        AgentToolRoutingDecision decision = AgentToolRoutingDecisionParser.Parse(
            routerJson,
            _skillRegistry.Skills,
            _profile.MaxToolsPerRequest);

        IReadOnlyList<IAgentSkill> selectedSkills = ResolveSelectedSkills(decision.SelectedToolNames);
        string selectedToolText = selectedSkills.Count == 0
            ? "no tools"
            : string.Join(", ", selectedSkills.Select(skill => skill.Name));

        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.RouteTools,
            "Route tools result",
            $"Router selected {selectedToolText}. Reason: {decision.Reason}");

        return selectedSkills;
    }

    private static List<ChatMessage> BuildToolRouterMessages(string userInput, string catalogJson)
    {
        return
        [
            new SystemChatMessage(BuildToolRouterSystemInstructions()),
            new UserChatMessage(BuildToolRouterUserMessage(userInput, catalogJson))
        ];
    }

    private static string BuildToolRouterSystemInstructions()
    {
        return """
        You are an AI Tool Router for an Agent Harness.
        Your job is to choose which tools should be exposed to the main agent for the current user message.

        Return only valid JSON. Do not wrap it in Markdown. Do not explain outside JSON.

        JSON shape:
        {
          "need_tools": true,
          "selected_tools": ["exact_tool_name"],
          "reason": "short reason"
        }

        Rules:
        - Use exact tool names from the tool catalog.
        - If no tool is needed, return need_tools=false and selected_tools=[].
        - Choose the smallest useful tool set.
        - You are only routing tools. You are not answering the user.
        """;
    }

    private static string BuildToolRouterUserMessage(string userInput, string catalogJson)
    {
        return $"""
        Current user message:
        {userInput}

        Lightweight tool catalog:
        {catalogJson}
        """;
    }

    private static string ReadRouterTextContent(ChatCompletion completion)
    {
        string text = string.Concat(completion.Content.Select(part => part.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AgentToolRoutingException("Tool router returned no JSON content.");
        }

        return text;
    }

    private IReadOnlyList<IAgentSkill> ResolveSelectedSkills(IReadOnlyList<string> selectedToolNames)
    {
        if (selectedToolNames.Count == 0)
        {
            return [];
        }

        Dictionary<string, IAgentSkill> skillsByName = _skillRegistry.Skills.ToDictionary(
            skill => skill.Name,
            StringComparer.Ordinal);

        return selectedToolNames
            .Select(toolName => skillsByName[toolName])
            .ToArray();
    }

    private async Task<string> CompleteStreamingAsync(List<ChatMessage> messages)
    {
        StringBuilder fullReply = new();

        await foreach (StreamingChatCompletionUpdate update in _client.CompleteChatStreamingAsync(messages))
        {
            if (update.ContentUpdate.Count == 0)
            {
                continue;
            }

            fullReply.Append(update.ContentUpdate[0].Text);
        }

        return fullReply.ToString();
    }

    private async Task<AgentToolConfirmationRequest?> ResolveToolCallsAsync(
        string runId,
        List<ChatMessage> messages,
        List<AgentDebugMessage> debugMessages,
        IReadOnlyList<IAgentSkill> selectedSkills,
        ChatCompletion completion,
        AgentWorkflowTrace workflowTrace,
        AgentRunState runState,
        AgentToolResultLimiter toolResultLimiter,
        AgentToolTimeoutRunner toolTimeoutRunner)
    {
        // 先把“模型要求调用工具”这条 assistant 消息加入上下文。
        // SDK 会保留 tool_call_id，下一条 ToolChatMessage 才能和它对上。
        messages.Add(new AssistantChatMessage(completion));
        debugMessages.Add(new AgentDebugMessage
        {
            Role = "assistant",
            ToolCalls = completion.ToolCalls
                .Select(toolCall => new AgentDebugToolCall(
                    toolCall.Id,
                    toolCall.FunctionName,
                    toolCall.FunctionArguments.ToString()))
                .ToArray()
        });

        foreach (ChatToolCall toolCall in completion.ToolCalls)
        {
            AddWorkflowStep(
                workflowTrace,
                runState,
                AgentWorkflowStepKind.ToolRequested,
                "Act",
                $"Model requested tool '{toolCall.FunctionName}'.",
                toolName: toolCall.FunctionName);

            IAgentSkill skill = _skillRegistry.GetRequiredSkill(toolCall.FunctionName);
            AgentToolConfirmationRequest? pendingApproval = await PauseForToolApprovalIfNeededAsync(
                runId,
                workflowTrace,
                runState,
                debugMessages,
                selectedSkills,
                skill,
                toolCall);
            if (pendingApproval is not null)
            {
                return pendingApproval;
            }

            AgentToolExecutionContext executionContext = new(runId, toolCall.Id);
            string rawResult;
            bool toolFailed = false;
            try
            {
                rawResult = await toolTimeoutRunner.RunAsync(
                    toolCall.FunctionName,
                    cancellationToken => _skillRegistry.ExecuteAsync(
                        toolCall.FunctionName,
                        toolCall.FunctionArguments.ToString(),
                        executionContext,
                        cancellationToken));
            }
            catch (Exception exception) when (AgentToolErrorFormatter.IsRecoverable(exception))
            {
                toolFailed = true;
                rawResult = AgentToolErrorFormatter.FormatRecoverableError(
                    toolCall.FunctionName,
                    exception);

                AddWorkflowStep(
                    workflowTrace,
                    runState,
                    AgentWorkflowStepKind.ToolFailed,
                    "Observe tool error",
                    $"Tool '{toolCall.FunctionName}' failed: {exception.Message}",
                    toolName: toolCall.FunctionName,
                    error: exception.Message);
            }

            string result = toolResultLimiter.Limit(rawResult);

            if (!toolFailed)
            {
                AddWorkflowStep(
                    workflowTrace,
                    runState,
                    AgentWorkflowStepKind.ToolExecuted,
                    "Observe",
                    $"Tool '{toolCall.FunctionName}' returned: {result}",
                    toolName: toolCall.FunctionName);
            }

            EmitToolResultPreview(toolCall, result);

            // 这条消息相当于告诉模型：你刚才要的工具结果在这里。
            messages.Add(new ToolChatMessage(toolCall.Id, result));
            debugMessages.Add(new AgentDebugMessage
            {
                Role = "tool",
                ToolCallId = toolCall.Id,
                Content = result
            });
        }

        return null;
    }

    private async Task<AgentToolConfirmationRequest?> PauseForToolApprovalIfNeededAsync(
        string runId,
        AgentWorkflowTrace workflowTrace,
        AgentRunState runState,
        IReadOnlyList<AgentDebugMessage> debugMessages,
        IReadOnlyList<IAgentSkill> selectedSkills,
        IAgentSkill skill,
        ChatToolCall toolCall)
    {
        if (!AgentToolPermissionPolicy.RequiresConfirmation(skill))
        {
            return null;
        }

        AddWorkflowStep(
            workflowTrace,
            runState,
            AgentWorkflowStepKind.ToolApprovalRequested,
            "Request tool approval",
            $"Tool '{skill.Name}' requires confirmation. Risk: {skill.RiskLevel}.",
            toolName: skill.Name);

        AgentToolConfirmationRequest request = new(
            ToolCallId: toolCall.Id,
            ToolName: skill.Name,
            Description: skill.Description,
            ArgumentsJson: toolCall.FunctionArguments.ToString(),
            RiskLevel: skill.RiskLevel);

        await CreatePendingApprovalCheckpointAsync(runId, request, runState, debugMessages, selectedSkills);
        return request;
    }

    private async Task CreatePendingApprovalCheckpointAsync(
        string runId,
        AgentToolConfirmationRequest request,
        AgentRunState runState,
        IReadOnlyList<AgentDebugMessage> debugMessages,
        IReadOnlyList<IAgentSkill> selectedSkills)
    {
        Func<AgentRunCheckpoint, Task> checkpointHandler = CheckpointCreatedAsync
            ?? throw new InvalidOperationException(
                "A checkpoint persistence handler is required when a tool needs approval.");

        IReadOnlyList<AgentCheckpointMessage> checkpointMessages =
            AgentCheckpointMessageBuilder.FromDebugMessages(debugMessages);
        string[] selectedToolNames = selectedSkills
            .Select(skill => skill.Name)
            .ToArray();

        AgentRunCheckpoint checkpoint = AgentRunCheckpoint.CreatePendingApproval(
            runId,
            DateTimeOffset.Now,
            request,
            runState.ToSnapshot(),
            checkpointMessages,
            selectedToolNames);

        await checkpointHandler(checkpoint);
    }

    private List<ChatMessage> BuildMessages(IReadOnlyList<ChatTurn> contextTurns)
    {
        List<ChatMessage> messages =
        [
            // system message 是角色设定：它告诉模型“你是谁、该怎么回答”。
            new SystemChatMessage(BuildSystemInstructions())
        ];

        foreach (ChatTurn turn in contextTurns)
        {
            messages.Add(turn.Role switch
            {
                ChatRole.User => new UserChatMessage(turn.Content),
                ChatRole.Assistant => new AssistantChatMessage(turn.Content),
                _ => throw new InvalidOperationException($"Unsupported chat role: {turn.Role}")
            });
        }

        return messages;
    }

    private static List<ChatMessage> BuildResumeMessages(
        IReadOnlyList<AgentCheckpointMessage> checkpointMessages)
    {
        List<ChatMessage> messages = [];

        foreach (AgentCheckpointMessage checkpointMessage in checkpointMessages)
        {
            messages.Add(checkpointMessage.Role switch
            {
                "system" => new SystemChatMessage(checkpointMessage.Content ?? string.Empty),
                "user" => new UserChatMessage(checkpointMessage.Content ?? string.Empty),
                "assistant" when checkpointMessage.ToolCalls.Count > 0 =>
                    BuildAssistantToolCallMessage(checkpointMessage),
                "assistant" => new AssistantChatMessage(checkpointMessage.Content ?? string.Empty),
                "tool" => new ToolChatMessage(
                    RequireCheckpointToolCallId(checkpointMessage),
                    checkpointMessage.Content ?? string.Empty),
                _ => throw new InvalidOperationException(
                    $"Unsupported checkpoint message role: {checkpointMessage.Role}")
            });
        }

        return messages;
    }

    private static AssistantChatMessage BuildAssistantToolCallMessage(
        AgentCheckpointMessage checkpointMessage)
    {
        ChatToolCall[] toolCalls = checkpointMessage.ToolCalls
            .Select(toolCall => ChatToolCall.CreateFunctionToolCall(
                toolCall.Id,
                toolCall.Name,
                BinaryData.FromString(toolCall.ArgumentsJson)))
            .ToArray();

        return new AssistantChatMessage(toolCalls);
    }

    private static string RequireCheckpointToolCallId(AgentCheckpointMessage checkpointMessage)
    {
        if (string.IsNullOrWhiteSpace(checkpointMessage.ToolCallId))
        {
            throw new InvalidOperationException("Tool checkpoint message requires tool_call_id.");
        }

        return checkpointMessage.ToolCallId;
    }

    private static void AddCurrentUserInput(
        List<ChatMessage> messages,
        List<AgentDebugMessage> debugMessages,
        string userInput)
    {
        messages.Add(new UserChatMessage(userInput));
        debugMessages.Add(new AgentDebugMessage
        {
            Role = "user",
            Content = userInput
        });
    }

    private List<AgentDebugMessage> BuildDebugMessages(IReadOnlyList<ChatTurn> contextTurns)
    {
        List<AgentDebugMessage> messages =
        [
            new()
            {
                Role = "system",
                Content = BuildSystemInstructions()
            }
        ];

        foreach (ChatTurn turn in contextTurns)
        {
            messages.Add(turn.Role switch
            {
                ChatRole.User => new AgentDebugMessage
                {
                    Role = "user",
                    Content = turn.Content
                },
                ChatRole.Assistant => new AgentDebugMessage
                {
                    Role = "assistant",
                    Content = turn.Content
                },
                _ => throw new InvalidOperationException($"Unsupported chat role: {turn.Role}")
            });
        }

        return messages;
    }

    private static List<AgentDebugMessage> BuildResumeDebugMessages(
        IReadOnlyList<AgentCheckpointMessage> checkpointMessages)
    {
        List<AgentDebugMessage> messages = [];

        foreach (AgentCheckpointMessage checkpointMessage in checkpointMessages)
        {
            messages.Add(new AgentDebugMessage
            {
                Role = checkpointMessage.Role,
                Content = checkpointMessage.Content,
                ToolCallId = checkpointMessage.ToolCallId,
                ToolCalls = checkpointMessage.ToolCalls
                    .Select(toolCall => new AgentDebugToolCall(
                        toolCall.Id,
                        toolCall.Name,
                        toolCall.ArgumentsJson))
                    .ToArray()
            });
        }

        return messages;
    }

    private async Task TrySaveResumedMemoryAsync(
        AgentRunCheckpoint checkpoint,
        string assistantReply)
    {
        string? userInput = checkpoint.Messages
            .LastOrDefault(message => message.Role == "user")
            ?.Content;

        if (string.IsNullOrWhiteSpace(userInput))
        {
            return;
        }

        AgentMemoryWritePolicy memoryWritePolicy = new(_profile.MaxMemoryContentChars);
        if (!memoryWritePolicy.ShouldWrite(userInput) || !memoryWritePolicy.ShouldWrite(assistantReply))
        {
            return;
        }

        _memory.AddUserMessage(userInput);
        _memory.AddAssistantMessage(assistantReply);
        await ChatMemoryStore.SaveAsync(_memoryPath, _memory);
    }

    private ChatCompletionOptions BuildChatOptions(IEnumerable<IAgentSkill> selectedSkills)
    {
        ChatCompletionOptions options = new()
        {
            AllowParallelToolCalls = false
        };

        foreach (IAgentSkill skill in selectedSkills)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                functionName: skill.Name,
                functionDescription: skill.Description,
                functionParameters: BinaryData.FromString(skill.ParametersJson)));
        }

        return options;
    }

    private void AddWorkflowStep(
        AgentWorkflowTrace workflowTrace,
        AgentRunState runState,
        AgentWorkflowStepKind kind,
        string title,
        string detail,
        string? toolName = null,
        string? error = null)
    {
        AgentWorkflowStep step = workflowTrace.Add(kind, title, detail);
        ApplyStateTransition(runState, kind, toolName, error);
        WorkflowStepCreated?.Invoke(step);
        StateChanged?.Invoke(runState.ToSnapshot());
    }

    private static void ApplyStateTransition(
        AgentRunState runState,
        AgentWorkflowStepKind kind,
        string? toolName,
        string? error)
    {
        switch (kind)
        {
            case AgentWorkflowStepKind.ReceiveInput:
                runState.MarkReceivedInput();
                break;

            case AgentWorkflowStepKind.BuildContext:
                runState.MarkBuiltContext();
                break;

            case AgentWorkflowStepKind.RouteTools:
                runState.MarkRoutedTools();
                break;

            case AgentWorkflowStepKind.AskModel:
                runState.MarkAskedModel();
                break;

            case AgentWorkflowStepKind.ToolRequested:
                runState.MarkToolRequested(RequireToolName(toolName, kind));
                break;

            case AgentWorkflowStepKind.ToolApprovalRequested:
                runState.MarkWaitingForApproval(RequireToolName(toolName, kind));
                break;

            case AgentWorkflowStepKind.ToolRejected:
                runState.MarkToolRejected(RequireToolName(toolName, kind));
                break;

            case AgentWorkflowStepKind.ToolFailed:
                runState.MarkToolFailed(
                    RequireToolName(toolName, kind),
                    string.IsNullOrWhiteSpace(error) ? "Unknown tool error." : error);
                break;

            case AgentWorkflowStepKind.ToolExecuted:
                runState.MarkToolExecuted(RequireToolName(toolName, kind));
                break;

            case AgentWorkflowStepKind.Finish:
                runState.MarkFinished();
                break;

            default:
                throw new InvalidOperationException($"Unsupported workflow step kind: {kind}");
        }
    }

    private static string RequireToolName(string? toolName, AgentWorkflowStepKind kind)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new InvalidOperationException($"Workflow step '{kind}' requires a tool name for state tracking.");
        }

        return toolName;
    }

    private void EmitChatRequestPreview(
        List<AgentDebugMessage> debugMessages,
        int requestNumber,
        IReadOnlyList<IAgentSkill> selectedSkills)
    {
        if (!_profile.ShowDebugRequests)
        {
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine($"--- Debug request body preview #{requestNumber} ---");
        builder.AppendLine(AgentDebugPreviewBuilder.BuildChatCompletionsRequestPreview(
            model: _profile.Model,
            stream: _profile.Stream,
            messages: debugMessages,
            skills: selectedSkills,
            includeTools: selectedSkills.Count > 0));
        builder.AppendLine("--- End debug request body preview ---");

        DebugMessageCreated?.Invoke(builder.ToString());
    }

    private void EmitToolRouterRequestPreview(string userInput, string catalogJson)
    {
        if (!_profile.ShowDebugRequests)
        {
            return;
        }

        AgentDebugMessage[] debugMessages =
        [
            new()
            {
                Role = "system",
                Content = BuildToolRouterSystemInstructions()
            },
            new()
            {
                Role = "user",
                Content = BuildToolRouterUserMessage(userInput, catalogJson)
            }
        ];

        StringBuilder builder = new();
        builder.AppendLine();
        builder.AppendLine("--- Debug tool router request body preview ---");
        builder.AppendLine(AgentDebugPreviewBuilder.BuildChatCompletionsRequestPreview(
            model: _profile.Model,
            stream: false,
            messages: debugMessages,
            skills: [],
            includeTools: false));
        builder.AppendLine("--- End debug tool router request body preview ---");

        DebugMessageCreated?.Invoke(builder.ToString());
    }

    private void EmitToolRouterResponsePreview(ChatCompletion completion, string routerJson)
    {
        if (!_profile.ShowDebugRequests)
        {
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("--- Debug tool router response preview ---");
        builder.AppendLine($"finish_reason: {completion.FinishReason}");
        builder.AppendLine($"content: {AgentDebugPreviewBuilder.RedactSensitiveValues(routerJson)}");
        builder.AppendLine("--- End debug tool router response preview ---");

        DebugMessageCreated?.Invoke(builder.ToString());
    }

    private void EmitChatResponsePreview(ChatCompletion completion)
    {
        if (!_profile.ShowDebugRequests)
        {
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("--- Debug model response preview ---");
        builder.AppendLine($"finish_reason: {completion.FinishReason}");

        if (completion.ToolCalls.Count > 0)
        {
            foreach (ChatToolCall toolCall in completion.ToolCalls)
            {
                builder.AppendLine($"tool_call_id: {toolCall.Id}");
                builder.AppendLine($"tool_name: {toolCall.FunctionName}");
                builder.AppendLine($"tool_arguments: {AgentDebugPreviewBuilder.RedactSensitiveValues(toolCall.FunctionArguments.ToString())}");
            }
        }
        else if (completion.Content.Count > 0)
        {
            builder.AppendLine($"content: {AgentDebugPreviewBuilder.RedactSensitiveValues(string.Concat(completion.Content.Select(part => part.Text)))}");
        }
        else
        {
            builder.AppendLine("content: <empty>");
        }

        builder.AppendLine("--- End debug model response preview ---");
        DebugMessageCreated?.Invoke(builder.ToString());
    }

    private void EmitToolResultPreview(ChatToolCall toolCall, string result)
    {
        if (!_profile.ShowDebugRequests)
        {
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("--- Debug local tool result ---");
        builder.AppendLine($"tool_call_id: {toolCall.Id}");
        builder.AppendLine($"tool_name: {toolCall.FunctionName}");
        builder.AppendLine($"result: {AgentDebugPreviewBuilder.RedactSensitiveValues(result)}");
        builder.AppendLine("--- End debug local tool result ---");
        DebugMessageCreated?.Invoke(builder.ToString());
    }

    private string BuildSystemInstructions()
    {
        return $"""
        You are {_profile.Name}.

        Description:
        {_profile.Description}

        Instructions:
        {_profile.Instructions}
        """;
    }

    private sealed record AgentLoopResult(
        string? AssistantReply,
        AgentToolConfirmationRequest? PendingApproval)
    {
        public static AgentLoopResult Completed(string assistantReply)
        {
            return new AgentLoopResult(assistantReply, PendingApproval: null);
        }

        public static AgentLoopResult Paused(AgentToolConfirmationRequest pendingApproval)
        {
            ArgumentNullException.ThrowIfNull(pendingApproval);
            return new AgentLoopResult(AssistantReply: null, pendingApproval);
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.App/Program.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.App/Program.cs</code></summary>

`````csharp
using AgentLearning.App;
using AgentLearning.Core;
using AgentLearning.Core.Diagnostics;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

// AppContext.BaseDirectory 指向编译后的运行目录。
// csproj 已经配置了复制 agent.json 和 agent.local.json，所以运行时能在这里找到配置文件。
string profilePath = Path.Combine(AppContext.BaseDirectory, "agent.json");
string localProfilePath = Path.Combine(AppContext.BaseDirectory, "agent.local.json");

// 读取 Agent 的角色设定、API 接线配置，以及本地私有密钥配置。
AgentProfile profile = await AgentProfileLoader.LoadFromFileAsync(profilePath, localProfilePath);

// 优先使用 agent.local.json 里的 api_key。
// 如果你临时不想写本地文件，也仍然可以用环境变量兜底。
string? apiKey = profile.ApiKey ?? Environment.GetEnvironmentVariable(profile.EnvKey);
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine($"No API key was found in agent.local.json or {profile.EnvKey}.");
    Console.WriteLine("Set one of them, then run this app again:");
    Console.WriteLine("  agent.local.json: { \"api_key\": \"sk-...\" }");
    Console.WriteLine($"  export {profile.EnvKey}=\"sk-...\"");
    return 1;
}

// ChatClient 对应你给的 curl 路径：POST /v1/chat/completions。
// Endpoint 使用 https://router.hddev.top/v1，SDK 会在它后面拼接 /chat/completions。
ChatClient client = new(
    model: profile.Model,
    credential: new ApiKeyCredential(apiKey),
    options: new OpenAIClientOptions
    {
        Endpoint = new Uri(profile.BaseUrl)
    });

// memory_file 可以写相对路径；这里把它解析成真正使用的文件路径。
string memoryPath = AgentPathResolver.ResolveRuntimePath(AppContext.BaseDirectory, profile.MemoryFile);
string notesPath = AgentPathResolver.ResolveRuntimePath(AppContext.BaseDirectory, "memory/agent-notes.md");
string checkpointPath = AgentPathResolver.ResolveRuntimePath(AppContext.BaseDirectory, "memory/pending-approval-checkpoint.json");
string knowledgeIndexPath = AgentPathResolver.ResolveRuntimePath(
    AppContext.BaseDirectory,
    "memory/knowledge-vector-index.json");
string knowledgeDirectoryPath = Path.Combine(AppContext.BaseDirectory, "knowledge");

// 现在记忆会从本地 JSON 文件恢复；文件不存在时得到一个空记忆。
ChatMemory memory = await ChatMemoryStore.LoadAsync(memoryPath);

// The MCP client starts the standalone server as a child process over stdio.
string mcpServerAssemblyPath = Path.Combine(
    AppContext.BaseDirectory,
    "mcp-server",
    "AgentLearning.McpServer.dll");
if (!File.Exists(mcpServerAssemblyPath))
{
    throw new FileNotFoundException("The bundled MCP server assembly was not found.", mcpServerAssemblyPath);
}

Dictionary<string, McpToolPolicy> mcpToolPolicies = new(StringComparer.Ordinal)
{
    ["get_learning_progress"] = new(
        RiskLevel: AgentSkillRiskLevel.Low,
        RequiresConfirmation: false),
    ["search_knowledge"] = new(
        RiskLevel: AgentSkillRiskLevel.Low,
        RequiresConfirmation: false),
    ["write_note"] = new(
        RiskLevel: AgentSkillRiskLevel.Medium,
        RequiresConfirmation: true)
};
await using McpSkillClient mcpSkillClient = await McpSkillClient.ConnectStdioAsync(
    serverName: "agent-learning-tools",
    command: "dotnet",
    arguments:
    [
        mcpServerAssemblyPath,
        "--notes-file",
        notesPath,
        "--knowledge-directory",
        knowledgeDirectoryPath,
        "--knowledge-index-file",
        knowledgeIndexPath,
        "--embedding-base-url",
        profile.EmbeddingBaseUrl,
        "--embedding-model",
        profile.EmbeddingModel
    ],
    toolPolicies: mcpToolPolicies);

// 注册当前 Agent 可以使用的技能。
// 这一步只是把 C# 函数准备好，真正什么时候调用由模型决定。
AgentSkillRegistry skillRegistry = new([
    new TimeSkill(),
    new CalculatorSkill(),
    .. mcpSkillClient.Skills
]);

AgentRunner agentRunner = new(profile, client, memory, memoryPath, skillRegistry);
agentRunner.CheckpointCreatedAsync = checkpoint => SaveCheckpointAsync(checkpointPath, checkpoint, profile);
agentRunner.CheckpointConsumedAsync = _ => AgentCheckpointStore.DeleteAsync(checkpointPath);
agentRunner.WorkflowStepCreated += step =>
{
    if (profile.ShowWorkflowTrace)
    {
        Console.WriteLine(AgentWorkflowStepFormatter.Format(step));
    }
};
agentRunner.DebugMessageCreated += Console.Write;

Console.WriteLine($"Loaded agent: {profile.Name}");
Console.WriteLine($"Wire API: {profile.WireApi}");
Console.WriteLine($"Base URL: {profile.BaseUrl}");
Console.WriteLine($"Stream: {profile.Stream}");
Console.WriteLine($"Native tool calling: {profile.NativeToolCalling}");
Console.WriteLine($"Tool router enabled: {profile.ToolRouterEnabled}");
Console.WriteLine($"Max tools per request: {profile.MaxToolsPerRequest}");
Console.WriteLine($"Show debug requests: {profile.ShowDebugRequests}");
Console.WriteLine($"Show workflow trace: {profile.ShowWorkflowTrace}");
Console.WriteLine($"Memory file: {memoryPath}");
Console.WriteLine($"Notes file: {notesPath}");
Console.WriteLine($"Checkpoint file: {checkpointPath}");
Console.WriteLine($"Knowledge directory: {knowledgeDirectoryPath}");
Console.WriteLine($"Knowledge index file: {knowledgeIndexPath}");
Console.WriteLine($"Embedding base URL: {profile.EmbeddingBaseUrl}");
Console.WriteLine($"Embedding model: {profile.EmbeddingModel}");
Console.WriteLine($"MCP server: {mcpServerAssemblyPath}");
Console.WriteLine($"MCP skills: {string.Join(", ", mcpSkillClient.Skills.Select(skill => skill.Name))}");
Console.WriteLine($"Loaded memory turns: {memory.Turns.Count}");
Console.WriteLine($"Max memory turns sent: {profile.MaxMemoryTurns}");
Console.WriteLine($"Max memory content chars: {profile.MaxMemoryContentChars}");
Console.WriteLine($"Max tool iterations: {profile.MaxToolIterations}");
Console.WriteLine($"Max tool result chars: {profile.MaxToolResultChars}");
Console.WriteLine($"Tool timeout seconds: {profile.ToolTimeoutSeconds}");
Console.WriteLine($"Skills: {string.Join(", ", skillRegistry.Skills.Select(skill => skill.Name))}");
Console.WriteLine("Type a message and press Enter. Type 'exit' to quit.");
Console.WriteLine("Local skill commands: /time, /calc <expression>");
Console.WriteLine();

if (profile.Stream && profile.NativeToolCalling)
{
    Console.WriteLine("Native tool calling is only implemented for non-streaming mode in this lesson.");
    return 1;
}

while (true)
{
    Console.Write("You> ");
    string? input = Console.ReadLine();

    // 输入 exit 就退出；这就是当前最简单的交互方式。
    if (input is null || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    // 空输入不调用模型，避免浪费一次请求。
    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (await TryResumeCheckpointCommandAsync(input, checkpointPath, profile, agentRunner))
    {
        Console.WriteLine();
        continue;
    }

    if (await TryRunLocalSkillCommandAsync(input, profile, memory, memoryPath, skillRegistry))
    {
        Console.WriteLine();
        continue;
    }

    AgentRunCheckpoint? unfinishedCheckpoint = await AgentCheckpointStore.LoadAsync(checkpointPath);
    if (unfinishedCheckpoint is not null)
    {
        Console.WriteLine($"Run {unfinishedCheckpoint.RunId} is still waiting to be resumed.");
        Console.WriteLine("Use /resume yes to approve it or /resume no to reject it before starting another agent run.");
        Console.WriteLine();
        continue;
    }

    try
    {
        AgentRunResult result = await agentRunner.RunAsync(input);
        PrintAgentRunResult(result, profile, printCompletedReply: !profile.Stream);

        if (profile.ShowWorkflowTrace)
        {
            Console.WriteLine(
                $"[State] {result.FinalState.Status} | model requests: {result.FinalState.ModelRequestCount} | tool calls: {result.FinalState.ToolCallCount}");
        }

        Console.WriteLine();
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Agent call failed: {exception.Message}");
        return 1;
    }
}

return 0;

static async Task<bool> TryRunLocalSkillCommandAsync(
    string input,
    AgentProfile profile,
    ChatMemory memory,
    string memoryPath,
    AgentSkillRegistry skillRegistry)
{
    if (input.Equals("/time", StringComparison.OrdinalIgnoreCase))
    {
        string result = await skillRegistry.ExecuteAsync(
            "get_current_time",
            "{}",
            AgentToolExecutionContext.CreateLocalCommand());
        await TrySaveLocalSkillMemoryAsync(profile, memory, memoryPath, input, result);
        Console.WriteLine($"{profile.Name}> {result}");
        return true;
    }

    const string calculatorPrefix = "/calc ";
    if (input.StartsWith(calculatorPrefix, StringComparison.OrdinalIgnoreCase))
    {
        string expression = input[calculatorPrefix.Length..].Trim();
        string argumentsJson = JsonSerializer.Serialize(new { expression });
        string result = await skillRegistry.ExecuteAsync(
            "calculate",
            argumentsJson,
            AgentToolExecutionContext.CreateLocalCommand());

        await TrySaveLocalSkillMemoryAsync(profile, memory, memoryPath, input, result);
        Console.WriteLine($"{profile.Name}> {result}");
        return true;
    }

    return false;
}

static async Task<bool> TryResumeCheckpointCommandAsync(
    string input,
    string checkpointPath,
    AgentProfile profile,
    AgentRunner agentRunner)
{
    const string resumeCommand = "/resume";
    if (!input.StartsWith(resumeCommand, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    string decision = input[resumeCommand.Length..].Trim();
    if (!decision.Equals("yes", StringComparison.OrdinalIgnoreCase) &&
        !decision.Equals("no", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Usage: /resume yes OR /resume no");
        return true;
    }

    AgentRunCheckpoint? checkpoint = await AgentCheckpointStore.LoadAsync(checkpointPath);
    if (checkpoint is null)
    {
        Console.WriteLine("No pending checkpoint was found.");
        return true;
    }

    bool approved = decision.Equals("yes", StringComparison.OrdinalIgnoreCase);
    AgentRunResult result;
    try
    {
        result = await agentRunner.ResumeAsync(checkpoint, approved);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Checkpoint resume failed: {exception.Message}");
        return true;
    }

    Console.WriteLine(approved
        ? "Checkpoint resumed with approval."
        : "Checkpoint resumed with rejection.");
    PrintAgentRunResult(result, profile, printCompletedReply: true);

    if (profile.ShowWorkflowTrace)
    {
        Console.WriteLine(
            $"[State] {result.FinalState.Status} | model requests: {result.FinalState.ModelRequestCount} | tool calls: {result.FinalState.ToolCallCount}");
    }

    return true;
}

static async Task TrySaveLocalSkillMemoryAsync(
    AgentProfile profile,
    ChatMemory memory,
    string memoryPath,
    string userInput,
    string assistantReply)
{
    AgentMemoryWritePolicy memoryWritePolicy = new(profile.MaxMemoryContentChars);
    if (!memoryWritePolicy.ShouldWrite(userInput) || !memoryWritePolicy.ShouldWrite(assistantReply))
    {
        return;
    }

    memory.AddUserMessage(userInput);
    memory.AddAssistantMessage(assistantReply);
    await ChatMemoryStore.SaveAsync(memoryPath, memory);
}

static async Task SaveCheckpointAsync(
    string checkpointPath,
    AgentRunCheckpoint checkpoint,
    AgentProfile profile)
{
    await AgentCheckpointStore.SaveAsync(checkpointPath, checkpoint);

    if (profile.ShowWorkflowTrace)
    {
        Console.WriteLine($"[Checkpoint] Saved {checkpoint.Kind} for run {checkpoint.RunId} to {checkpointPath}");
    }
}

static void PrintAgentRunResult(
    AgentRunResult result,
    AgentProfile profile,
    bool printCompletedReply)
{
    if (result.Outcome == AgentRunOutcome.WaitingForApproval)
    {
        AgentToolConfirmationRequest request = result.PendingApproval
            ?? throw new InvalidOperationException("A paused run must contain pending approval data.");

        Console.WriteLine("Agent paused for tool approval.");
        Console.WriteLine($"Tool: {request.ToolName}");
        Console.WriteLine($"Risk: {request.RiskLevel}");
        Console.WriteLine($"Description: {request.Description}");
        Console.WriteLine($"Arguments: {AgentDebugPreviewBuilder.RedactSensitiveValues(request.ArgumentsJson)}");
        Console.WriteLine("Use /resume yes to approve or /resume no to reject.");
        return;
    }

    if (printCompletedReply)
    {
        Console.WriteLine($"{profile.Name}> {result.AssistantReply}");
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.App/agent.json -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.App/agent.json</code></summary>

`````json
{
  "name": "Grimoire Router",
  "model": "gpt-5.4",
  "base_url": "https://router.hddev.top/v1",
  "embedding_base_url": "http://127.0.0.1:1234/v1",
  "embedding_model": "text-embedding-granite-embedding-278m-multilingual",
  "env_key": "GRIMOIRE_API_KEY",
  "wire_api": "chat_completions",
  "stream": false,
  "native_tool_calling": true,
  "tool_router_enabled": true,
  "max_tools_per_request": 2,
  "show_debug_requests": true,
  "show_workflow_trace": true,
  "memory_file": "memory/chat-memory.json",
  "max_memory_turns": 6,
  "max_memory_content_chars": 2000,
  "max_tool_iterations": 3,
  "max_tool_result_chars": 1200,
  "tool_timeout_seconds": 5,
  "description": "A patient C# agent teacher for a beginner learning how agents work.",
  "instructions": "Teach one concept at a time. Prefer small examples. Explain why each piece exists before adding the next piece."
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/AgentCheckpointResumer.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/AgentCheckpointResumer.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core;

/// <summary>
/// Resolves the tool step represented by a checkpoint.
/// </summary>
public static class AgentCheckpointResumer
{
    /// <summary>
    /// Execute a pending tool, reject it, or reuse an already resolved tool observation.
    /// </summary>
    public static async Task<AgentCheckpointResumeResult> ResumeAsync(
        AgentRunCheckpoint checkpoint,
        bool approved,
        AgentSkillRegistry skillRegistry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(skillRegistry);

        if (checkpoint.Kind == AgentCheckpointKind.ToolResolved)
        {
            ResolvedToolCall resolvedTool = checkpoint.ResolvedTool
                ?? throw new InvalidOperationException("Checkpoint does not contain resolved tool data.");

            return new AgentCheckpointResumeResult(
                checkpoint.RunId,
                resolvedTool.ToolCallId,
                resolvedTool.ToolName,
                resolvedTool.Approved,
                resolvedTool.ToolExecuted,
                resolvedTool.Observation);
        }

        if (checkpoint.Kind != AgentCheckpointKind.PendingToolApproval)
        {
            throw new InvalidOperationException($"Unsupported checkpoint kind: {checkpoint.Kind}");
        }

        PendingToolApproval pendingApproval = checkpoint.PendingApproval
            ?? throw new InvalidOperationException("Checkpoint does not contain pending approval data.");

        if (!approved)
        {
            string rejectedObservation = AgentToolApprovalObservation.BuildRejected(pendingApproval.ToolName);
            return new AgentCheckpointResumeResult(
                checkpoint.RunId,
                pendingApproval.ToolCallId,
                pendingApproval.ToolName,
                Approved: false,
                ToolExecuted: false,
                Observation: rejectedObservation);
        }

        AgentToolExecutionContext executionContext = new(
            checkpoint.RunId,
            pendingApproval.ToolCallId);
        string toolResult = await skillRegistry.ExecuteAsync(
            pendingApproval.ToolName,
            pendingApproval.ArgumentsJson,
            executionContext,
            cancellationToken);

        return new AgentCheckpointResumeResult(
            checkpoint.RunId,
            pendingApproval.ToolCallId,
            pendingApproval.ToolName,
            Approved: true,
            ToolExecuted: true,
            Observation: toolResult);
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/AgentProfile.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/AgentProfile.cs</code></summary>

`````csharp
using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// Agent 的角色、模型接线配置，以及运行保护规则。
/// </summary>
public sealed record AgentProfile(
    /// <summary>Agent 的显示名称。</summary>
    string Name,

    /// <summary>要调用的模型名称，由当前 Router/服务端决定可用值。</summary>
    string Model,

    /// <summary>OpenAI 兼容服务的基础地址，例如 https://router.hddev.top/v1。</summary>
    [property: JsonPropertyName("base_url")]
    string BaseUrl,

    /// <summary>The LM Studio OpenAI-compatible base URL used for local embeddings.</summary>
    [property: JsonPropertyName("embedding_base_url")]
    string EmbeddingBaseUrl,

    /// <summary>The local LM Studio embedding model identifier.</summary>
    [property: JsonPropertyName("embedding_model")]
    string EmbeddingModel,

    /// <summary>保存 API Key 的环境变量名，代码不会把密钥写死在文件里。</summary>
    [property: JsonPropertyName("env_key")]
    string EnvKey,

    /// <summary>底层调用协议。你的 curl 示例使用 chat_completions。</summary>
    [property: JsonPropertyName("wire_api")]
    string WireApi,

    /// <summary>是否使用流式返回。false 对应 curl 里的 "stream": false。</summary>
    [property: JsonPropertyName("stream")]
    bool Stream,

    /// <summary>是否把技能声明为 Chat Completions 原生 tools，让模型自动选择要不要调用技能。</summary>
    [property: JsonPropertyName("native_tool_calling")]
    bool NativeToolCalling,

    /// <summary>是否先调用 AI Tool Router，让模型从轻量工具目录里选择本轮需要暴露的工具。</summary>
    [property: JsonPropertyName("tool_router_enabled")]
    bool ToolRouterEnabled,

    /// <summary>AI Tool Router 一次最多可以选择多少个工具，用来避免单轮暴露过多工具。</summary>
    [property: JsonPropertyName("max_tools_per_request")]
    int MaxToolsPerRequest,

    /// <summary>是否在控制台打印请求体预览，方便学习和排查 Tool Calling。</summary>
    [property: JsonPropertyName("show_debug_requests")]
    bool ShowDebugRequests,

    /// <summary>是否打印 Agent 工作流步骤，方便观察 ReAct 循环。</summary>
    [property: JsonPropertyName("show_workflow_trace")]
    bool ShowWorkflowTrace,

    /// <summary>聊天记忆保存到哪里。相对路径会基于程序运行目录计算。</summary>
    [property: JsonPropertyName("memory_file")]
    string MemoryFile,

    /// <summary>每次请求最多发送多少条历史消息给模型，用来控制上下文大小。</summary>
    [property: JsonPropertyName("max_memory_turns")]
    int MaxMemoryTurns,

    /// <summary>单条记忆最多允许保存多少字符，用来避免大段文本污染长期记忆。</summary>
    [property: JsonPropertyName("max_memory_content_chars")]
    int MaxMemoryContentChars,

    /// <summary>一次用户请求里最多允许几轮工具调用，用来防止工具循环失控。</summary>
    [property: JsonPropertyName("max_tool_iterations")]
    int MaxToolIterations,

    /// <summary>工具结果最多保留多少字符发回模型，用来控制上下文大小。</summary>
    [property: JsonPropertyName("max_tool_result_chars")]
    int MaxToolResultChars,

    /// <summary>每个工具最多允许执行多少秒，用来防止工具卡住后 Agent 一直等待。</summary>
    [property: JsonPropertyName("tool_timeout_seconds")]
    int ToolTimeoutSeconds,

    /// <summary>本地 API Key。建议只放在 agent.local.json，不要放在主配置里。</summary>
    [property: JsonPropertyName("api_key")]
    string? ApiKey,

    /// <summary>Agent 的一句话说明，方便人理解它的用途。</summary>
    string Description,

    /// <summary>Agent 的系统指令，决定它回答问题时的角色、语气和边界。</summary>
    string Instructions);
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/AgentProfileLoader.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/AgentProfileLoader.cs</code></summary>

`````csharp
using System.Text.Json;

namespace AgentLearning.Core;

/// <summary>
/// 负责从 JSON 文件读取 Agent 配置，并做最基本的校验。
/// </summary>
public static class AgentProfileLoader
{
    // 允许 JSON 里的 name/model 等字段大小写不敏感。
    // base_url/env_key/wire_api 这种下划线字段通过 AgentProfile 上的 JsonPropertyName 映射。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 从指定路径读取 Agent 配置。
    /// </summary>
    public static async Task<AgentProfile> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return await LoadFromFileAsync(filePath, localFilePath: null, cancellationToken);
    }

    /// <summary>
    /// 从主配置和本地私有配置读取 Agent 配置。
    /// 主配置放公开信息，本地配置只放 api_key。
    /// </summary>
    public static async Task<AgentProfile> LoadFromFileAsync(
        string filePath,
        string? localFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Agent profile file was not found.", filePath);
        }

        await using FileStream stream = File.OpenRead(filePath);
        AgentProfile? profile = await JsonSerializer.DeserializeAsync<AgentProfile>(
            stream,
            JsonOptions,
            cancellationToken);

        if (profile is null)
        {
            throw new InvalidOperationException("Agent profile file is empty or invalid.");
        }

        AgentProfile mergedProfile = await MergeLocalProfileAsync(profile, localFilePath, cancellationToken);

        Validate(mergedProfile);
        return mergedProfile;
    }

    private static async Task<AgentProfile> MergeLocalProfileAsync(
        AgentProfile profile,
        string? localFilePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
        {
            return NormalizeApiKey(profile);
        }

        await using FileStream stream = File.OpenRead(localFilePath);
        AgentLocalProfile? localProfile = await JsonSerializer.DeserializeAsync<AgentLocalProfile>(
            stream,
            JsonOptions,
            cancellationToken);

        if (localProfile is null || string.IsNullOrWhiteSpace(localProfile.ApiKey))
        {
            return NormalizeApiKey(profile);
        }

        return profile with { ApiKey = localProfile.ApiKey.Trim() };
    }

    private static AgentProfile NormalizeApiKey(AgentProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.ApiKey)
            ? profile with { ApiKey = null }
            : profile with { ApiKey = profile.ApiKey.Trim() };
    }

    // 配置缺失时直接抛出清晰错误，不用默认值偷偷掩盖问题。
    private static void Validate(AgentProfile profile)
    {
        RequireValue(profile.Name, "name");
        RequireValue(profile.Model, "model");
        RequireValue(profile.BaseUrl, "base_url");
        RequireValue(profile.EmbeddingBaseUrl, "embedding_base_url");
        RequireValue(profile.EmbeddingModel, "embedding_model");
        RequireValue(profile.EnvKey, "env_key");
        RequireValue(profile.WireApi, "wire_api");
        RequireValue(profile.MemoryFile, "memory_file");
        RequireValue(profile.Description, "description");
        RequireValue(profile.Instructions, "instructions");

        if (!Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Agent profile field 'base_url' must be an absolute URL.");
        }

        if (!Uri.TryCreate(profile.EmbeddingBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "Agent profile field 'embedding_base_url' must be an absolute URL.");
        }

        if (profile.MaxMemoryTurns <= 0)
        {
            throw new InvalidOperationException("Agent profile field 'max_memory_turns' must be greater than zero.");
        }

        if (profile.MaxMemoryContentChars <= 0)
        {
            throw new InvalidOperationException("Agent profile field 'max_memory_content_chars' must be greater than zero.");
        }

        if (profile.MaxToolsPerRequest <= 0)
        {
            throw new InvalidOperationException("Agent profile field 'max_tools_per_request' must be greater than zero.");
        }

        if (profile.MaxToolIterations <= 0)
        {
            throw new InvalidOperationException("Agent profile field 'max_tool_iterations' must be greater than zero.");
        }

        if (profile.MaxToolResultChars <= 0)
        {
            throw new InvalidOperationException("Agent profile field 'max_tool_result_chars' must be greater than zero.");
        }

        if (profile.ToolTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Agent profile field 'tool_timeout_seconds' must be greater than zero.");
        }

        if (!profile.WireApi.Equals("chat_completions", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only wire_api = 'chat_completions' is supported in this lesson.");
        }
    }

    private static void RequireValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Agent profile field '{fieldName}' is required.");
        }
    }

    private sealed record AgentLocalProfile(
        [property: System.Text.Json.Serialization.JsonPropertyName("api_key")]
        string? ApiKey);
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/Skills/AgentSkillRegistry.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/Skills/AgentSkillRegistry.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Skills;

/// <summary>
/// 技能注册表。
/// 它负责按名字找到技能并执行，避免 Program.cs 里写一大堆 switch。
/// </summary>
public sealed class AgentSkillRegistry
{
    private readonly Dictionary<string, IAgentSkill> _skills;

    public AgentSkillRegistry(IEnumerable<IAgentSkill> skills)
    {
        _skills = skills.ToDictionary(skill => skill.Name, StringComparer.Ordinal);
    }

    /// <summary>所有可用技能。</summary>
    public IReadOnlyCollection<IAgentSkill> Skills => _skills.Values;

    /// <summary>按函数名取得技能元数据，找不到时抛出清晰错误。</summary>
    public IAgentSkill GetRequiredSkill(string skillName)
    {
        if (!_skills.TryGetValue(skillName, out IAgentSkill? skill))
        {
            throw new AgentUnknownSkillException(skillName);
        }

        return skill;
    }

    /// <summary>按函数名执行技能。</summary>
    public async Task<string> ExecuteAsync(
        string skillName,
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        IAgentSkill skill = GetRequiredSkill(skillName);
        return await skill.ExecuteAsync(argumentsJson, executionContext, cancellationToken);
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/Skills/CalculatorSkill.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/Skills/CalculatorSkill.cs</code></summary>

`````csharp
using System.Globalization;
using System.Text.Json;

namespace AgentLearning.Core.Skills;

/// <summary>
/// 简单计算器技能。
/// 第一版只支持数字、括号、加减乘除，避免执行任意代码带来安全风险。
/// </summary>
public sealed class CalculatorSkill : IAgentSkill
{
    public string Name => "calculate";

    public string Description => "Calculate a basic math expression with numbers, parentheses, +, -, *, and /.";

    public string ParametersJson => """
        {
          "type": "object",
          "properties": {
            "expression": {
              "type": "string",
              "description": "A basic math expression, for example: (2 + 3) * 4"
            }
          },
          "required": ["expression"],
          "additionalProperties": false
        }
        """;

    public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Low;

    public bool RequiresConfirmation => false;

    public Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        string expression = ReadExpression(argumentsJson);
        double result = new ExpressionParser(expression).Parse();
        return Task.FromResult(FormatNumber(result));
    }

    private static string ReadExpression(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);

        if (!document.RootElement.TryGetProperty("expression", out JsonElement expressionElement))
        {
            throw new InvalidOperationException("Calculator skill requires a non-empty 'expression' argument.");
        }

        string? expression = expressionElement.GetString();
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new InvalidOperationException("Calculator skill requires a non-empty 'expression' argument.");
        }

        return expression;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###############", CultureInfo.InvariantCulture);
    }

    private sealed class ExpressionParser
    {
        private readonly string _expression;
        private int _position;

        public ExpressionParser(string expression)
        {
            _expression = expression;
        }

        public double Parse()
        {
            double value = ParseExpression();
            SkipWhitespace();

            if (_position != _expression.Length)
            {
                throw new InvalidOperationException($"Unexpected character '{_expression[_position]}' in expression.");
            }

            return value;
        }

        private double ParseExpression()
        {
            double value = ParseTerm();

            while (true)
            {
                SkipWhitespace();

                if (Match('+'))
                {
                    value += ParseTerm();
                }
                else if (Match('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            double value = ParseFactor();

            while (true)
            {
                SkipWhitespace();

                if (Match('*'))
                {
                    value *= ParseFactor();
                }
                else if (Match('/'))
                {
                    double divisor = ParseFactor();
                    if (divisor == 0)
                    {
                        throw new InvalidOperationException("Division by zero is not allowed.");
                    }

                    value /= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseFactor()
        {
            SkipWhitespace();

            if (Match('('))
            {
                double value = ParseExpression();
                SkipWhitespace();

                if (!Match(')'))
                {
                    throw new InvalidOperationException("Missing closing parenthesis in expression.");
                }

                return value;
            }

            if (Match('-'))
            {
                return -ParseFactor();
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            int start = _position;

            while (_position < _expression.Length &&
                   (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
            {
                _position++;
            }

            if (start == _position)
            {
                throw new InvalidOperationException("Expected a number in expression.");
            }

            string token = _expression[start.._position];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException($"Invalid number '{token}' in expression.");
            }

            return value;
        }

        private bool Match(char expected)
        {
            if (_position >= _expression.Length || _expression[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
            {
                _position++;
            }
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/Skills/IAgentSkill.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/Skills/IAgentSkill.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Skills;

/// <summary>
/// A skill that the agent can expose to the model and execute locally.
/// </summary>
public interface IAgentSkill
{
    /// <summary>The function name exposed to the model.</summary>
    string Name { get; }

    /// <summary>The description shown to the model.</summary>
    string Description { get; }

    /// <summary>The JSON Schema for model-generated arguments.</summary>
    string ParametersJson { get; }

    /// <summary>The risk level used by the harness permission policy.</summary>
    AgentSkillRiskLevel RiskLevel { get; }

    /// <summary>Whether this skill always requires human approval.</summary>
    bool RequiresConfirmation { get; }

    /// <summary>
    /// Executes the skill with model arguments and trusted harness metadata.
    /// </summary>
    Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default);
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/Skills/TimeSkill.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/Skills/TimeSkill.cs</code></summary>

`````csharp
namespace AgentLearning.Core.Skills;

/// <summary>
/// 返回当前时间的技能。
/// 这是最容易理解的 Tool Calling 示例：模型问时间，C# 代码提供真实时间。
/// </summary>
public sealed class TimeSkill : IAgentSkill
{
    private readonly Func<DateTimeOffset> _clock;

    public TimeSkill()
        : this(() => DateTimeOffset.Now)
    {
    }

    public TimeSkill(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    public string Name => "get_current_time";

    public string Description => "Get the current local date and time.";

    public string ParametersJson => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Low;

    public bool RequiresConfirmation => false;

    public Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_clock().ToString("O"));
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE src/AgentLearning.Core/Skills/WriteNoteSkill.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>src/AgentLearning.Core/Skills/WriteNoteSkill.cs</code></summary>

`````csharp
using System.Text.Json;

namespace AgentLearning.Core.Skills;

/// <summary>
/// Appends notes to a local Markdown file without duplicating the same logical tool call.
/// </summary>
public sealed class WriteNoteSkill : IAgentSkill
{
    private const int LockAttemptCount = 100;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly string _notesFilePath;
    private readonly Func<DateTimeOffset> _clock;

    public WriteNoteSkill(string notesFilePath)
        : this(notesFilePath, () => DateTimeOffset.Now)
    {
    }

    public WriteNoteSkill(string notesFilePath, Func<DateTimeOffset> clock)
    {
        if (string.IsNullOrWhiteSpace(notesFilePath))
        {
            throw new ArgumentException("Notes file path cannot be empty.", nameof(notesFilePath));
        }

        ArgumentNullException.ThrowIfNull(clock);

        _notesFilePath = Path.GetFullPath(notesFilePath.Trim());
        _clock = clock;
    }

    public string Name => "write_note";

    public string Description => "Append a note to a local markdown notes file.";

    public string ParametersJson => """
        {
          "type": "object",
          "properties": {
            "note": {
              "type": "string",
              "description": "The note content to append to the local notes file."
            }
          },
          "required": ["note"],
          "additionalProperties": false
        }
        """;

    public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Medium;

    public bool RequiresConfirmation => true;

    public async Task<string> ExecuteAsync(
        string argumentsJson,
        AgentToolExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionContext);

        string note = ReadNote(argumentsJson);
        string directory = Path.GetDirectoryName(_notesFilePath)
            ?? throw new InvalidOperationException("The notes file must have a parent directory.");
        Directory.CreateDirectory(directory);

        string operationMarker = BuildOperationMarker(executionContext.IdempotencyKey);
        string lockFilePath = $"{_notesFilePath}.lock";
        await using FileStream lockStream = await AcquireFileLockAsync(lockFilePath, cancellationToken);
        string existingContent = File.Exists(_notesFilePath)
            ? await File.ReadAllTextAsync(_notesFilePath, cancellationToken)
            : string.Empty;

        if (existingContent.Contains(operationMarker, StringComparison.Ordinal))
        {
            return BuildSuccessResult();
        }

        string entry = BuildEntry(operationMarker, note);
        string updatedContent = AppendEntry(existingContent, entry);
        await WriteAtomicallyAsync(updatedContent, cancellationToken);

        return BuildSuccessResult();
    }

    private string BuildEntry(string operationMarker, string note)
    {
        return $"""
            {operationMarker}
            - {_clock():O}
              {note}

            """;
    }

    private string BuildSuccessResult()
    {
        return $"Note saved to {_notesFilePath}.";
    }

    private async Task WriteAtomicallyAsync(string content, CancellationToken cancellationToken)
    {
        string tempFilePath = $"{_notesFilePath}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempFilePath, content, cancellationToken);
            File.Move(tempFilePath, _notesFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private static async Task<FileStream> AcquireFileLockAsync(
        string lockFilePath,
        CancellationToken cancellationToken)
    {
        IOException? lastException = null;
        for (int attempt = 0; attempt < LockAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException exception)
            {
                lastException = exception;
            }

            await Task.Delay(LockRetryDelay, cancellationToken);
        }

        throw new IOException($"Timed out while locking notes file '{lockFilePath}'.", lastException);
    }

    private static string BuildOperationMarker(string idempotencyKey)
    {
        return $"<!-- grimoire-agent-operation:{idempotencyKey} -->";
    }

    private static string AppendEntry(string existingContent, string entry)
    {
        if (existingContent.Length == 0 || existingContent.EndsWith('\n'))
        {
            return existingContent + entry;
        }

        return existingContent + Environment.NewLine + entry;
    }

    private static string ReadNote(string argumentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(argumentsJson);

        if (!document.RootElement.TryGetProperty("note", out JsonElement noteElement))
        {
            throw new InvalidOperationException("Write note skill requires a non-empty 'note' argument.");
        }

        string? note = noteElement.GetString();
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new InvalidOperationException("Write note skill requires a non-empty 'note' argument.");
        }

        return note.Trim();
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/AgentCheckpointResumerTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/AgentCheckpointResumerTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;

namespace AgentLearning.Core.Tests;

public sealed class AgentCheckpointResumerTests
{
    [Fact]
    public async Task ResumeAsync_executes_pending_tool_when_approved()
    {
        string tempDirectory = CreateTempDirectory();
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        AgentRunCheckpoint checkpoint = CreateCheckpoint("""{"note":"Resume 会执行保存的工具参数。"}""");
        AgentSkillRegistry skillRegistry = new([
            new WriteNoteSkill(notesFile)
        ]);

        try
        {
            AgentCheckpointResumeResult result = await AgentCheckpointResumer.ResumeAsync(
                checkpoint,
                approved: true,
                skillRegistry);

            string savedText = await File.ReadAllTextAsync(notesFile);
            Assert.True(result.Approved);
            Assert.True(result.ToolExecuted);
            Assert.Equal("run_resume", result.RunId);
            Assert.Equal("call_resume", result.ToolCallId);
            Assert.Equal("write_note", result.ToolName);
            Assert.Contains("Note saved to", result.Observation);
            Assert.Contains("Resume 会执行保存的工具参数。", savedText);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeAsync_returns_rejection_observation_without_executing_tool_when_rejected()
    {
        string tempDirectory = CreateTempDirectory();
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        AgentRunCheckpoint checkpoint = CreateCheckpoint("""{"note":"这条不应该写入。"}""");
        AgentSkillRegistry skillRegistry = new([
            new WriteNoteSkill(notesFile)
        ]);

        try
        {
            AgentCheckpointResumeResult result = await AgentCheckpointResumer.ResumeAsync(
                checkpoint,
                approved: false,
                skillRegistry);

            Assert.False(result.Approved);
            Assert.False(result.ToolExecuted);
            Assert.Equal("run_resume", result.RunId);
            Assert.Equal("call_resume", result.ToolCallId);
            Assert.Equal("write_note", result.ToolName);
            Assert.Contains("user rejected", result.Observation);
            Assert.False(File.Exists(notesFile));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeAsync_same_pending_checkpoint_twice_writes_note_once()
    {
        string tempDirectory = CreateTempDirectory();
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        const string note = "A retried checkpoint must not duplicate this note.";
        AgentRunCheckpoint checkpoint = CreateCheckpoint($"{{\"note\":\"{note}\"}}");
        AgentSkillRegistry firstProcessRegistry = new([new WriteNoteSkill(notesFile)]);
        AgentSkillRegistry restartedProcessRegistry = new([new WriteNoteSkill(notesFile)]);

        try
        {
            AgentCheckpointResumeResult first = await AgentCheckpointResumer.ResumeAsync(
                checkpoint,
                approved: true,
                firstProcessRegistry);
            AgentCheckpointResumeResult second = await AgentCheckpointResumer.ResumeAsync(
                checkpoint,
                approved: true,
                restartedProcessRegistry);

            string savedText = await File.ReadAllTextAsync(notesFile);
            Assert.Equal(first.Observation, second.Observation);
            Assert.Equal(1, savedText.Split(note, StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeAsync_rejects_checkpoint_without_pending_approval()
    {
        AgentRunSnapshot snapshot = new(
            Status: AgentRunStatus.WaitingForApproval,
            ModelRequestCount: 1,
            ToolCallCount: 1,
            LastToolName: "write_note",
            WaitingForApproval: true,
            LastError: null);

        AgentRunCheckpoint checkpoint = new(
            RunId: "run_resume",
            Kind: AgentCheckpointKind.PendingToolApproval,
            CreatedAt: DateTimeOffset.Now,
            State: snapshot,
            Messages: CreateCheckpointMessages("""{"note":"hello"}"""),
            SelectedToolNames: ["write_note"],
            PendingApproval: null,
            ResolvedTool: null);

        AgentSkillRegistry skillRegistry = new([
            new WriteNoteSkill(Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.md"))
        ]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AgentCheckpointResumer.ResumeAsync(checkpoint, approved: true, skillRegistry));

        Assert.Contains("pending approval", exception.Message);
    }

    private static AgentRunCheckpoint CreateCheckpoint(string argumentsJson)
    {
        AgentRunSnapshot snapshot = new(
            Status: AgentRunStatus.WaitingForApproval,
            ModelRequestCount: 1,
            ToolCallCount: 1,
            LastToolName: "write_note",
            WaitingForApproval: true,
            LastError: null);

        AgentToolConfirmationRequest request = new(
            ToolCallId: "call_resume",
            ToolName: "write_note",
            Description: "Append a note.",
            ArgumentsJson: argumentsJson,
            RiskLevel: AgentSkillRiskLevel.Medium);

        return AgentRunCheckpoint.CreatePendingApproval(
            runId: "run_resume",
            createdAt: new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.FromHours(8)),
            request: request,
            state: snapshot,
            messages: CreateCheckpointMessages(argumentsJson),
            selectedToolNames: ["write_note"]);
    }

    private static AgentCheckpointMessage[] CreateCheckpointMessages(string argumentsJson)
    {
        return
        [
            AgentCheckpointMessage.Text("system", "You are a teacher."),
            AgentCheckpointMessage.Text("user", "please save a note"),
            AgentCheckpointMessage.AssistantToolCalls(
            [
                new AgentCheckpointToolCall(
                    Id: "call_resume",
                    Name: "write_note",
                    ArgumentsJson: argumentsJson)
            ])
        ];
    }

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-resume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/AgentProfileLoaderTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/AgentProfileLoaderTests.cs</code></summary>

`````csharp
using AgentLearning.Core;

namespace AgentLearning.Core.Tests;

public sealed class AgentProfileLoaderTests
{
    [Fact]
    public async Task LoadFromFileAsync_reads_agent_role_settings()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            tempFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 2,
              "show_debug_requests": true,
              "show_workflow_trace": true,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 2000,
              "max_tool_iterations": 3,
              "max_tool_result_chars": 1200,
              "tool_timeout_seconds": 5,
              "api_key": null,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        try
        {
            AgentProfile profile = await AgentProfileLoader.LoadFromFileAsync(tempFile);

            Assert.Equal("Grimoire Tutor", profile.Name);
            Assert.Equal("gpt-5.4", profile.Model);
            Assert.Equal("https://router.hddev.top/v1", profile.BaseUrl);
            Assert.Equal("http://127.0.0.1:1234/v1", profile.EmbeddingBaseUrl);
            Assert.Equal("test-embedding-model", profile.EmbeddingModel);
            Assert.Equal("GRIMOIRE_API_KEY", profile.EnvKey);
            Assert.Equal("chat_completions", profile.WireApi);
            Assert.False(profile.Stream);
            Assert.False(profile.NativeToolCalling);
            Assert.True(profile.ToolRouterEnabled);
            Assert.Equal(2, profile.MaxToolsPerRequest);
            Assert.True(profile.ShowDebugRequests);
            Assert.True(profile.ShowWorkflowTrace);
            Assert.Equal("memory/chat-memory.json", profile.MemoryFile);
            Assert.Equal(6, profile.MaxMemoryTurns);
            Assert.Equal(2000, profile.MaxMemoryContentChars);
            Assert.Equal(3, profile.MaxToolIterations);
            Assert.Equal(1200, profile.MaxToolResultChars);
            Assert.Equal(5, profile.ToolTimeoutSeconds);
            Assert.Null(profile.ApiKey);
            Assert.Equal("A patient C# agent teacher.", profile.Description);
            Assert.Equal("Teach one concept at a time.", profile.Instructions);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_reads_api_key_from_local_profile_file()
    {
        string profileFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        string localFile = Path.Combine(Path.GetTempPath(), $"agent-local-{Guid.NewGuid():N}.json");

        await File.WriteAllTextAsync(
            profileFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 2,
              "show_debug_requests": false,
              "show_workflow_trace": false,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 2000,
              "max_tool_iterations": 3,
              "max_tool_result_chars": 1200,
              "tool_timeout_seconds": 5,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        await File.WriteAllTextAsync(
            localFile,
            """
            {
              "api_key": "test-local-key"
            }
            """);

        try
        {
            AgentProfile profile = await AgentProfileLoader.LoadFromFileAsync(profileFile, localFile);

            Assert.Equal("test-local-key", profile.ApiKey);
        }
        finally
        {
            File.Delete(profileFile);
            File.Delete(localFile);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_rejects_non_positive_max_tool_iterations()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            tempFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 2,
              "show_debug_requests": false,
              "show_workflow_trace": false,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 2000,
              "max_tool_iterations": 0,
              "max_tool_result_chars": 1200,
              "tool_timeout_seconds": 5,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentProfileLoader.LoadFromFileAsync(tempFile));

            Assert.Contains("max_tool_iterations", error.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_rejects_non_positive_max_tool_result_chars()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            tempFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 2,
              "show_debug_requests": false,
              "show_workflow_trace": false,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 2000,
              "max_tool_iterations": 3,
              "max_tool_result_chars": 0,
              "tool_timeout_seconds": 5,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentProfileLoader.LoadFromFileAsync(tempFile));

            Assert.Contains("max_tool_result_chars", error.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_rejects_non_positive_tool_timeout_seconds()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            tempFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 2,
              "show_debug_requests": false,
              "show_workflow_trace": false,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 2000,
              "max_tool_iterations": 3,
              "max_tool_result_chars": 1200,
              "tool_timeout_seconds": 0,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentProfileLoader.LoadFromFileAsync(tempFile));

            Assert.Contains("tool_timeout_seconds", error.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_rejects_non_positive_max_memory_content_chars()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            tempFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 2,
              "show_debug_requests": false,
              "show_workflow_trace": false,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 0,
              "max_tool_iterations": 3,
              "max_tool_result_chars": 1200,
              "tool_timeout_seconds": 5,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentProfileLoader.LoadFromFileAsync(tempFile));

            Assert.Contains("max_memory_content_chars", error.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadFromFileAsync_rejects_non_positive_max_tools_per_request()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            tempFile,
            """
            {
              "name": "Grimoire Tutor",
              "model": "gpt-5.4",
              "base_url": "https://router.hddev.top/v1",
              "embedding_base_url": "http://127.0.0.1:1234/v1",
              "embedding_model": "test-embedding-model",
              "env_key": "GRIMOIRE_API_KEY",
              "wire_api": "chat_completions",
              "stream": false,
              "native_tool_calling": false,
              "tool_router_enabled": true,
              "max_tools_per_request": 0,
              "show_debug_requests": false,
              "show_workflow_trace": false,
              "memory_file": "memory/chat-memory.json",
              "max_memory_turns": 6,
              "max_memory_content_chars": 2000,
              "max_tool_iterations": 3,
              "max_tool_result_chars": 1200,
              "tool_timeout_seconds": 5,
              "description": "A patient C# agent teacher.",
              "instructions": "Teach one concept at a time."
            }
            """);

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => AgentProfileLoader.LoadFromFileAsync(tempFile));

            Assert.Contains("max_tools_per_request", error.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/AgentRunnerPauseTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/AgentRunnerPauseTests.cs</code></summary>

`````csharp
using AgentLearning.App;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunnerPauseTests
{
    [Fact]
    public async Task RunAsync_pauses_before_executing_tool_that_requires_approval()
    {
        string tempDirectory = CreateTempDirectory();
        string memoryPath = Path.Combine(tempDirectory, "memory.json");
        string notesPath = Path.Combine(tempDirectory, "notes.md");
        ChatMemory memory = new();
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(
                "call_write",
                "write_note",
                "{\"note\":\"Do not write before approval\"}"));

        AgentRunCheckpoint? savedCheckpoint = null;
        bool checkpointConsumed = false;
        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            memory,
            memoryPath,
            new AgentSkillRegistry([new WriteNoteSkill(notesPath)]));
        runner.CheckpointCreatedAsync = checkpoint =>
        {
            savedCheckpoint = checkpoint;
            return Task.CompletedTask;
        };
        runner.CheckpointConsumedAsync = _ =>
        {
            checkpointConsumed = true;
            return Task.CompletedTask;
        };

        AgentRunResult result = await runner.RunAsync("Save this note.");

        Assert.Equal(AgentRunOutcome.WaitingForApproval, result.Outcome);
        Assert.Null(result.AssistantReply);
        Assert.NotNull(result.PendingApproval);
        Assert.Equal("call_write", result.PendingApproval.ToolCallId);
        Assert.Equal("write_note", result.PendingApproval.ToolName);
        Assert.Equal(AgentRunStatus.WaitingForApproval, result.FinalState.Status);
        Assert.True(result.FinalState.WaitingForApproval);

        Assert.NotNull(savedCheckpoint);
        Assert.Equal(AgentCheckpointKind.PendingToolApproval, savedCheckpoint.Kind);
        Assert.Equal("call_write", savedCheckpoint.PendingApproval?.ToolCallId);
        Assert.False(checkpointConsumed);

        Assert.False(File.Exists(notesPath));
        Assert.False(File.Exists(memoryPath));
        Assert.Empty(memory.Turns);
        Assert.Single(chatClient.Requests);
        Assert.False(chatClient.Options.Single()?.AllowParallelToolCalls ?? true);
    }

    [Fact]
    public async Task RunAsync_does_not_execute_protected_tool_without_checkpoint_storage()
    {
        string tempDirectory = CreateTempDirectory();
        string notesPath = Path.Combine(tempDirectory, "notes.md");
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(
                "call_write",
                "write_note",
                "{\"note\":\"Never execute without a checkpoint\"}"));

        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            Path.Combine(tempDirectory, "memory.json"),
            new AgentSkillRegistry([new WriteNoteSkill(notesPath)]));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("Save this note."));

        Assert.Contains("checkpoint persistence handler", exception.Message);
        Assert.False(File.Exists(notesPath));
    }

    private static AgentProfile CreateProfile()
    {
        return new AgentProfile(
            Name: "Test Agent",
            Model: "test-model",
            BaseUrl: "https://example.test/v1",
            EmbeddingBaseUrl: "http://127.0.0.1:1234/v1",
            EmbeddingModel: "test-embedding-model",
            EnvKey: "TEST_API_KEY",
            WireApi: "chat_completions",
            Stream: false,
            NativeToolCalling: true,
            ToolRouterEnabled: false,
            MaxToolsPerRequest: 4,
            ShowDebugRequests: false,
            ShowWorkflowTrace: false,
            MemoryFile: "memory.json",
            MaxMemoryTurns: 6,
            MaxMemoryContentChars: 2000,
            MaxToolIterations: 3,
            MaxToolResultChars: 1200,
            ToolTimeoutSeconds: 5,
            ApiKey: "test-key",
            Description: "Test agent.",
            Instructions: "Answer briefly.");
    }

    private static ChatCompletion CreateToolCallCompletion(
        string id,
        string toolName,
        string argumentsJson)
    {
        ChatToolCall toolCall = ChatToolCall.CreateFunctionToolCall(
            id,
            toolName,
            BinaryData.FromString(argumentsJson));

        return OpenAIChatModelFactory.ChatCompletion(
            $"chatcmpl_{Guid.NewGuid():N}",
            ChatFinishReason.ToolCalls,
            [],
            null,
            [toolCall],
            ChatMessageRole.Assistant,
            null,
            [],
            [],
            DateTimeOffset.Now,
            "test-model",
            null,
            null);
    }

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-runner-pause-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private sealed class FakeAgentChatClient : IAgentChatClient
    {
        private readonly Queue<ChatCompletion> _responses;

        public FakeAgentChatClient(params ChatCompletion[] responses)
        {
            _responses = new Queue<ChatCompletion>(responses);
        }

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public List<ChatCompletionOptions?> Options { get; } = [];

        public Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null)
        {
            Requests.Add(messages.ToArray());
            Options.Add(options);
            return Task.FromResult(_responses.Dequeue());
        }

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            throw new NotSupportedException("Streaming is not used by this test.");
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/AgentRunnerResumeTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/AgentRunnerResumeTests.cs</code></summary>

`````csharp
using AgentLearning.App;
using AgentLearning.Core.Skills;
using AgentLearning.Core.Workflow;
using OpenAI.Chat;

namespace AgentLearning.Core.Tests;

public sealed class AgentRunnerResumeTests
{
    [Fact]
    public async Task ResumeAsync_continues_full_tool_loop_when_resumed_model_requests_another_tool()
    {
        string tempDirectory = CreateTempDirectory();
        string memoryPath = Path.Combine(tempDirectory, "memory.json");
        string notesPath = Path.Combine(tempDirectory, "notes.md");
        AgentRunCheckpoint checkpoint = CreatePendingWriteNoteCheckpoint();

        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(
                "call_time",
                "get_current_time",
                "{}"),
            CreateTextCompletion("笔记已保存，我又查了一次当前时间。"));
        AgentRunCheckpoint? savedResolvedCheckpoint = null;
        bool checkpointConsumed = false;
        chatClient.BeforeCompleteChatAsync = () =>
        {
            Assert.NotNull(savedResolvedCheckpoint);
            Assert.False(checkpointConsumed);
        };

        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            memoryPath,
            new AgentSkillRegistry([
                new WriteNoteSkill(notesPath),
                new TimeSkill()
            ]));
        runner.CheckpointCreatedAsync = createdCheckpoint =>
        {
            savedResolvedCheckpoint = createdCheckpoint;
            return Task.CompletedTask;
        };
        runner.CheckpointConsumedAsync = consumedCheckpoint =>
        {
            Assert.Same(savedResolvedCheckpoint, consumedCheckpoint);
            checkpointConsumed = true;
            return Task.CompletedTask;
        };

        AgentRunResult result = await runner.ResumeAsync(checkpoint, approved: true);

        Assert.Equal(AgentRunOutcome.Completed, result.Outcome);
        Assert.NotNull(savedResolvedCheckpoint);
        Assert.Equal(AgentCheckpointKind.ToolResolved, savedResolvedCheckpoint.Kind);
        Assert.NotNull(savedResolvedCheckpoint.ResolvedTool);
        Assert.Equal("call_write", savedResolvedCheckpoint.ResolvedTool.ToolCallId);
        Assert.Equal("write_note", savedResolvedCheckpoint.ResolvedTool.ToolName);
        Assert.True(savedResolvedCheckpoint.ResolvedTool.ToolExecuted);
        Assert.Contains("Note saved to", savedResolvedCheckpoint.ResolvedTool.Observation);
        Assert.True(checkpointConsumed);
        Assert.Equal("笔记已保存，我又查了一次当前时间。", result.AssistantReply);
        Assert.Equal(AgentRunStatus.Finished, result.FinalState.Status);
        Assert.Equal(2, result.FinalState.ModelRequestCount);
        Assert.Equal(1, result.FinalState.ToolCallCount);
        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Contains("ResumeAsync 第二轮测试", await File.ReadAllTextAsync(notesPath));
    }

    [Fact]
    public async Task ResumeAsync_uses_tool_resolved_checkpoint_without_executing_tool_again()
    {
        string tempDirectory = CreateTempDirectory();
        string memoryPath = Path.Combine(tempDirectory, "memory.json");
        string notesPath = Path.Combine(tempDirectory, "notes.md");
        AgentRunCheckpoint checkpoint = CreateToolResolvedCheckpoint();

        FakeAgentChatClient chatClient = new(CreateTextCompletion("继续完成回答。"));
        bool checkpointConsumed = false;

        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            memoryPath,
            new AgentSkillRegistry([
                new WriteNoteSkill(notesPath),
                new TimeSkill()
            ]));
        runner.CheckpointCreatedAsync = _ => throw new InvalidOperationException("ToolResolved resume should not create another checkpoint.");
        runner.CheckpointConsumedAsync = consumedCheckpoint =>
        {
            Assert.Same(checkpoint, consumedCheckpoint);
            checkpointConsumed = true;
            return Task.CompletedTask;
        };

        AgentRunResult result = await runner.ResumeAsync(checkpoint, approved: true);

        Assert.Equal(AgentRunOutcome.Completed, result.Outcome);
        Assert.True(checkpointConsumed);
        Assert.Equal("继续完成回答。", result.AssistantReply);
        Assert.False(File.Exists(notesPath));
        Assert.Single(chatClient.Requests);
    }

    [Fact]
    public async Task ResumeAsync_pauses_again_when_model_requests_another_protected_tool()
    {
        string tempDirectory = CreateTempDirectory();
        string memoryPath = Path.Combine(tempDirectory, "memory.json");
        string notesPath = Path.Combine(tempDirectory, "notes.md");
        AgentRunCheckpoint checkpoint = CreatePendingWriteNoteCheckpoint();
        FakeAgentChatClient chatClient = new(
            CreateToolCallCompletion(
                "call_write_again",
                "write_note",
                "{\"note\":\"Second note must wait\"}"));

        List<AgentRunCheckpoint> savedCheckpoints = [];
        bool checkpointConsumed = false;
        AgentRunner runner = new(
            CreateProfile(),
            chatClient,
            new ChatMemory(),
            memoryPath,
            new AgentSkillRegistry([
                new WriteNoteSkill(notesPath),
                new TimeSkill()
            ]));
        runner.CheckpointCreatedAsync = createdCheckpoint =>
        {
            savedCheckpoints.Add(createdCheckpoint);
            return Task.CompletedTask;
        };
        runner.CheckpointConsumedAsync = _ =>
        {
            checkpointConsumed = true;
            return Task.CompletedTask;
        };

        AgentRunResult result = await runner.ResumeAsync(checkpoint, approved: true);

        Assert.Equal(AgentRunOutcome.WaitingForApproval, result.Outcome);
        Assert.Null(result.AssistantReply);
        Assert.Equal("call_write_again", result.PendingApproval?.ToolCallId);
        Assert.Equal(AgentRunStatus.WaitingForApproval, result.FinalState.Status);
        Assert.False(checkpointConsumed);

        Assert.Collection(
            savedCheckpoints,
            resolved => Assert.Equal(AgentCheckpointKind.ToolResolved, resolved.Kind),
            pending =>
            {
                Assert.Equal(AgentCheckpointKind.PendingToolApproval, pending.Kind);
                Assert.Equal("call_write_again", pending.PendingApproval?.ToolCallId);
            });

        string notes = await File.ReadAllTextAsync(notesPath);
        Assert.Contains("ResumeAsync 第二轮测试", notes);
        Assert.DoesNotContain("Second note must wait", notes);
        Assert.False(File.Exists(memoryPath));
        Assert.Single(chatClient.Requests);
    }

    private static AgentProfile CreateProfile()
    {
        return new AgentProfile(
            Name: "Test Agent",
            Model: "test-model",
            BaseUrl: "https://example.test/v1",
            EmbeddingBaseUrl: "http://127.0.0.1:1234/v1",
            EmbeddingModel: "test-embedding-model",
            EnvKey: "TEST_API_KEY",
            WireApi: "chat_completions",
            Stream: false,
            NativeToolCalling: true,
            ToolRouterEnabled: false,
            MaxToolsPerRequest: 4,
            ShowDebugRequests: false,
            ShowWorkflowTrace: false,
            MemoryFile: "memory.json",
            MaxMemoryTurns: 6,
            MaxMemoryContentChars: 2000,
            MaxToolIterations: 3,
            MaxToolResultChars: 1200,
            ToolTimeoutSeconds: 5,
            ApiKey: "test-key",
            Description: "Test agent.",
            Instructions: "Answer briefly.");
    }

    private static AgentRunCheckpoint CreatePendingWriteNoteCheckpoint()
    {
        AgentRunSnapshot snapshot = new(
            Status: AgentRunStatus.WaitingForApproval,
            ModelRequestCount: 1,
            ToolCallCount: 1,
            LastToolName: "write_note",
            WaitingForApproval: true,
            LastError: null);

        AgentToolConfirmationRequest request = new(
            ToolCallId: "call_write",
            ToolName: "write_note",
            Description: "Append a note.",
            ArgumentsJson: """{"note":"ResumeAsync 第二轮测试"}""",
            RiskLevel: AgentSkillRiskLevel.Medium);

        AgentCheckpointMessage[] messages =
        [
            AgentCheckpointMessage.Text("system", "You are a test agent."),
            AgentCheckpointMessage.Text("user", "保存笔记后再查时间"),
            AgentCheckpointMessage.AssistantToolCalls(
            [
                new AgentCheckpointToolCall(
                    Id: "call_write",
                    Name: "write_note",
                    ArgumentsJson: """{"note":"ResumeAsync 第二轮测试"}""")
            ])
        ];

        return AgentRunCheckpoint.CreatePendingApproval(
            runId: "run_resume_test",
            createdAt: new DateTimeOffset(2026, 7, 9, 10, 30, 0, TimeSpan.FromHours(8)),
            request: request,
            state: snapshot,
            messages: messages,
            selectedToolNames: ["write_note", "get_current_time"]);
    }

    private static AgentRunCheckpoint CreateToolResolvedCheckpoint()
    {
        AgentRunSnapshot snapshot = new(
            Status: AgentRunStatus.ToolExecuted,
            ModelRequestCount: 1,
            ToolCallCount: 1,
            LastToolName: "write_note",
            WaitingForApproval: false,
            LastError: null);

        return AgentRunCheckpoint.CreateToolResolved(
            runId: "run_resolved_test",
            createdAt: new DateTimeOffset(2026, 7, 10, 10, 30, 0, TimeSpan.FromHours(8)),
            state: snapshot,
            messages:
            [
                AgentCheckpointMessage.Text("system", "You are a test agent."),
                AgentCheckpointMessage.Text("user", "保存笔记"),
                AgentCheckpointMessage.AssistantToolCalls(
                [
                    new AgentCheckpointToolCall(
                        Id: "call_write",
                        Name: "write_note",
                        ArgumentsJson: """{"note":"Do not write again"}""")
                ])
            ],
            selectedToolNames: ["write_note"],
            resolvedTool: new ResolvedToolCall(
                ToolCallId: "call_write",
                ToolName: "write_note",
                Approved: true,
                ToolExecuted: true,
                Observation: "Note saved earlier."));
    }

    private static ChatCompletion CreateTextCompletion(string text)
    {
        return OpenAIChatModelFactory.ChatCompletion(
            $"chatcmpl_{Guid.NewGuid():N}",
            ChatFinishReason.Stop,
            [ChatMessageContentPart.CreateTextPart(text)],
            null,
            [],
            ChatMessageRole.Assistant,
            null,
            [],
            [],
            DateTimeOffset.Now,
            "test-model",
            null,
            null);
    }

    private static ChatCompletion CreateToolCallCompletion(
        string id,
        string toolName,
        string argumentsJson)
    {
        ChatToolCall toolCall = ChatToolCall.CreateFunctionToolCall(
            id,
            toolName,
            BinaryData.FromString(argumentsJson));

        return OpenAIChatModelFactory.ChatCompletion(
            $"chatcmpl_{Guid.NewGuid():N}",
            ChatFinishReason.ToolCalls,
            [],
            null,
            [toolCall],
            ChatMessageRole.Assistant,
            null,
            [],
            [],
            DateTimeOffset.Now,
            "test-model",
            null,
            null);
    }

    private static string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-runner-resume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private sealed class FakeAgentChatClient : IAgentChatClient
    {
        private readonly Queue<ChatCompletion> _responses;

        public FakeAgentChatClient(params ChatCompletion[] responses)
        {
            _responses = new Queue<ChatCompletion>(responses);
        }

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Action? BeforeCompleteChatAsync { get; set; }

        public Task<ChatCompletion> CompleteChatAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null)
        {
            BeforeCompleteChatAsync?.Invoke();
            Requests.Add(messages.ToArray());
            return Task.FromResult(_responses.Dequeue());
        }

        public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteChatStreamingAsync(
            IReadOnlyList<ChatMessage> messages)
        {
            throw new NotSupportedException("Streaming is not used by this test.");
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/AgentSkillRegistryTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/AgentSkillRegistryTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class AgentSkillRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_runs_registered_skill_by_name()
    {
        AgentSkillRegistry registry = new([
            new CalculatorSkill()
        ]);

        string result = await registry.ExecuteAsync(
            "calculate",
            """{"expression":"6 * 7"}""",
            new AgentToolExecutionContext("run_registry", "call_calculate"));

        Assert.Equal("42", result);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_unknown_skill_name()
    {
        AgentSkillRegistry registry = new([
            new CalculatorSkill()
        ]);

        AgentUnknownSkillException exception = await Assert.ThrowsAsync<AgentUnknownSkillException>(
            () => registry.ExecuteAsync(
                "missing_skill",
                "{}",
                new AgentToolExecutionContext("run_registry", "call_missing")));

        Assert.Equal("Unknown skill: missing_skill", exception.Message);
    }

    [Fact]
    public void GetRequiredSkill_returns_registered_skill_metadata()
    {
        AgentSkillRegistry registry = new([
            new WriteNoteSkill(Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.md"))
        ]);

        IAgentSkill skill = registry.GetRequiredSkill("write_note");

        Assert.Equal("write_note", skill.Name);
        Assert.Equal(AgentSkillRiskLevel.Medium, skill.RiskLevel);
        Assert.True(skill.RequiresConfirmation);
    }

    [Fact]
    public void GetRequiredSkill_rejects_unknown_skill_name()
    {
        AgentSkillRegistry registry = new([
            new CalculatorSkill()
        ]);

        AgentUnknownSkillException exception = Assert.Throws<AgentUnknownSkillException>(
            () => registry.GetRequiredSkill("missing_skill"));

        Assert.Equal("Unknown skill: missing_skill", exception.Message);
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/AgentToolPermissionPolicyTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/AgentToolPermissionPolicyTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class AgentToolPermissionPolicyTests
{
    [Fact]
    public void RequiresConfirmation_returns_false_for_low_risk_safe_skills()
    {
        IAgentSkill skill = new CalculatorSkill();

        bool requiresConfirmation = AgentToolPermissionPolicy.RequiresConfirmation(skill);

        Assert.False(requiresConfirmation);
    }

    [Fact]
    public void RequiresConfirmation_returns_true_when_skill_declares_confirmation()
    {
        IAgentSkill skill = new WriteNoteSkill(
            Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.md"));

        bool requiresConfirmation = AgentToolPermissionPolicy.RequiresConfirmation(skill);

        Assert.True(requiresConfirmation);
    }

    [Fact]
    public void RequiresConfirmation_returns_true_for_medium_or_higher_risk()
    {
        IAgentSkill skill = new TestRiskOnlySkill();

        bool requiresConfirmation = AgentToolPermissionPolicy.RequiresConfirmation(skill);

        Assert.True(requiresConfirmation);
    }

    private sealed class TestRiskOnlySkill : IAgentSkill
    {
        public string Name => "test_risk_only";

        public string Description => "A test skill with risk metadata.";

        public string ParametersJson => """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """;

        public AgentSkillRiskLevel RiskLevel => AgentSkillRiskLevel.Medium;

        public bool RequiresConfirmation => false;

        public Task<string> ExecuteAsync(
            string argumentsJson,
            AgentToolExecutionContext executionContext,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("ok");
        }
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/CalculatorSkillTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/CalculatorSkillTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class CalculatorSkillTests
{
    [Theory]
    [InlineData("""{"expression":"2 + 3 * 4"}""", "14")]
    [InlineData("""{"expression":"(2 + 3) * 4"}""", "20")]
    [InlineData("""{"expression":"10 / 4"}""", "2.5")]
    public async Task ExecuteAsync_calculates_basic_math_expressions(string argumentsJson, string expected)
    {
        CalculatorSkill skill = new();

        string result = await skill.ExecuteAsync(
            argumentsJson,
            new AgentToolExecutionContext("run_calculator", "call_calculator"));

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_missing_expression()
    {
        CalculatorSkill skill = new();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => skill.ExecuteAsync(
                """{}""",
                new AgentToolExecutionContext("run_calculator", "call_calculator")));

        Assert.Equal("Calculator skill requires a non-empty 'expression' argument.", exception.Message);
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/TimeSkillTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/TimeSkillTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class TimeSkillTests
{
    [Fact]
    public async Task ExecuteAsync_returns_current_time_from_clock()
    {
        DateTimeOffset fixedTime = new(2026, 7, 6, 9, 30, 0, TimeSpan.FromHours(8));
        TimeSkill skill = new(() => fixedTime);

        string result = await skill.ExecuteAsync(
            "{}",
            new AgentToolExecutionContext("run_time", "call_time"));

        Assert.Equal("2026-07-06T09:30:00.0000000+08:00", result);
    }
}
`````

</details>
<!-- END FILE -->

<!-- FILE: REPLACE tests/AgentLearning.Core.Tests/WriteNoteSkillTests.cs -->
<details>
<summary><strong>完整覆盖</strong> <code>tests/AgentLearning.Core.Tests/WriteNoteSkillTests.cs</code></summary>

`````csharp
using AgentLearning.Core.Skills;

namespace AgentLearning.Core.Tests;

public sealed class WriteNoteSkillTests
{
    [Fact]
    public async Task ExecuteAsync_appends_note_to_configured_file()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        DateTimeOffset fixedNow = new(2026, 7, 8, 10, 30, 0, TimeSpan.FromHours(8));
        WriteNoteSkill skill = new(notesFile, () => fixedNow);
        AgentToolExecutionContext executionContext = new("run_note", "call_note");

        string result = await skill.ExecuteAsync(
            """{"note":"记住：Tool Calling 需要权限控制。"}""",
            executionContext);

        string savedText = await File.ReadAllTextAsync(notesFile);
        Assert.Equal("write_note", skill.Name);
        Assert.Equal(AgentSkillRiskLevel.Medium, skill.RiskLevel);
        Assert.True(skill.RequiresConfirmation);
        Assert.Contains("Note saved to", result);
        Assert.Contains(executionContext.IdempotencyKey, savedText);
        Assert.Contains("2026-07-08T10:30:00.0000000+08:00", savedText);
        Assert.Contains("记住：Tool Calling 需要权限控制。", savedText);
    }

    [Fact]
    public async Task ExecuteAsync_rejects_empty_note()
    {
        string notesFile = Path.Combine(Path.GetTempPath(), $"notes-{Guid.NewGuid():N}.md");
        WriteNoteSkill skill = new(notesFile);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => skill.ExecuteAsync(
                """{"note":" "}""",
                new AgentToolExecutionContext("run_note", "call_empty")));

        Assert.Contains("note", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_same_operation_twice_writes_note_once()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        Func<DateTimeOffset> clock =
            () => new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.FromHours(8));
        WriteNoteSkill firstProcessSkill = new(
            notesFile,
            clock);
        WriteNoteSkill restartedProcessSkill = new(notesFile, clock);
        AgentToolExecutionContext executionContext = new("run_same", "call_same");
        const string argumentsJson = """{"note":"This operation must be written once."}""";

        string firstResult = await firstProcessSkill.ExecuteAsync(argumentsJson, executionContext);
        string secondResult = await restartedProcessSkill.ExecuteAsync(argumentsJson, executionContext);

        string savedText = await File.ReadAllTextAsync(notesFile);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal(1, CountOccurrences(savedText, executionContext.IdempotencyKey));
        Assert.Equal(1, CountOccurrences(savedText, "This operation must be written once."));
        Assert.False(File.Exists($"{notesFile}.tmp"));
    }

    [Fact]
    public async Task ExecuteAsync_different_operations_can_write_the_same_note_twice()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        WriteNoteSkill skill = new(notesFile);
        AgentToolExecutionContext firstContext = new("run_first", "call_note");
        AgentToolExecutionContext secondContext = new("run_second", "call_note");
        const string argumentsJson = """{"note":"The same text is intentional."}""";

        await skill.ExecuteAsync(argumentsJson, firstContext);
        await skill.ExecuteAsync(argumentsJson, secondContext);

        string savedText = await File.ReadAllTextAsync(notesFile);
        Assert.Equal(2, CountOccurrences(savedText, "The same text is intentional."));
        Assert.Contains(firstContext.IdempotencyKey, savedText);
        Assert.Contains(secondContext.IdempotencyKey, savedText);
    }

    [Fact]
    public async Task ExecuteAsync_concurrent_operations_preserve_every_note()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"agent-notes-{Guid.NewGuid():N}");
        string notesFile = Path.Combine(tempDirectory, "notes.md");
        WriteNoteSkill skill = new(notesFile);

        Task<string>[] writes = Enumerable.Range(1, 8)
            .Select(index => skill.ExecuteAsync(
                $"{{\"note\":\"Concurrent note {index}\"}}",
                new AgentToolExecutionContext("run_concurrent", $"call_{index}")))
            .ToArray();

        await Task.WhenAll(writes);

        string savedText = await File.ReadAllTextAsync(notesFile);
        for (int index = 1; index <= writes.Length; index++)
        {
            Assert.Equal(1, CountOccurrences(savedText, $"Concurrent note {index}"));
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
`````

</details>
<!-- END FILE -->

### 编译与自动化验收

在项目根目录执行：

```bash
dotnet test AgentLearning.sln
```

应看到的关键结果（耗时会因电脑而不同）：

```text
Passed! - Failed: 0, Passed: 104, Skipped: 0, Total: 104
```

<!-- END SELF-CONTAINED CODE -->

## 本章验收

- [ ] 能解释 MCP 是工具协议，不是工具选择模型。
- [ ] 能说明 stdio 的 stdout 为什么不能打印普通日志。
- [ ] 能沿调用链找到 AgentRunner、Adapter、Client、Server、Tool。
- [ ] 明白本地安全策略不能完全信任远端声明。
- [ ] 能解释当前 stdio 与真正跨电脑远程部署的区别。
- [ ] 104 个测试全部通过。

## 本章小结

工具已经可以跨进程，并通过统一协议动态发现。下一章详细实现 `search_knowledge` 背后的 RAG：文档切块、Embedding、向量检索、关键词检索和 Hybrid Search。

[下一章：RAG、Embedding 与 Hybrid Search](10-rag.md)

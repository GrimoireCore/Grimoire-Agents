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
string ragEvaluationPath = Path.Combine(
    AppContext.BaseDirectory,
    "evaluation",
    "rag-evaluation.json");
string groundednessEvaluationPath = Path.Combine(
    AppContext.BaseDirectory,
    "evaluation",
    "groundedness-evaluation.json");
string endToEndRagEvaluationPath = Path.Combine(
    AppContext.BaseDirectory,
    "evaluation",
    "e2e-rag-evaluation.json");
string endToEndRagBaselinePath = Path.Combine(
    AppContext.BaseDirectory,
    "evaluation",
    "e2e-rag-baseline.json");
string endToEndRagArtifactPath = AgentPathResolver.ResolveRuntimePath(
    AppContext.BaseDirectory,
    "memory/evaluation/e2e-rag-latest.json");

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

IAgentChatClient evaluationClient = new OpenAIChatClientAdapter(client);
AgentRunner agentRunner = new(profile, evaluationClient, memory, memoryPath, skillRegistry);
EndToEndRagRegressionRunner endToEndRagRegressionRunner = new(
    new EndToEndRagEvaluator(skillRegistry, evaluationClient),
    endToEndRagEvaluationPath,
    endToEndRagBaselinePath,
    endToEndRagArtifactPath,
    profile.Model,
    profile.EmbeddingModel);
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
Console.WriteLine($"RAG evaluation file: {ragEvaluationPath}");
Console.WriteLine($"Groundedness evaluation file: {groundednessEvaluationPath}");
Console.WriteLine($"End-to-end RAG evaluation file: {endToEndRagEvaluationPath}");
Console.WriteLine($"End-to-end RAG baseline file: {endToEndRagBaselinePath}");
Console.WriteLine($"End-to-end RAG latest artifact: {endToEndRagArtifactPath}");
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
Console.WriteLine("Local commands: /time, /calc <expression>, /eval-rag, /eval-grounding, /eval-rag-e2e");
Console.WriteLine();

if (profile.Stream && profile.NativeToolCalling)
{
    Console.WriteLine("Native tool calling is only implemented for non-streaming mode in this lesson.");
    return 1;
}

if (args.Length > 0)
{
    if (args.Length != 1
        || !args[0].Equals("--eval-rag-e2e", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Unsupported arguments. Use --eval-rag-e2e for the CI regression gate.");
        return 1;
    }

    try
    {
        EndToEndRagRegressionRunResult regressionResult = await endToEndRagRegressionRunner.RunAsync();
        PrintEndToEndRagRegressionRunResult(regressionResult);
        return regressionResult.Gate.Passed ? 0 : 2;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"End-to-end RAG regression gate failed to run: {exception.Message}");
        return 1;
    }
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

    if (await TryRunLocalSkillCommandAsync(
            input,
            profile,
            memory,
            memoryPath,
            ragEvaluationPath,
            groundednessEvaluationPath,
            skillRegistry,
            evaluationClient,
            endToEndRagRegressionRunner))
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
    string ragEvaluationPath,
    string groundednessEvaluationPath,
    AgentSkillRegistry skillRegistry,
    IAgentChatClient evaluationClient,
    EndToEndRagRegressionRunner endToEndRagRegressionRunner)
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

    if (input.Equals("/eval-rag", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            KnowledgeRetrievalEvaluator evaluator = new(skillRegistry);
            KnowledgeRetrievalEvaluationReport report = await evaluator.EvaluateAsync(ragEvaluationPath);
            PrintKnowledgeRetrievalEvaluationReport(report);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"RAG evaluation failed: {exception.Message}");
        }

        return true;
    }

    if (input.Equals("/eval-grounding", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            GroundednessEvaluator evaluator = new(evaluationClient);
            GroundednessEvaluationReport report = await evaluator.EvaluateAsync(
                groundednessEvaluationPath);
            PrintGroundednessEvaluationReport(report);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Groundedness evaluation failed: {exception.Message}");
        }

        return true;
    }

    if (input.Equals("/eval-rag-e2e", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            EndToEndRagRegressionRunResult regressionResult = await endToEndRagRegressionRunner.RunAsync();
            PrintEndToEndRagRegressionRunResult(regressionResult);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"End-to-end RAG evaluation failed: {exception.Message}");
        }

        return true;
    }

    return false;
}

static void PrintEndToEndRagRegressionRunResult(EndToEndRagRegressionRunResult result)
{
    PrintEndToEndRagEvaluationReport(result.Report);
    Console.WriteLine($"Latest artifact: {result.ArtifactFilePath}");
    Console.WriteLine($"Regression gate: {(result.Gate.Passed ? "PASS" : "FAIL")}");
    foreach (string failure in result.Gate.Failures)
    {
        Console.WriteLine($"  - {failure}");
    }
}

static void PrintEndToEndRagEvaluationReport(EndToEndRagEvaluationReport report)
{
    foreach (EndToEndRagEvaluationResult result in report.Results)
    {
        string status = result.Passed ? "PASS" : "FAIL";
        string retrieved = result.RetrievedMatches.Count == 0
            ? "<none>"
            : string.Join(
                ", ",
                result.RetrievedMatches.Select(match =>
                    $"{match.SourcePath}#chunk-{match.ChunkNumber}"));
        Console.WriteLine($"[RAG E2E] {status} {result.Case.Id}");
        Console.WriteLine($"  retrieved: {retrieved}");
        Console.WriteLine($"  retrieval correct: {result.RetrievalCorrect}");
        Console.WriteLine($"  citation correct: {result.CitationCorrect}");
        Console.WriteLine($"  citation repair attempted: {result.CitationRepairAttempted}");
        Console.WriteLine($"  grounded: {result.Grounded}");
        Console.WriteLine(FormattableString.Invariant(
            $"  groundedness score: {result.GroundednessJudgment.Score:F2}"));
        Console.WriteLine($"  answer: {result.Answer.Replace(Environment.NewLine, " ")}");
        if (!result.CitationCorrect)
        {
            Console.WriteLine($"  citation error: {result.CitationValidation.Error}");
        }

        if (result.GroundednessJudgment.UnsupportedClaims.Count > 0)
        {
            Console.WriteLine(
                $"  unsupported: {string.Join(" | ", result.GroundednessJudgment.UnsupportedClaims)}");
        }
    }

    Console.WriteLine(
        $"Retrieval: {report.RetrievalCorrectCount}/{report.TotalCount} ({report.RetrievalAccuracy:P0})");
    Console.WriteLine(
        $"Citations: {report.CitationCorrectCount}/{report.TotalCount} ({report.CitationAccuracy:P0})");
    Console.WriteLine(
        $"Groundedness: {report.GroundedCount}/{report.TotalCount} ({report.GroundednessRate:P0})");
    Console.WriteLine(
        $"Citation repairs: {report.CitationRepairCount}/{report.TotalCount} ({report.CitationRepairRate:P0})");
    Console.WriteLine(
        $"End-to-end: {report.PassedCount}/{report.TotalCount} ({report.PassRate:P0})");
}

static void PrintGroundednessEvaluationReport(GroundednessEvaluationReport report)
{
    foreach (GroundednessEvaluationResult result in report.Results)
    {
        string status = result.IsCorrect ? "PASS" : "FAIL";
        Console.WriteLine($"[Groundedness Eval] {status} {result.Case.Id}");
        Console.WriteLine($"  expected grounded: {result.Case.ExpectedGrounded}");
        Console.WriteLine($"  judged grounded: {result.Judgment.Grounded}");
        Console.WriteLine(FormattableString.Invariant(
            $"  score: {result.Judgment.Score:F2}"));
        Console.WriteLine($"  reason: {result.Judgment.Reason}");
        if (result.Judgment.UnsupportedClaims.Count > 0)
        {
            Console.WriteLine(
                $"  unsupported: {string.Join(" | ", result.Judgment.UnsupportedClaims)}");
        }
    }

    Console.WriteLine(
        $"Accuracy: {report.CorrectCount}/{report.TotalCount} ({report.Accuracy:P0})");
    Console.WriteLine(
        $"Grounded acceptance: {report.AcceptedGroundedCount}/{report.GroundedCaseCount} ({report.GroundedAcceptanceRate:P0})");
    Console.WriteLine(
        $"Unsupported rejection: {report.RejectedUnsupportedCount}/{report.UnsupportedCaseCount} ({report.UnsupportedRejectionRate:P0})");
}

static void PrintKnowledgeRetrievalEvaluationReport(KnowledgeRetrievalEvaluationReport report)
{
    foreach (KnowledgeRetrievalEvaluationResult result in report.Results)
    {
        string expected = result.ExpectsNoAnswer
            ? "<no answer>"
            : result.Case.ExpectedSourcePath!;
        string status = result.ExpectsNoAnswer
            ? result.NoAnswerCorrect ? "PASS" : "FAIL"
            : result.RecallAt3Correct ? "PASS" : "FAIL";
        Console.WriteLine($"[RAG Eval] {status} {result.Case.Id}");
        Console.WriteLine($"  expected: {expected}");
        if (result.RetrievedMatches.Count == 0)
        {
            Console.WriteLine("  retrieved: <none>");
            continue;
        }

        Console.WriteLine("  retrieved:");
        foreach (KnowledgeRetrievalMatch match in result.RetrievedMatches)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"    {match.Rank}. {match.SourcePath} (chunk {match.ChunkNumber}): combined={match.CombinedScore:F3}, vector={match.VectorScore:F3}, keyword={match.KeywordScore:F3}"));
        }
    }

    Console.WriteLine(
        $"Top 1: {report.Top1CorrectCount}/{report.AnswerCaseCount} ({report.Top1Accuracy:P0})");
    Console.WriteLine(
        $"Recall@3: {report.RecallAt3CorrectCount}/{report.AnswerCaseCount} ({report.RecallAt3:P0})");
    Console.WriteLine(
        $"No answer: {report.NoAnswerCorrectCount}/{report.NoAnswerCaseCount} ({report.NoAnswerAccuracy:P0})");
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

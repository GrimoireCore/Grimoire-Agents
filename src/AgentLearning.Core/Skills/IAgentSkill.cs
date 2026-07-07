namespace AgentLearning.Core.Skills;

/// <summary>
/// Agent 可以调用的一个技能。
/// 模型只负责“决定要不要调用”，真正执行动作的是这里的 C# 代码。
/// </summary>
public interface IAgentSkill
{
    /// <summary>模型调用工具时使用的函数名。</summary>
    string Name { get; }

    /// <summary>给模型看的技能说明，帮助模型判断什么时候该调用它。</summary>
    string Description { get; }

    /// <summary>给模型看的 JSON Schema，描述这个技能需要哪些参数。</summary>
    string ParametersJson { get; }

    /// <summary>
    /// 执行技能。
    /// argumentsJson 是模型按 ParametersJson 生成的参数 JSON。
    /// </summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}

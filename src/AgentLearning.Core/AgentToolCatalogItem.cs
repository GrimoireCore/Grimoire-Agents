using System.Text.Json.Serialization;

namespace AgentLearning.Core;

/// <summary>
/// 发给 Tool Router 的轻量工具目录项。
/// 它只包含工具名和短说明，不包含完整参数 Schema，这样工具变多时也不会把上下文撑大。
/// </summary>
public sealed record AgentToolCatalogItem(
    /// <summary>工具的真实函数名，Router 必须原样返回这个名字。</summary>
    [property: JsonPropertyName("name")]
    string Name,

    /// <summary>工具的短说明，帮助 Router 判断什么时候该选择它。</summary>
    [property: JsonPropertyName("description")]
    string Description,

    /// <summary>工具风险等级，让 Router 知道这个工具是不是会影响外部世界。</summary>
    [property: JsonPropertyName("risk_level")]
    string RiskLevel,

    /// <summary>这个工具执行前是否需要用户确认。</summary>
    [property: JsonPropertyName("requires_confirmation")]
    bool RequiresConfirmation);

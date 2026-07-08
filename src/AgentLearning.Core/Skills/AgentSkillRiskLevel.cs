namespace AgentLearning.Core.Skills;

/// <summary>
/// 技能的风险等级。
/// 等级越高，越不应该让模型在没有用户确认的情况下直接执行。
/// </summary>
public enum AgentSkillRiskLevel
{
    /// <summary>只读或纯计算，不会修改外部世界。</summary>
    Low,

    /// <summary>会写入本地文件、发送草稿、修改轻量状态等。</summary>
    Medium,

    /// <summary>可能删除数据、调用外部系统、影响用户资产或业务状态。</summary>
    High,

    /// <summary>付款、下单、批量删除等必须强确认的动作。</summary>
    Critical
}

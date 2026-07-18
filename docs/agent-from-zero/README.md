# C# Agent 从零到完整实现

这套教材由 12 个可独立分享的 Markdown 章节组成。学习者只需要这 12 个文件，不需要课程原仓库、Git 历史、Patch、截图资源或额外源码。

## 学习方法

1. 安装 .NET 8 SDK，并准备一个空目录。
2. 从第 1 章开始，按顺序学习，不要跳过章节。
3. 每章先理解文字与 Mermaid 流程图，再执行“本章完整文件代码”。
4. `新建` 表示创建文件；`完整覆盖` 表示用文档中的完整内容替换旧文件。
5. 运行每章给出的命令，并对照纯文本运行结果。
6. 完成本章验收清单后，再进入下一章。

## 章节目录

| 章 | 主题 | 完成后的自动化验收 |
|---|---|---:|
| [1](01-from-llm-to-agent.md) | 第一次模型调用 | Build 通过 |
| [2](02-profile-and-api.md) | 角色设定、配置与 System Message | 2 tests |
| [3](03-skills-and-tool-calling.md) | Skill 与原生 Tool Calling | 13 tests |
| [4](04-memory-and-context.md) | Memory、上下文窗口与 Workflow | 24 tests |
| [5](05-harness-react-state.md) | Agent Harness 与 ReAct 循环 | 25 tests |
| [6](06-guardrails.md) | 循环、超时、异常与结果保护 | 52 tests |
| [7](07-tool-router.md) | AI Tool Router 与工具筛选 | 59 tests |
| [8](08-approval-and-checkpoint.md) | 人工确认、状态机与可恢复 Agent | 89 tests |
| [9](09-mcp.md) | MCP Client、Server 与动态工具 | 105 tests |
| [10](10-rag.md) | RAG、Embedding 与 Hybrid Search | 108 tests |
| [11](11-rag-evaluation.md) | Grounding、引用与 RAG 评测 | 129 tests |
| [12](12-observability.md) | 结构化 Trace 与可观察性 | 132 tests |

## 最终成果

学完后，读者会得到一个包含 Profile、Memory、Skills、Tool Calling、Harness、Guardrails、AI Tool Router、人工审批、Checkpoint、MCP、RAG、评测和结构化 Trace 的完整 C# Agent。

每章都包含：

- 面向初学者的概念说明。
- 直接嵌入 Markdown 的 Mermaid 流程图。
- 本章所有新增或变更文件的完整内容。
- 可直接执行的构建、测试和运行命令。
- 不依赖图片的纯文本运行效果。
- 常见错误、验收清单和下一章衔接。

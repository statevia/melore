# Melore ドキュメント

第一読者は、Melore を実装する開発者と、Cursor などの AI エージェントです。

推奨読書順: **用語 → 要件 → アーキテクチャ → データモデル → 技術選定 → 実装計画 → 評価**。

| 文書 | 問い |
| --- | --- |
| [glossary.md](glossary.md) | 専門用語は何を指すか |
| [requirements.md](requirements.md) | 何を満たせばよいか |
| [architecture/overview.md](architecture/overview.md) | 何がどこにあり、どう流れるか |
| [architecture/data-model.md](architecture/data-model.md) | 何を永続化するか |
| [decisions/README.md](decisions/README.md) | なぜその技術にしたか |
| [guides/implementation-plan.md](guides/implementation-plan.md) | どの順で作るか |
| [guides/mcp-tool-candidates.md](guides/mcp-tool-candidates.md) | MCP ツールの追加候補 |
| [guides/search-gaps.md](guides/search-gaps.md) | 検索漏れと対応 |
| [guides/agent-task-accuracy-plan.md](guides/agent-task-accuracy-plan.md) | Melore の有無でタスク精度はどう違い、何を直すか |
| [evaluation.md](evaluation.md) | どう検証し、何をもって完了とするか |

対象リポジトリ（Statevia）の知識体系は Statevia 側の [`docs/README.md`](https://github.com/statevia/statevia/blob/main/docs/README.md) を正本とする。Melore はそれをインデックスする側であり、Statevia の契約を置き換えない。

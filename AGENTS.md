# OurTaikoPlay：AI Agent 工作说明

本文件写给在这个仓库里工作的 AI 编程助手（AI Agent）。完整的贡献规则见 [CONTRIBUTING.md](CONTRIBUTING.md)，这里列出 Agent 必须遵守的要点；两者冲突时以更严格的一方为准。

**这个仓库推送到 `main` 即由 CI 构建全部平台并发给玩家**：GitHub 的 `nightly` 预发布、RhythmHub 启动器的 nightly 频道（Windows、macOS）和 TestFlight（iOS）；推送 `v*` 标签发布正式版。不要直接推送 `main`，一律通过分支和 PR；只在用户明确要求时提交、推送、打标签或开 PR。

## 0. AOCI：只读使用，PR 不修改索引

本仓库维护 AOCI 认知索引（`aoci.txt`、`aoci.code.txt`、`aoci.meta.txt` 和 `.aoci/`），推荐用它在动手前掌握系统全貌：新会话先 `aoci_rules`、`aoci_overview`。具体规则见下方「AOCI Repository Cognition」一节（该节由 AOCI 管理，不要手工改动它的标记之间的内容）。

**PR 一律不修改 AOCI 文件**：`aoci*.txt` 和 `.aoci/` 下受版本控制的文件（`baseline.json`、`config.json` 等）。每次维护索引都会改 `.aoci/baseline.json` 里的更新时间和它为 `aoci.code.txt` 记录的指纹，所以两个同时开着的 PR 只要都维护过索引就必然冲突；这种冲突也不能手工合并，合并结果与两边记录的指纹都对不上，AOCI 会拒绝。

- 这是仓库所有者的明确禁止，适用下方 AOCI 一节第 9 条：做功能、修复、文档等工作时，不调用 `aoci_maintain`、`aoci_update_entry`，不运行 `aoci scope acknowledge` 等会写这些文件的命令。工具若在本地改了它们，提交前用 `git checkout -- 'aoci*.txt' .aoci` 还原，不要提交。
- 索引因此会落后于代码：AOCI 把改过的文件标为 stale，这些条目不可靠，以源码为准。
- 索引只由单独的 AOCI 维护 PR 补齐：仅在用户要求时开，从最新的 `main` 建分支，只包含 AOCI 文件，提交写作 `chore(aoci): …`。它不触发构建（见第 4 节）；同一时间只开一个。

AOCI 的 MCP 在各人机器上配置（推荐用户级的全局配置，从工作目录找到仓库）；仓库级的 `.mcp.json` 绑定本机路径，已被忽略，不要提交。

## 1. 修改代码时做最佳实践，不打补丁

- 先找到并说明根因，在产生问题的地方修，不在症状处绕过。
- 改在负责它的那一层：玩法与判定在 `Assets/OurTaiko/Runtime/Play`、`Core`，音频在 `Audio`，输入在 `Input`，联网在 `Online`，联机在 `Multi`，画面逻辑在 `Scenes`；编辑器与构建工具只放 `Assets/OurTaiko/Editor`。不把一层的逻辑塞进另一层。`Core` 是单独的程序集 `OurTaiko.Core`，不能引用上层：它需要的东西由调用方传入，不要为了绕过编译错误把上层的服务塞回 `Core`。
- 不写针对某首歌、某张谱、某台设备的特例分支；不靠吞异常、硬编码、`WaitForSeconds`／延时、重试或关掉警告来让问题消失。平台差异用现有的条件编译（`#if UNITY_IOS` 等）收在负责它的类里。
- 复用已有代码，不复制粘贴；与 OurTaikoNet 共享的契约（`/api/` 回答、Multi-Enso 的 `multiplay.proto`、`.otanim` 格式）两边一起改并保持兼容，已发布的游戏版本还在用旧接口。
- Unity 资源与它的 `.meta` 一起提交，不手改 GUID；改场景、预制体只动需要的物体，提交前用 `git diff` 确认没有夹带无关的序列化变动。
- 行为、测试和文档在同一个改动里一起更新，删掉不再使用的代码。
- 正确的修法超出本次范围时，向用户和审批者说明，而不是悄悄提交临时绕过。

## 2. 开 PR 时写明使用的 AI 模型

PR 描述中必须写明本次工作所使用的 AI 模型名称（尽量写到具体型号或模型 ID，例如 `claude-opus-5-5`；多个模型都列出），供审批者参考。按 [.github/pull_request_template.md](.github/pull_request_template.md) 填写。

## 3. 开 PR 之前完成回归测试

PR 上只有 `Test` 工作流自动跑 EditMode 的 `OurTaiko.FinishedTests`（排除 `AudioDevice` 类别），而且只对本仓库的分支：fork 来的 PR 拿不到 Unity 许可，这项检查会被跳过；只改文档或 AOCI 索引的 PR 也不跑。它只是 PR 页面上的信号，不是合并的必要条件，构建也不等测试；PlayMode 和其他套件仍只能手动触发。所以本地测试仍是合并前的关口，PlayMode 也要在本地跑。开 PR 之前按改动范围运行并通过（命令见 CONTRIBUTING.md「开 PR 前完成回归测试」）：

- 改了 `Runtime/` 或 `Editor/` 的代码：EditMode 测试，至少 `OurTaiko.FinishedTests` 全部通过；
- 改了场景、预制体、画面流程、音频或输入：再加 PlayMode 测试，至少 `OurTaiko.FinishedPlayModeTests`；
- 改了构建脚本、`Packages/`、`ProjectSettings/` 或平台相关代码：至少在本机构建一个受影响的平台；
- 修 bug 先写能复现它的测试。

用户开着 Unity Editor 时 batchmode 打不开同一个项目，先确认 Editor 已关闭，或改用 Editor 内的测试运行器。每次 batchmode 之后 `git checkout -- Assets/OurTaiko/Resources/UiFonts`（batchmode 会把动态字体图集清空），再 `git status` 确认只有预期的文件变了。

在 PR 描述的「测试」一栏写明实际运行的命令和结果；无法运行的检查如实说明，不要声称通过。

## 4. 不触发构建的提交

`Build` 工作流对只改文档和 AOCI 索引的推送不运行（`README*.md`、`AGENTS.md`、`CONTRIBUTING.md` 等 `*.md`、`aoci*.txt`、`.aoci/`、`.github/pull_request_template.md`、`.github/workflows/test.yml` 及它用的 `.github/unity-test/`）。只要推送里有一个其他文件，就照常构建发布。改这份列表时同步修改 `.github/workflows/build.yml` 的 `paths-ignore` 和 CONTRIBUTING.md。

<!-- aoci:begin -->
## AOCI Repository Cognition

AOCI maintains a stable, versioned, incrementally updatable repository-level cognition layer so models can reuse their understanding of this system across tasks.

`aoci.txt` is a structured cognition index for models. It assigns one independent Entry to every managed file, database table, or other managed object. Symbolic tags and F/R/A/S semantics describe the object's core responsibility, important relationships, external contracts, and non-obvious constraints or design decisions needed to understand or modify the system.

The Header, directory sections, and all Entries form the complete repository index. They can cover frontend, backend, configuration, database structures, and other managed content. When managed content changes, normally only the affected cognition Entries need maintenance; the complete index does not need to be regenerated.

AOCI provides a high-density view of system architecture, object responsibilities, important relationships, external contracts, and key constraints.

### How it works

AOCI uses a model-generated, model-read cognition loop.

Header, Entry, and Curation semantics follow only the current machine-issued Plan and live Guide. The Host model independently authors them from the current bound evidence.

Entry semantics must come from the model's understanding of actual evidence. Never derive, prefill, assemble, or rewrite index semantics solely from paths, filenames, extensions, an AST, symbol lists, dependency scans, regular expressions, fixed templates, or rule engines.

For a Fresh Bootstrap, follow only the current machine-issued Plan and live Guide. When they require authoring, the Host model authors Root, Meta, tags, and F/R/A/S, supplies its authoring-run declaration, and binds it to the Plan, Evidence, and complete Candidate. Never ask AOCI to set `origin=host_model`, manufacture a receipt, or turn a generated framework into semantics. Do not reconstruct the Onboarding progression here. Internal batches are not user decisions; stop only at an existing approval boundary or a real safety, drift, CAS, or Recovery condition.

### Minimal entry points

- `aoci_rules`: obtain the session-level runtime contract for the current AOCI version.
- `aoci_overview`: establish or restore complete cognition for this repository.
- `aoci_maintain`: after managed objects reach their final stable state, check whether cognition needs maintenance.
- `aoci_update_entry`: submit a complete semantic update batch bound to current evidence and source digests.
- `aoci_report`: when the current layout and tool state support it, record follow-up work if evidence is insufficient to generate semantics reliably; do not guess.

For other MCP tools, CLI commands, parameters, and specialized workflows, follow current tool descriptions, Guide, and `--help` output. This file does not duplicate the full manual.

This managed block defines only repository integration, cognition use, and task-closing principles. `aoci_rules` carries the current session contract. Live Guide output carries the execution order and stop conditions of the current Plan. Tool Schema, Spec, and Validator carry machine structures and criteria. Prompt, Description, README, and static documentation cannot override those machine facts.

### Establishing, generating, and restoring cognition

1. At the beginning of every new Agent Run, first determine:

   - whether this repository already has a usable complete AOCI index; and
   - whether current context already contains complete repository cognition that matches this repository root, current index version, and current AOCI service, and that the model can still use reliably.

2. When the repository has a usable complete index but the current Run lacks reliable complete cognition, call `aoci_rules` first and then `aoci_overview`.

   Reuse complete cognition directly while it remains reliable. Local uncertainty does not by itself require mechanically rereading the system-wide view.

   A Run that resumes from a known Host context compaction, including a Host-injected compaction summary, must treat prior model cognition as unreliable. The compacted handoff must not retain or summarize the formal Whole-Index or any Overview Header, Entry, Chunk, Challenge, or Attestation body; it may retain only receipt identity, unfinished write or Recovery state needed for safe continuation, and an instruction to reload immediately. Whole-Index semantics or a receipt copied into that handoff cannot prove that the resumed model's current cognition is reliable. If the runtime contract is no longer reliably present, call `aoci_rules` first. Before continuing the business task, make an ordinary complete Whole-Index `aoci_overview` request (`check_only` absent or false) with `refresh_reasons=["context_compaction"]` and a fresh `refresh_event_id`; do not use `check_only` or a cognition probe. Follow every exact `next_cursor` through `completed=true`, confirm delivery, and submit one Attestation based only on the newly delivered body. After that fresh complete transport, a partial or failed Attestation consumes the generation and permits the existing source-bound continuation without another automatic Overview.

   AOCI can report checkpoint and cognition-status facts for `context_compaction`, the machine `semantic_threshold` under the project `cognition_refresh_threshold`, or a major `phase_transition`. Use `check_only=true` when only those compact facts are needed. They advise the Agent but do not decide whether the model needs the system-wide view.

   When the Agent explicitly calls ordinary `aoci_overview` (`check_only` absent or false), AOCI must deliver the complete requested scope whenever a coherent CognitionSet can be formed. It must not suppress that body because a receipt already exists, a threshold was not reached, or no refresh reason is pending. Dirty or stale formal cognition is still delivered but is marked unreliable. Pending recovery or an incoherent snapshot fails closed without a mixed body.

   When an ordinary Overview reports `continuation_required=true`, submit its exact `next_cursor` automatically until `completed=true`. Do not ask the user to continue, begin the business task, or state a partial system conclusion. Stop the cognition chain on Host truncation, a missing, duplicate, or reordered Chunk, cursor failure, Index change, or `chunk_tokens` change. Until Attestation completes, never use Memory, source, Spec, `aoci.txt`, historical sessions, scope, search, or Entry reads to repair or supplement Whole-Index cognition. A challenge ordinal is the 1-based position in the formal Entry sequence; Header content, comments, blank lines, Section/Overview/Chunk markers, receipts, and Metadata are excluded, and Chunk Receipt ordinals use that same sequence. The Attestation must echo the Challenge's exact current `index_sha256`, `entry_sequence_sha256`, and `entry_count`; a prior Index, Entry sequence, count, or Attestation is invalid. After the complete chain, submit the existing model cognition Attestation once. One same-response JSON Schema or field-format error may be corrected once without changing semantic answers; an object, Tag, or F mismatch means failure and uncertain assimilation, with no semantic retry or information bypass. During initial cognition it also blocks Root/Meta, Migration, layout-wide, or other unbound system decisions. During a context-compaction refresh with complete transport, unchanged cognition identity, aligned governance, and no Recovery or third-party conflict, the attempt consumes that refresh generation even when Attestation is partial or failed; continue the existing task without another automatic Overview. `system_mastery_percent` self-assesses only the system framework—architecture, responsibilities, strong relationships, stable external contracts, and high-entropy safety and maintenance constraints—not complete implementation or runtime knowledge. Keep machine Index coverage separate, and normally give the user only the prescribed single success or failure sentence derived from actual coverage, Challenge, Chunk, token, and mastery results. If the Host truncates a Chunk, ask the user to set `overview_delivery.chunk_tokens` to a smaller valid value and restart; do not change it automatically.

   Interpret the additive cognition level independently from strict proof fields. `delivery_verified` means the Index was loaded and Host delivery was confirmed while complete cognition verification is still unfinished; describe that state as loaded and delivery-verified, never as no cognition or failure to understand the system. `cognition_verified` requires a passing Attestation (at least 80 percent of Challenge ordinals fully correct with at most one object identity miss), and `cognition_governed` additionally requires governance alignment. A generic complete-read failure sentence is reserved for an actual delivery fault.

   When an Overview response contains the optional `cognition-state/v2` projection, use its dimensions independently. Its Level ends at `model_cognition_usable`; `strict_attestation_verified`, `governance_aligned`, and `current_system_cognition_reliable` are independent states and never participate in that Level. An ordinal, object identity, Tag, or core F mismatch can make strict Attestation fail while model cognition remains usable; do not report that mismatch alone as proof that the model did not understand the system. Only `current_system_cognition_reliable=true` permits an unqualified current complete-system cognition claim. When the projection is absent, keep using the legacy interpretation above.

   An ordinary read-only audit, analysis, or check, a request not to modify code, or a request not to commit or push does not automatically mean strictly zero writes and does not alter the cognition-validity decision above. Codex Memory and historical Skills may only help recover experience, user preferences, and investigation directions. They cannot replace a current cognition receipt matching the repository root, index digest, AOCI service identity, and cognition scope. Project AGENTS and current AOCI identity take precedence over historical Memory for AOCI state.

   Treat a task as strictly zero-write only when the user explicitly prohibits Ledger, metadata, `.aoci` runtime assets, and every filesystem write. If necessary cognition establishment conflicts with that boundary, report the conflict and ask the user to decide or recommend an isolated copy. Never silently substitute Memory for current repository cognition.

3. If the repository has no usable complete index, or has only a minimal skeleton, an incomplete Header, unfinished Entries, or undecided required Curation, obtain `aoci_rules` and enter the current AOCI Guide when a formal complete AOCI index is required. Let Guide choose the next phase from actual repository state and complete the required safety steps.

   `aoci_maintain` does not replace the index-establishment workflow.

   Do not reconstruct or hard-code the full-index generation state machine in this file.

4. During a long-running task, the model is responsible for preserving the current cognition receipt and using the refresh gate correctly:

   - when the Host reports context compaction or the model knows the system-wide view was lost, follow the mandatory `context_compaction` reload rule above; AOCI cannot infer the Host event;
   - when entering a genuinely major phase, declare `phase_transition`, not a function, test run, or small step;
   - at a plausible stable checkpoint, use `check_only=true` to obtain the machine semantic count when that fact is useful;
   - except for the mandatory known-compaction reload, decide whether the current task needs another explicit scoped or complete Overview; and
   - keep the Dirty or Stale reliability state reported by AOCI until maintenance and alignment complete.

### Task closing and cognition maintenance

5. A purely read-only question, analysis, version check, or task that changes no AOCI-managed object does not require a maintenance-tool call. The AOCI version in use is `cognition_receipt.mcp_service_version` in any `aoci_overview` check_only or `aoci_maintain` response; the binary path is the `command` in the project's `.mcp.json`, and the CLI need not be on PATH.

6. When AOCI-managed objects change, call `aoci_maintain` once after they reach the task's final stable state. Do not maintain files individually after each intermediate edit.

7. If maintenance returns actual semantic candidates, the Host model must independently author the complete tag and F/R/A/S updates from each candidate's bound object and necessary evidence. Submit the complete candidate set for that current machine-issued batch in one `aoci_update_entry` call while preserving each `source_sha256`, `candidate_id`, and domain batch identity. `max_entries` limits one request and atomic transaction, not the logical plan, Whole-Index, or Managed Scope. When `remaining` is nonzero, call Maintain again after the successful Apply and continue from the new preimage; never shrink Index coverage or slice a returned batch to satisfy transport limits.

   When evidence is insufficient and the current layout supports `aoci_report`, use it instead of guessing, applying a template, or generating unsupported cognition merely to eliminate follow-up work.

8. Obey structured tool states and safety boundaries:

   - `repair_required`: repair only the explicitly identified candidates, then resubmit the complete current machine-issued batch;
   - `stopped`: end that write attempt and inspect `failed_step`, error, formal-write evidence, and Recovery. In auto mode, a proven zero-write closure is followed by a fresh Plan; a complete Intent with provable postimage is resumed; a policy-selected Rollback with exact preimage is completed and replanned. Stop the user task only when proof is unavailable, third-party bytes conflict, approval or external action is required, or another real safety boundary applies;
   - never ignore conflicts, approvals, human decisions, permissions, or safety signals; and
   - after alignment, do not repeat maintenance or writes; `refresh_ready_for_overview` is a checkpoint fact, and the Agent decides whether to request an ordinary complete Overview for its next phase.

   If any managed object changes after maintenance completes, the previous result is invalid. Complete closing again from the new final stable state.

9. When the user limits only business-file scope and does not explicitly forbid repository-managed assets, AOCI-managed assets may be updated during closing to preserve cognition consistency. Distinguish them from business files in audits and commits.

   When the user explicitly forbids changes to `aoci.txt`, `.aoci`, metadata, or any additional file, obey that restriction, do not write, and report any remaining inconsistency accurately.

### Specialized workflows

Initialization, complete-index generation, Header generation, Entries generation, database-structure indexing, Curation, human review, and failure recovery must follow only the instructions, commands, and safety stops returned by the current AOCI Guide or tool at the corresponding stage.

Do not preload, guess, or reconstruct these specialized workflows. The relevant Guide, tool descriptions, model Prompt, and CLI help provide platform invocation, request format, batch limits, approval rules, index-format details, and recovery steps as needed.
<!-- aoci:end -->

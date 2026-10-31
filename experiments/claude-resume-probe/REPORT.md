# Claude Desktop: automatic Pigeon monitor recovery — probe report

Date: 2026-10-31 (all times UTC). Author: Claude (Opus 5.5) in the Claude Desktop Code tab, coordinating session `3c1a5fa5-9504-400e-ae83-12d51e361755`, with the user's approval at each install step.

## 1. Question

After a Claude Desktop conversation resumes, Pigeon's `SessionStart` hook runs and the MCP bridge restores the conversation's topic association, but no `pigeon messages wait` monitor starts by itself. We wanted to know:

1. Does Claude Code offer a supported way for a hook, plugin or other session feature to start or re-arm a watcher attached to the **same** conversation **without a new user prompt**, including while the conversation is idle?
2. Does it work in **Claude Desktop**, not only in the terminal CLI?
3. If not, is there a workaround?

## 2. Short answer

- The documented mechanisms (`asyncRewake` hooks, plugin monitors, `SessionStart` on resume) **work as documented in the interactive terminal CLI**, on both 2.1.314 and 2.1.309.
- **Claude Desktop behaves differently with the very same 2.1.309 binary**: `asyncRewake` alone does not wake the model, plugin monitors never start, and a reopened conversation does not resume until a message is sent. The likely cause is how Desktop launches Claude Code (SDK streaming mode, see §4.3). This is an inference; the behaviour differences are observed.
- **Workaround that works in Desktop (observed)**: a `Stop` hook with `"async": true` **and** `"asyncRewake": true` that waits in the background and exits 2 on an event. It woke an idle Desktop conversation after 3 minutes with no prompt, received the correct session ID, re-armed itself after the wake turn and woke it again. It was tested with a synthetic trigger file, not with real Pigeon.

## 3. Environment

| Item | Value |
|---|---|
| OS | Windows 11 Enterprise 10.0.26220 |
| Claude Desktop | updated during the test from `Claude_4.29.0.0` to `Claude_4.4212.0.0` (MSIX) |
| Claude Code used by Desktop | `%APPDATA%\Claude\claude-code\2.1.309\9078b9c1d466\claude.exe` (2.1.308 also on disk). The Desktop update did **not** change it; a conversation started after the update still recorded `"version":"2.1.309"` |
| Standalone CLI | `%USERPROFILE%\.local\bin\claude.exe`, 2.1.314 (`claude update` reported it current; the docs changelog lists 2.1.316) |
| Desktop launch arguments (from the process list) | `--output-format stream-json --verbose --input-format stream-json --effort=… --model=… --permission-prompt-tool=stdio --resume=<id>` |
| Shared state | Both copies read the same `~/.claude` settings, plugins and transcripts |
| Gating env vars | `DISABLE_TELEMETRY`, `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`, Bedrock/Vertex/Foundry: none set |
| Pigeon plugin | `pigeon@pigeon-local`, local scope for `C:\Projects\Pigeon`; source `integrations/claude-plugin`. Installed cache was 0.1.2 (no hooks) until 03:06:49, then 0.2.0 |

Docs (Desktop page, "Claude Code version in the Code tab"): the Code tab "runs its own copy of Claude Code", which "the desktop app downloads and updates", so it "can differ from the `claude` command in your terminal". The only documented update path is **Help > Check for Updates…** followed by a new session; no setting chooses or pins the version.

## 4. What the documentation says (checked against the pages)

| Topic | Documented behaviour | Source |
|---|---|---|
| Hook handler types | `command`, `http`, `mcp_tool`, `prompt`, `agent`. None invokes a built-in tool such as Monitor | hooks.md |
| `SessionStart` output | Context only (`additionalContext`, `initialUserMessage`, `watchPaths`, `sessionTitle`, `reloadSkills`). `initialUserMessage` starts a turn only "in non-interactive mode with the `-p` flag" | hooks.md |
| `async` | Output "is delivered on the next conversation turn. If the session is idle, the response waits until the next user interaction." | hooks.md, "Run hooks in the background" |
| `asyncRewake` | "runs in the background and wakes Claude on exit code 2"; "Exception: an `asyncRewake` hook that exits with code 2 wakes Claude immediately even when the session is idle." Timeout is still enforced for `asyncRewake` | hooks.md |
| Plugin monitors | `monitors/monitors.json` or `experimental.monitors`; fields `name`, `command`, `description`, `when` (`"always"` = "starts at session start and on plugin reload"; `"on-skill-invoke:<skill>"`). "What it prints reaches Claude as notifications." Not started with `-p`; min v2.1.310 only on Bedrock/Vertex/Foundry or with telemetry/nonessential traffic disabled | plugins/components.md, plugins/manifest-reference.md |
| Resume | `SessionStart` runs again with `source: "resume"`. "Background Bash and monitor tasks aren't" restored; unfinished background work appears as a note and "Claude Code doesn't start a turn from those notes" | sessions.md, hooks.md |
| Channels | MCP push (`claude/channel`), research preview, launch flags required, Desktop support not documented. Not tested here | channels.md |

The docs are CLI-centric. None of these pages describes Desktop-specific behaviour.

## 5. Chronology

### 5.0 Baseline observations in the coordinating conversation (Desktop 2.1.309)

- 03:03 and 03:05: Pigeon's `UserPromptSubmit` `mcp_tool` hook (`plugin:pigeon:pigeon/pigeon_session_restore`) ran and returned `null`. No `SessionStart` record at the original start — the installed cache (0.1.2) had no `hooks/` folder.
- 03:06:49: Pigeon plugin updated to 0.2.0 (`hooks/hooks.json` present).
- 03:09:53: `SessionStart:resume` ran `pigeon hook session-start claude-code` (exit 0) and injected context asking the model to re-arm the monitor "on the next model turn". About 35 s before the user's next prompt, so something other than a sent message started that process (opening/focusing the conversation is a guess; not reproduced).
- 03:11:52: Monitor tool started `pigeon messages wait --claude 3c1a5fa5-…` (30 min timeout).
- ~03:13:45: the Claude Code process restarted; the monitor was killed. The "stopped" task notification was queued and only seen with the next prompt at 03:17. No `SessionStart` record for that restart.
- 03:43:28: the coordinating conversation's process restarted again (after the Desktop app update; process creation time). Again no `SessionStart` record.
- Anomaly (unexplained): after the 0.2.0 update, no further `UserPromptSubmit` hook records appear in this conversation's transcript, although they do appear in the test conversations. The transcript's only hook records are `UserPromptSubmit` at 03:03:23 and 03:05:21 and `SessionStart:resume` at 03:09:53.

### 5.1 Run 1 — probe v0.1.0 in Desktop (session `51a7c1d5-…`)

Probe plugin `pigeon-resume-probe` (directory marketplace `experiments/claude-resume-probe`, installed at local scope):
- **A**: `SessionStart` (`startup|resume`) command hook, `"asyncRewake": true`, `timeout: 30`. Logs input `session_id`/`source` and env `CLAUDE_CODE_SESSION_ID`, sleeps 10 s, prints a marker to stderr, exits 2.
- **B**: plugin monitor in `experimental.monitors`, `when: "on-skill-invoke:probe"`; skill had `disable-model-invocation: true`.

Observed:
- A fired with the correct ID in both the hook input and the env. Exit 2 was recorded as `hook_non_blocking_error`; the marker never reached a model request. The first prompt was queued ~1 s after the hook exited, so the hook appears to have **blocked startup** for 10 s.
- B never started. Probe flaws: the start brief was prefixed with background text, so the slash command was mid-message; `disable-model-invocation` hid the skill from the model; the user's later manual invocation also produced no B process.
- Resume: archived the conversation (process ended), unarchived, opened it in a window without sending anything. For 60 s: no process, no `SessionStart`, `isRunning: false`. The app states it resumes on the next message.

### 5.2 Hypotheses after run 1

1. Version gap: Desktop runs 2.1.309, docs describe 2.1.316.
2. `asyncRewake` behaves differently in some launch mode.
3. Probe B flaws: `experimental.monitors` vs default file, user slash command vs Skill tool, no `always` control.

### 5.3 Binary inspection (read-only `grep -a` of the 2.1.309 `claude.exe`)

- Schema: `asyncRewake: "If true, hook runs in background and wakes the model on exit code 2 (blocking error). Implies async."`; also internal `rewakeMessage`, `rewakeSummary` (default summary "Stop hook feedback").
- Background decision function (minified): `function yU(e,n){return(e.async===!0||e.asyncRewake===!0&&(!_e()||hCt()))&&n!==!0}`. Candidate definitions found: `function _e(){return!n().host.launchOptions.isInteractive()}` and `function hCt(){return n().host.launchOptions.hasStreamingInput()}`. Because minified names collide, which branch failed in Desktop could not be determined. Conclusion: `asyncRewake` alone runs in the background only under launch-mode conditions; `async: true` bypasses them.
- `on-skill-invoke`, `monitors.json` strings exist in 2.1.309, so plugin monitors are not simply missing from that version.

### 5.4 Runs 2 and 3 — probe v0.2.0 in Desktop

Changes: A1 = `asyncRewake` only; A2 = `async: true` + `asyncRewake: true`; B1 = `monitors/monitors.json`, `when: "always"` (control); B2 = `on-skill-invoke:probe`, skill invocable by the model; start brief asks the model to call the Skill tool.

- Run 2 (session `b882405b-…`, interrupted by the user): A1 and A2 started with the correct ID; no B entry.
- Run 3 (session `c85745b8-…`):
  - A1: same as run 1 — synchronous, `hook_non_blocking_error`, nothing delivered.
  - **A2: woke the model before any prompt.** At 03:52:51 the conversation received a `<task-notification>` "Stop hook feedback" with the marker as a system reminder; the model replied unprompted at 03:52:55 with the marker and the correct session ID.
  - B1: never started. B2: Skill tool ran at 03:52:59; monitor never started.
  - Resume: after the user archived the conversation (process ended), I unarchived and opened it; 90 s with no process, no `SessionStart`, no log or transcript change.

### 5.5 Run 4 — standalone CLI 2.1.314, interactive, `--plugin-dir` (session `3f9db8ec-…`)

No config change (`--plugin-dir` loads the plugin for that session only). Launched in a terminal tab without any prompt; then the tab was closed and `claude --resume <id> --plugin-dir …` was run, again without any prompt.

| Probe | Startup | Resume |
|---|---|---|
| A1 (`asyncRewake` only) | background; woke the model | woke the model (`source=resume`) |
| A2 (`async` + `asyncRewake`) | woke the model | woke the model |
| B1 (monitor, `always`) | started ~6 s in; output delivered; env `CLAUDE_CODE_SESSION_ID` correct | started again (new PID); delivered |

B1 started while both A hooks were still sleeping, so startup was not blocked.

### 5.6 Run 5 — Desktop's own 2.1.309 binary as an interactive CLI (session `f059f436-…`)

Same as run 4 but executing `%APPDATA%\Claude\claude-code\2.1.309\…\claude.exe` directly, with `DISABLE_AUTOUPDATER=1` for that tab.

Result: identical to run 4 — A1, A2 and B1 all worked at startup and on resume without a prompt, with the correct session ID.

**Conclusion of runs 3–5: the version is not the cause. The same 2.1.309 binary works interactively and fails inside Desktop, so the difference comes from how Desktop runs it** (SDK streaming mode with `--input-format stream-json` and `--permission-prompt-tool=stdio`). That last attribution is an inference.

### 5.7 Run 6 — probe v0.3.0: a Stop hook as the waiter, in Desktop (session `00641e9c-…`)

Design (`v0.3.0/stop_waiter.py`, `hooks.json`):
- `Stop` hook, `"async": true`, `"asyncRewake": true`, `timeout: 600`.
- Reads `session_id` from stdin, writes a per-session lock containing a random token ("newest waiter supersedes older ones"), then polls every second:
  - lock token changed → exit 0 (superseded);
  - trigger file `logs/trigger-<session_id>` present → delete it, print `PROBE_C_WAKE …` to stderr, exit 2;
  - 570 s elapsed → exit 0 quietly (before the hook timeout).
- Stand-in for `pigeon messages wait --claude <session_id>`.

Observed:

| Time | Event |
|---|---|
| 04:13:17 | first turn ("ready") ended; waiter armed with the correct session ID |
| 04:16:29 | after ~3 min idle, trigger created; waiter exited 2; **Desktop woke the conversation without a prompt**; reply `PROBE_C_WAKE note=idle-3min …` |
| 04:16:35 | the wake turn's `Stop` armed a new waiter automatically |
| 04:17:48 | second trigger woke it again (`note=rearm-check`) |
| 04:17:53 | re-armed again |

Side observations:
- The hook also loaded in the coordinating conversation (same project) and armed waiters for `3c1a5fa5-…`; newer waiters superseded older ones, so only one was live; none was triggered.
- Because a directory-marketplace plugin loads from its source folder, sessions that loaded the probe keep running its `Stop` hook after uninstall. After cleanup a waiter re-armed in the coordinating conversation; it was stopped and `plugin/stop_waiter.py` was replaced with a no-op (original kept in `v0.3.0/`).

## 6. Summary matrix

| Capability | Docs | CLI 2.1.314 | CLI 2.1.309 (Desktop's binary) | Desktop 2.1.309 |
|---|---|---|---|---|
| `SessionStart` `asyncRewake` alone wakes the model | yes | yes | yes | **no** (runs synchronously, `hook_non_blocking_error`, blocks startup) |
| `SessionStart` `async` + `asyncRewake` wakes the model | yes | yes | yes | **yes** (at startup) |
| Plugin monitor `when: always` | starts | starts, correct ID | starts, correct ID | **never starts** |
| Plugin monitor `on-skill-invoke` | starts | not tested | not tested | **never starts** |
| Resume without a prompt runs `SessionStart` | yes | yes | yes | **no resume until a message is sent** |
| `Stop` hook `async` + `asyncRewake` waiter wakes an idle session | implied | not tested | not tested | **yes** (3 min idle; re-arms each turn) |
| Correct session ID available to the hook | — | yes (hook input and env) | yes | yes (hook input and env) |

## 7. Conclusions

1. The documentation is accurate for the interactive CLI. Desktop's Code tab deviates from it on 2.1.309, independent of version.
2. In Desktop, `asyncRewake` needs `"async": true` as well. Without it the hook is treated as an ordinary synchronous hook.
3. In Desktop, plugin monitors did not start in any configuration tried.
4. In Desktop, a reopened conversation has no process until a message is sent, so nothing (hooks included) can run before that message.
5. A `Stop` hook with `async` + `asyncRewake` is a working, prompt-free wake path in Desktop, and it re-arms itself after every turn. Each conversation's waiter receives its own session ID, so Pigeon can route by tool + session ID.

## 8. Proposed design for Pigeon (not yet implemented or tested with real Pigeon)

- **Desktop**: add to the Pigeon plugin:
  - `Stop` hook, `async: true`, `asyncRewake: true`, long `timeout`, running a Pigeon command that (1) exits 0 immediately when the session has no Pigeon association, (2) takes a per-session supersede lock, (3) blocks in `pigeon messages wait --claude <session_id>` and exits 2 with a fixed wake instruction on a wake, (4) exits 0 shortly before its timeout.
  - Optionally a `SessionStart` hook with the same flags to cover a brand-new conversation before its first turn ends.
  - The model still confirms the wake generation, receives, acknowledges and completes the wake as today.
- **Terminal CLI sessions**: a plugin monitor (`monitors/monitors.json`, `when: always`) running a `pigeon messages wait` loop starts by itself at startup and on resume with the right session ID, without the Monitor tool.

Known limits:
- A new conversation has no waiter until its first turn ends (unless the `SessionStart` variant is added).
- Idle time longer than the hook timeout leaves no waiter until the next turn; the maximum allowed timeout is untested. Alternative: wake the model periodically to re-arm (costs a model turn each time).
- Desktop resume needs one user message; the first turn after it re-arms the waiter.
- One background process per connected conversation; every wake is a model turn.
- Wakes appear in the transcript as "Stop hook feedback".
- Session ID is a routing claim, not authentication.
- The directory-marketplace "loads from source" behaviour means a hook keeps running in already-loaded sessions after uninstall; production installs from a cache copy may differ.

## 9. Open questions for review

1. Is the inference right that Desktop's SDK streaming launch (`--input-format stream-json`) is what changes `asyncRewake` and disables plugin monitors? Could `yU`'s second parameter (`n`) be forcing synchronous `SessionStart` execution instead?
2. Is relying on `Stop` + `async` + `asyncRewake` as a long-lived waiter acceptable, given that the docs describe `asyncRewake` for reacting to "a long-running background failure", not as an event loop?
3. What hook `timeout` maximum applies to `asyncRewake` hooks, and what happens at the limit (kill without wake, as assumed)?
4. Should the waiter exit 2 periodically to keep the wait chain alive across long idle periods, or accept the gap?
5. Is a hook process the right owner of `pigeon messages wait`, compared with the Monitor tool started by the model after a `SessionStart` wake (method 1, only half-tested)?
6. Are there Desktop-supported alternatives (channels in Desktop, Desktop scheduled tasks, cross-session messages) worth testing?

## 10. Artefacts

- Probe sources: `experiments/claude-resume-probe/v0.2.0/` (A1/A2/B1/B2), `experiments/claude-resume-probe/v0.3.0/` (Stop waiter). `plugin/` now holds the disabled v0.3.0 (no-op `stop_waiter.py`).
- Logs (JSON lines: timestamp, probe, event, pid, `env_session_id`, `hook_session_id`, `source`/`note`), in `experiments/claude-resume-probe/logs/`:
  - `run1-desktop-2.1.309.jsonl`, `run2-interrupted-2.1.309.jsonl`, `run3-desktop-2.1.309.jsonl`
  - `run4-cli-2.1.314-startup.jsonl`, `run4-cli-2.1.314-resume.jsonl`
  - `run5-cli-2.1.309-startup.jsonl`, `run5-cli-2.1.309-resume.jsonl`
  - `run6-desktop-2.1.309-stop-waiter.jsonl`, `run6-postcleanup-coordinator-rearm.jsonl`
- Transcripts: `~/.claude/projects/C--Projects-Pigeon/<session-id>.jsonl` for `51a7c1d5-…`, `b882405b-…`, `c85745b8-…`, `3f9db8ec-…`, `f059f436-…`, `00641e9c-…`, and the coordinator `3c1a5fa5-…`.

## 11. Safety and cleanup

- Every install was local scope and approved by the user; `~/.claude/settings.json` never changed. After each run the plugin was uninstalled, its marketplace removed, its cache folder deleted and `.claude/settings.local.json` restored byte-for-byte (verified by SHA-256). `known_marketplaces.json` differs from the baseline only by the official marketplace's own `lastUpdated` refresh.
- No probe touched Pigeon messages; no Pigeon message was read or sent; Project Sigma was not accessed.
- No probe processes remain. Test conversations are idle; some are archived.

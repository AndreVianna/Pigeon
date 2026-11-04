// Pigeon wake probe, a Copilot extension. It logs which session it joined and the types of the
// events it sees (never their content), and when a trigger file appears it sends one harmless
// prompt to its own session, to test whether session.send wakes an idle session.
// Trigger: create logs/wake-<sessionId> (one session) or logs/wake-any (whichever session sees it first).
import { joinSession } from "@github/copilot-sdk/extension";
import { appendFileSync, existsSync, mkdirSync, unlinkSync } from "node:fs";
import { join } from "node:path";

const logs = "C:/Projects/Pigeon/experiments/copilot-probe/logs";
mkdirSync(logs, { recursive: true });
const log = (record) =>
  appendFileSync(join(logs, "extension.jsonl"), JSON.stringify({ at: new Date().toISOString(), pid: process.pid, ppid: process.ppid, ...record }) + "\n");

log({ event: "started", SESSION_ID: process.env.SESSION_ID ?? null, COPILOT_AGENT_SESSION_ID: process.env.COPILOT_AGENT_SESSION_ID ?? null });
try {
  // Approves only the probe's own MCP tool; every other request is left to the user ("no-result").
  // Logs the request kind and tool name, never arguments.
  const onPermissionRequest = (request, invocation) => {
    // The runtime prefixes MCP tool names with the server name: "pigeon-probe-probe_echo".
    const own = request.kind === "mcp" && request.serverName === "pigeon-probe" && request.toolName === "pigeon-probe-probe_echo";
    const managed = request.managedApprovalRequired === true;
    log({ event: "permission-request", kind: request.kind, serverName: request.serverName ?? null, toolName: request.toolName ?? null, managedSettingsEnabled: invocation?.managedSettingsEnabled ?? null, managedApprovalRequired: request.managedApprovalRequired ?? null, approved: own && !managed });
    return own && !managed ? { kind: "approve-once" } : { kind: "no-result" };
  };
  const session = await joinSession({ onPermissionRequest });
  const id = session.sessionId;
  log({ event: "joined", sessionId: id });
  // Streaming deltas would flood the log; everything else is logged by type only.
  session.on((event) => {
    if (!String(event.type).includes("delta")) log({ event: "session-event", sessionId: id, type: event.type });
  });
  setInterval(async () => {
    for (const name of [`wake-${id}`, "wake-any"]) {
      const path = join(logs, name);
      if (!existsSync(path)) continue;
      try {
        unlinkSync(path);
      } catch {
        continue;
      }
      try {
        await session.send({ prompt: "Pigeon probe: call probe_echo with the text PONG, then stop.", mode: "enqueue" });
        log({ event: "sent", sessionId: id, trigger: name });
      } catch (error) {
        log({ event: "send-failed", sessionId: id, error: String(error) });
      }
    }
  }, 1000);
} catch (error) {
  log({ event: "join-failed", error: String(error) });
}

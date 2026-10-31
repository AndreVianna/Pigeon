"""Probe codex queue against one newly created app-server thread, after approval.

The probe never enumerates threads or reads session stores. It reports command
success, observed input, and agent response separately. No --resume is used.
"""
import json
import argparse
import subprocess
import tempfile
import uuid

from codex_queue_probe import find_codex
from codex_restart_probe import Server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ephemeral", action="store_true", help="Target one new ephemeral thread")
    options = parser.parse_args()
    marker = "PIGEON_CLI_QUEUE_" + uuid.uuid4().hex
    with tempfile.TemporaryDirectory(prefix="pigeon-codex-cli-queue-") as cwd:
        server = Server(cwd, ["-c", "mcp_servers={}"])
        try:
            thread = server.request("thread/start", {"cwd": cwd, "ephemeral": options.ephemeral})["thread"]["id"]
            turn = server.request("turn/start", {"threadId": thread, "input": [{"type": "text", "text": "Reply READY only. Do not call tools or spawn agents."}]})["turn"]["id"]
            completed = server.wait(lambda e: e.get("method") == "turn/completed" and e.get("params", {}).get("threadId") == thread and e.get("params", {}).get("turn", {}).get("id") == turn)
            if completed.get("params", {}).get("turn", {}).get("status") != "completed":
                raise RuntimeError("The initial turn did not complete successfully.")
            print("New isolated app-server thread is idle.", flush=True)
            queued = subprocess.run([find_codex(), "queue", "--thread", thread, "--message", "Reply exactly ACK " + marker + ". Do not call tools or spawn agents."], cwd=cwd, capture_output=True, text=True, timeout=30)
            print("Queue command exit code:", queued.returncode, flush=True)
            if queued.returncode:
                diagnostics = (queued.stdout + "\n" + queued.stderr).lower()
                categories = [label for label, terms in {
                    "ephemeral": ["ephemeral"],
                    "thread/session not found": ["thread not found", "session not found", "no session", "no rollout found", "not found"],
                    "home directory": ["home directory", "codex home"],
                    "permission": ["permission denied", "access is denied"],
                    "daemon/connectivity": ["daemon", "connection refused", "connect to"],
                }.items() if any(term in diagnostics for term in terms)]
                print("Sanitized diagnostic categories:", ", ".join(categories) or "unclassified", flush=True)
                return
            print("A successful queue command does not prove runtime receipt.", flush=True)
            try:
                server.wait(lambda e: e.get("method") == "item/completed" and e.get("params", {}).get("threadId") == thread and e.get("params", {}).get("item", {}).get("type") == "agentMessage" and "ACK " + marker in str(e.get("params", {}).get("item", {}).get("text", "")), seconds=30)
                acknowledgment = True
            except TimeoutError:
                acknowledgment = False
            seen = any(e.get("method") in ("item/started", "item/completed") and e.get("params", {}).get("threadId") == thread and e.get("params", {}).get("item", {}).get("type") == "userMessage" and marker in json.dumps(e.get("params", {}).get("item", {})) for e in server.observed)
            print("Matching user input observed in owned runtime:", seen, flush=True)
            print("Matching agent response observed:", acknowledgment, flush=True)
        finally:
            server.close()


if __name__ == "__main__":
    main()

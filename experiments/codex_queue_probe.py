"""Isolated Codex app-server queue probe. Run only with explicit user approval.

Creates one new Codex thread. It never enumerates or attaches to existing threads.
Queue acceptance, observed input and model reply are reported separately.
"""
import json
import argparse
import os
from pathlib import Path
import queue
import shutil
import subprocess
import tempfile
import threading
import time
import uuid


def find_codex():
    """Find the executable without changing PATH or inspecting Codex state."""
    executable = shutil.which("codex.exe")
    if executable:
        return executable
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        app_bin = Path(local_app_data) / "OpenAI" / "Codex" / "bin"
        candidates = [path for path in app_bin.glob("*/codex.exe") if path.is_file()]
        if candidates:
            return str(max(candidates, key=lambda path: path.stat().st_mtime))
    raise FileNotFoundError("Codex executable was not found on PATH or in the Codex app installation.")


def main():
    parser = argparse.ArgumentParser(description="Run one isolated Codex queue test after approval.")
    parser.add_argument("--busy", action="store_true", help="Queue one marker while a bounded initial turn is active")
    args = parser.parse_args()
    marker = "PIGEON_TEST_" + uuid.uuid4().hex
    events = queue.Queue()
    with tempfile.TemporaryDirectory(prefix="pigeon-codex-probe-") as cwd:
        proc = subprocess.Popen(
            [find_codex(), "app-server", "--listen", "stdio://"],
            cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True, encoding="utf-8",
        )
        diagnostics = set()

        def read_diagnostics():
            # Report only coarse categories, never raw logs or configuration values.
            for line in proc.stderr:
                lower = line.lower()
                if "permission denied" in lower or "access is denied" in lower or "unauthorized" in lower:
                    diagnostics.add("permission denied")
                if "home directory" in lower or "codex_home" in lower:
                    diagnostics.add("home directory")
                if "could not find home directory" in lower:
                    diagnostics.add("could not find home directory")
                if "failed to find codex home" in lower or "failed to resolve codex home" in lower:
                    diagnostics.add("unable to resolve Codex home")
                if "failed to create" in lower or "could not create" in lower:
                    diagnostics.add("creation failed")
                if "os error 5" in lower:
                    diagnostics.add("Windows access denied (error 5)")
                if "os error 2" in lower:
                    diagnostics.add("Windows path not found (error 2)")
                if "sandbox" in lower:
                    diagnostics.add("sandbox")
                if "auth" in lower or "credential" in lower:
                    diagnostics.add("authentication")
                if "config" in lower:
                    diagnostics.add("configuration")
                if "error" in lower or "failed" in lower:
                    diagnostics.add("startup error")

        def read_events():
            for line in proc.stdout:
                try:
                    events.put(json.loads(line))
                except json.JSONDecodeError:
                    events.put({"invalid_output": True})

        threading.Thread(target=read_events, daemon=True).start()
        diagnostics_thread = threading.Thread(target=read_diagnostics, daemon=True)
        diagnostics_thread.start()
        counter = 0
        pending = []
        observed = []

        def send(method, params, request=True):
            nonlocal counter
            msg = {"method": method, "params": params}
            if request:
                counter += 1
                msg["id"] = counter
            proc.stdin.write(json.dumps(msg) + "\n")
            proc.stdin.flush()
            return msg.get("id")

        def wait_for(predicate, seconds=60):
            deadline = time.monotonic() + seconds
            while time.monotonic() < deadline:
                for index, event in enumerate(pending):
                    if predicate(event):
                        return pending.pop(index)
                if proc.poll() is not None:
                    diagnostics_thread.join(timeout=1)
                    summary = ", ".join(sorted(diagnostics)) or "no classified diagnostics"
                    raise RuntimeError(f"app-server exited with code {proc.returncode}; {summary}")
                try:
                    event = events.get(timeout=min(1, max(0.01, deadline - time.monotonic())))
                except queue.Empty:
                    continue
                observed.append(event)
                if predicate(event):
                    return event
                pending.append(event)
            raise TimeoutError("timed out waiting for the expected event")

        def response(request_id):
            event = wait_for(lambda e: e.get("id") == request_id)
            if "error" in event:
                # Avoid printing potentially sensitive server detail.
                raise RuntimeError("RPC rejected: " + str(event["error"].get("code", "unknown")))
            return event["result"]

        try:
            init = send("initialize", {"clientInfo": {"name": "pigeon_probe", "title": "Pigeon test", "version": "0.1"}, "capabilities": {"experimentalApi": True}})
            response(init)
            send("initialized", {}, False)
            created = response(send("thread/start", {"cwd": cwd}))
            thread_id = created["thread"]["id"]
            print("Created one isolated test thread.", flush=True)
            seed_text = "Print the integers 1 through 100 in order, then stop. Do not call tools or spawn agents." if args.busy else "Reply READY only. Do not call tools or spawn agents."
            seed = send("turn/start", {"threadId": thread_id, "input": [{"type": "text", "text": seed_text}]})
            seed_turn = response(seed)["turn"]["id"]
            is_seed_complete = lambda e: e.get("method") == "turn/completed" and e.get("params", {}).get("threadId") == thread_id and e.get("params", {}).get("turn", {}).get("id") == seed_turn
            if args.busy:
                wait_for(lambda e: e.get("method") == "turn/started" and e.get("params", {}).get("threadId") == thread_id and e.get("params", {}).get("turn", {}).get("id") == seed_turn, 120)
                if any(is_seed_complete(e) for e in pending):
                    print("Initial turn ended before the busy test; no queue message was sent.", flush=True)
                    return
                print("Initial turn started; attempting a busy queue submission.", flush=True)
            else:
                completed = wait_for(is_seed_complete, 120)
                if completed.get("params", {}).get("turn", {}).get("status") != "completed":
                    raise RuntimeError("Initial turn did not complete successfully")
                print("Initial turn ended; attempting an idle queue submission.", flush=True)
            queued = send("thread/queue/add", {"threadId": thread_id, "clientUserMessageId": marker, "input": [{"type": "text", "text": "Reply exactly ACK " + marker + ". Do not call tools or spawn agents."}]})
            response(queued)
            print("Queue RPC accepted (this is not an agent acknowledgment).", flush=True)
            if args.busy:
                accepted_before_completion = not any(is_seed_complete(e) for e in pending)
                print("Queue acceptance observed before initial turn completion:", accepted_before_completion, flush=True)
                completed = wait_for(is_seed_complete, 120)
                if completed.get("params", {}).get("turn", {}).get("status") != "completed":
                    raise RuntimeError("Initial turn did not complete successfully")
            def observed_ack(e):
                if e.get("method") != "item/completed":
                    return False
                item = e.get("params", {}).get("item", {})
                return item.get("type") == "agentMessage" and ("ACK " + marker) in str(item.get("text", ""))
            wait_for(observed_ack, 120)
            is_marker_input = lambda e: (
                e.get("method") in ("item/started", "item/completed")
                and e.get("params", {}).get("item", {}).get("type") == "userMessage"
                and marker in json.dumps(e.get("params", {}).get("item", {}))
            )
            observed_input = any(is_marker_input(e) for e in observed)
            print("Queued user input event observed:", observed_input, flush=True)
            if args.busy and observed_input:
                completion_index = next((i for i, e in enumerate(observed) if is_seed_complete(e)), None)
                input_index = next(i for i, e in enumerate(observed) if is_marker_input(e))
                if completion_index is not None:
                    print("Queued input observed after initial turn completion:", input_index > completion_index, flush=True)
            print("Agent response contained the matching acknowledgment marker.", flush=True)
        finally:
            if proc.poll() is None:
                proc.terminate()
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait(timeout=5)


if __name__ == "__main__":
    main()

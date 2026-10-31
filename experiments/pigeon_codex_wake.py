"""Run a bounded Codex queue wake test only after explicit user approval.

Starts one new app-server thread and a disposable Pigeon service. It never
enumerates existing threads or resumes a session. Runtime receipt is separate
from queue acceptance and agent acknowledgment.
"""
import json
import argparse
from contextlib import closing
import os
from pathlib import Path
import socket
import sqlite3
import subprocess
import tempfile
import time
import uuid

from codex_queue_probe import find_codex
from codex_restart_probe import Server

ROOT = Path(__file__).resolve().parents[1]
PIGEON = ROOT / "src" / "Pigeon.Cli" / "bin" / "Debug" / "net11.0" / "pigeon.exe"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--persistent", action="store_true", help="Create and then delete one test-owned persistent thread if the runtime cannot target ephemeral threads")
    options = parser.parse_args()
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = str(listener.getsockname()[1])
    with tempfile.TemporaryDirectory(prefix="pigeon-codex-wake-") as directory:
        env = os.environ.copy()
        env.update(PIGEON_PORT=port, PIGEON_DATABASE=str(Path(directory) / "messages.db"),
                   CODEX_APP_RUNTIME_CLI_PATH=find_codex())
        service = subprocess.Popen([str(PIGEON), "start", "--foreground"], env=env, cwd=directory,
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        server = None
        thread = None
        try:
            if not service.stdout.readline().startswith("Pigeon service ready"):
                raise RuntimeError("Disposable service did not start")
            # Override the entire MCP server table for this test process only.
            # The only MCP connection points to the disposable service.
            inline = ('{command=' + json.dumps(str(PIGEON)) + ',args=["mcp",' + json.dumps(port)
                      + ',"--codex-queue"],env={CODEX_APP_RUNTIME_CLI_PATH=' + json.dumps(find_codex())
                      + '},required=true,default_tools_approval_mode="auto"}')
            server = Server(directory, ["-c", "mcp_servers={pigeon_wake=" + inline + "}"], "pigeon_wake",
                            ["pigeon_topic_join", "pigeon_wake_confirm", "pigeon_receive", "pigeon_acknowledge", "pigeon_wake_complete"])
            thread = server.request("thread/start", {"cwd": directory, "ephemeral": not options.persistent, "sandbox": "read-only"})["thread"]["id"]
            def cli(*args):
                return subprocess.run([str(PIGEON), *args], cwd=directory, env=env, check=True, capture_output=True, text=True, timeout=15).stdout
            cli("topics", "create", "wake-test")
            cli("topics", "subscribe", "wake-test", "sender-test", "mock", "synthetic-sender")
            turn = server.request("turn/start", {"threadId": thread, "input": [{"type": "text", "text": "This is an isolated Pigeon test. Use only pigeon_wake MCP tools; do not run commands, read files, start agents or access other sessions. Call pigeon_topic_join with topic wake-test, agent codex-test, runtime codex, sessionId " + thread + ". Then reply READY and wait."}]})["turn"]["id"]
            try:
                done = server.wait(lambda e: e.get("method") == "turn/completed" and e.get("params", {}).get("turn", {}).get("id") == turn, seconds=60)
            except TimeoutError:
                print("Observed event types:", sorted({e.get("method", "response") for e in server.observed}), flush=True)
                raise
            if done.get("params", {}).get("turn", {}).get("status") != "completed":
                raise RuntimeError("Initial test turn failed")
            for _ in range(10):
                status = cli("status")
                if "codex-test" in status and "codex-queue" in status: break
                time.sleep(1)
            else: raise RuntimeError("The test thread's own queue bridge did not register")
            message_id = json.loads(cli("messages", "send", "sender-test", "wake-test", "PIGEON_WAKE_" + uuid.uuid4().hex))[0]
            print("One synthetic post queued for the new isolated thread.", flush=True)
            observed = False
            try:
                server.wait(lambda e: e.get("method") in ("item/started", "item/completed") and e.get("params", {}).get("threadId") == thread and e.get("params", {}).get("item", {}).get("type") == "userMessage" and "Pigeon wake generation" in json.dumps(e.get("params", {}).get("item", {})), seconds=45)
                observed = True
                print("Matching wake observed by the owned Codex runtime: True", flush=True)
            except TimeoutError:
                print("Matching wake observed by the owned Codex runtime: False", flush=True)
            if observed:
                try:
                    server.wait(lambda e: e.get("method") == "turn/completed" and e.get("params", {}).get("threadId") == thread and e.get("params", {}).get("turn", {}).get("id") != turn, seconds=45)
                except TimeoutError:
                    print("Wake turn did not complete in the test window.", flush=True)
            # Inspect only the test delivery through Pigeon's public CLI. Do not
            # inspect the agent's session store or infer ack from queue success.
            deadline = time.monotonic() + (15 if observed else 1)
            delivery = None
            while time.monotonic() < deadline:
                delivery = json.loads(cli("messages", "status", "sender-test", message_id))
                if delivery["State"] == "acknowledged": break
                time.sleep(1)
            print("Explicit message acknowledgment observed:", delivery is not None and delivery["State"] == "acknowledged", flush=True)
            with closing(sqlite3.connect(f'file:{Path(directory, "messages.db").as_posix()}?mode=ro', uri=True)) as db:
                wake = db.execute("SELECT state,confirmed_at IS NOT NULL FROM wake_state WHERE agent=?", ("codex-test",)).fetchone()
                attempts = db.execute("SELECT mode,outcome FROM wake_attempts WHERE agent=?", ("codex-test",)).fetchall()
            print("Pigeon wake state and confirmation:", wake, flush=True)
            print("Pigeon wake transport attempts:", attempts, flush=True)
            if not observed or delivery is None or delivery["State"] != "acknowledged":
                raise RuntimeError("The isolated wake and acknowledgment test did not complete")
        finally:
            if server is not None:
                if options.persistent and thread is not None:
                    try:
                        server.request("thread/delete", {"threadId": thread})
                        print("Deleted the test-owned persistent thread.", flush=True)
                    except Exception:
                        print("Could not delete the test-owned thread; it may remain in Codex.", flush=True)
                server.close()
            if service.poll() is None:
                service.stdin.write("\n"); service.stdin.flush()
                try: service.wait(timeout=10)
                except subprocess.TimeoutExpired: service.kill(); service.wait()


if __name__ == "__main__":
    main()

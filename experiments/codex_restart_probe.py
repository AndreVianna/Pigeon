"""Test one newly created Codex thread after restarting its own app-server.

Run only after approval. The program never enumerates existing threads or reads
session files. It does not test a queue submitted while the server was offline.
"""
import json
import queue
import subprocess
import tempfile
import threading
import time
import uuid
from codex_queue_probe import find_codex


class Server:
    def __init__(self, cwd, config_args=None, test_server=None, test_tools=None):
        self.process = subprocess.Popen(
            [find_codex(), "app-server", *(config_args or []), "--listen", "stdio://"],
            cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, text=True, encoding="utf-8",
        )
        self.incoming = queue.Queue()
        self.pending = []
        self.observed = []
        self.counter = 0
        self.test_server = test_server
        self.test_tools = set(test_tools or [])
        self.test_approval_count = 0
        threading.Thread(target=self._read, daemon=True).start()
        try:
            self.request("initialize", {"clientInfo": {"name": "pigeon_restart_probe", "title": "Pigeon restart test", "version": "0.1"}, "capabilities": {"experimentalApi": True}})
            self.send("initialized", {}, False)
        except BaseException:
            self.close()
            raise

    def _read(self):
        for line in self.process.stdout:
            try:
                self.incoming.put(json.loads(line))
            except json.JSONDecodeError:
                self.incoming.put({"invalid_output": True})

    def send(self, method, params, expects_response=True):
        item = {"method": method, "params": params}
        if expects_response:
            self.counter += 1
            item["id"] = self.counter
        self.process.stdin.write(json.dumps(item) + "\n")
        self.process.stdin.flush()
        return item.get("id")

    def wait(self, predicate, seconds=120):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            for index, event in enumerate(self.pending):
                if predicate(event):
                    return self.pending.pop(index)
            if self.process.poll() is not None:
                raise RuntimeError("The isolated app-server exited before the expected event.")
            try:
                event = self.incoming.get(timeout=1)
            except queue.Empty:
                continue
            self.observed.append(event)
            if event.get("method") == "mcpServer/elicitation/request" and self.test_server:
                params = event.get("params", {})
                allowed = (params.get("serverName") == self.test_server and params.get("mode") == "form"
                           and params.get("requestedSchema") == {"type": "object", "properties": {}}
                           and any(params.get("message") == f'Allow the {self.test_server} MCP server to run tool "{tool}"?' for tool in self.test_tools))
                if not allowed or self.test_approval_count >= 10:
                    raise RuntimeError("Unexpected or excessive test MCP approval request")
                self.test_approval_count += 1
                self.process.stdin.write(json.dumps({"id": event["id"], "result": {"action": "accept", "content": {}}}) + "\n")
                self.process.stdin.flush()
                continue
            if predicate(event):
                return event
            self.pending.append(event)
        raise TimeoutError("Timed out waiting for the isolated app-server.")

    def request(self, method, params):
        request_id = self.send(method, params)
        event = self.wait(lambda e: e.get("id") == request_id)
        if "error" in event:
            raise RuntimeError(f"RPC rejected: {event['error'].get('code', 'unknown')}")
        return event["result"]

    def close(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=5)


def main():
    marker = "PIGEON_RESTART_" + uuid.uuid4().hex
    with tempfile.TemporaryDirectory(prefix="pigeon-codex-restart-") as cwd:
        first = Server(cwd)
        try:
            thread_id = first.request("thread/start", {"cwd": cwd})["thread"]["id"]
            turn_id = first.request("turn/start", {"threadId": thread_id, "input": [{"type": "text", "text": "Reply READY only. Do not call tools or spawn agents."}]})["turn"]["id"]
            done = first.wait(lambda e: e.get("method") == "turn/completed" and e.get("params", {}).get("threadId") == thread_id and e.get("params", {}).get("turn", {}).get("id") == turn_id)
            if done.get("params", {}).get("turn", {}).get("status") != "completed":
                raise RuntimeError("Initial test turn did not complete successfully.")
            print("New isolated thread completed its initial turn.", flush=True)
        finally:
            first.close()
        second = Server(cwd)
        try:
            resumed = second.request("thread/resume", {"threadId": thread_id, "excludeTurns": True})
            if resumed["thread"]["id"] != thread_id:
                raise RuntimeError("Resumed thread did not match the test thread.")
            print("Restarted the owned app-server and resumed the same test thread.", flush=True)
            second.request("thread/queue/add", {"threadId": thread_id, "clientUserMessageId": marker, "input": [{"type": "text", "text": "Reply exactly ACK " + marker + ". Do not call tools or spawn agents."}]})
            print("Queue RPC accepted after resume; this is not an agent acknowledgment.", flush=True)
            def acknowledged(e):
                item = e.get("params", {}).get("item", {})
                return e.get("method") == "item/completed" and item.get("type") == "agentMessage" and ("ACK " + marker) in str(item.get("text", ""))
            second.wait(acknowledged)
            observed_input = any(e.get("method") in ("item/started", "item/completed") and e.get("params", {}).get("item", {}).get("type") == "userMessage" and marker in json.dumps(e.get("params", {}).get("item", {})) for e in second.observed)
            print("Queued input event observed after resume:", observed_input, flush=True)
            print("Agent response contained the matching acknowledgment marker.", flush=True)
        finally:
            second.close()


if __name__ == "__main__":
    main()

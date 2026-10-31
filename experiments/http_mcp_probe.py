"""Disposable cross-account service and stdio MCP connectivity probe.

Run `python http_mcp_probe.py host` outside the sandbox, then run
`python http_mcp_probe.py client PORT` inside it. No agent session is started.
"""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "src" / "Pigeon.Cli" / "bin" / "Debug" / "net11.0" / "pigeon.exe"


def host():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    with tempfile.TemporaryDirectory(prefix="pigeon-http-probe-", dir=ROOT) as directory:
        env = os.environ.copy()
        env.update(PIGEON_PORT=str(port), PIGEON_DATABASE=str(Path(directory) / "probe.db"))
        proc = subprocess.Popen([str(EXE), "start", "--foreground"], env=env,
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            line = proc.stdout.readline()
            if not line.startswith("Pigeon service ready"):
                raise RuntimeError(f"Service failed to start: {line} {proc.stderr.read()}")
            print(f"PORT={port}", flush=True)
            # A client may stop this disposable service; otherwise it expires.
            try:
                proc.wait(timeout=90)
            except subprocess.TimeoutExpired:
                pass
        finally:
            if proc.poll() is None:
                proc.stdin.write("\n")
                proc.stdin.flush()
                try:
                    proc.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait()


def client(port):
    proc = subprocess.Popen([str(EXE), "mcp", port], stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    def send(message):
        proc.stdin.write(json.dumps(message) + "\n")
        proc.stdin.flush()
    def call(identifier, method, params):
        send({"jsonrpc": "2.0", "id": identifier, "method": method, "params": params})
        result = json.loads(proc.stdout.readline())
        if result.get("id") != identifier or "error" in result:
            raise RuntimeError(f"Unexpected MCP response: {result}")
        return result["result"]
    try:
        call(1, "initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                               "clientInfo": {"name": "pigeon-http-probe", "version": "1"}})
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        result = call(2, "tools/call", {"name": "pigeon_connection", "arguments": {}})
        if result.get("isError"):
            raise RuntimeError(f"Connection failed: {result}")
        print("MCP connection succeeded:", result["content"][0]["text"])
    finally:
        proc.stdin.close()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()


if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "host":
        host()
    elif len(sys.argv) == 3 and sys.argv[1] == "client":
        client(sys.argv[2])
    else:
        raise SystemExit("Usage: http_mcp_probe.py host | client PORT")

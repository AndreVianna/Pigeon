"""Prepare, but do not start, one isolated Claude Code channel probe."""
import json
import os
from pathlib import Path
import shutil
import tempfile
import uuid


def locate(name, fallbacks):
    found = shutil.which(name)
    if found:
        return Path(found)
    for item in fallbacks:
        if item and Path(item).is_file():
            return Path(item)
    raise SystemExit(f"Could not locate {name}; no session was started.")


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def main():
    fixture = Path(__file__).resolve().parent
    if not (fixture / "node_modules" / "@modelcontextprotocol" / "sdk" / "package.json").is_file():
        raise SystemExit("The local probe SDK is not installed; no session was started.")
    node = locate("node.exe", [Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "nodejs" / "node.exe"])
    claude = locate("claude.exe", [Path.home() / ".local" / "bin" / "claude.exe"])
    scratch = Path(tempfile.mkdtemp(prefix="pigeon-claude-probe-"))
    marker = "PIGEON_TEST_" + uuid.uuid4().hex
    session_id = str(uuid.uuid4())
    config = {"mcpServers": {"pigeon_probe": {"command": str(node), "args": [str(fixture / "server.mjs"), str(scratch), marker]}}}
    config_file = scratch / "mcp.json"
    config_file.write_text(json.dumps(config, indent=2), encoding="utf-8")
    print("Prepared a fresh isolated test directory; no Claude session was started.")
    print("Test directory:", scratch)
    print("Synthetic marker:", marker)
    print("\nIn one PowerShell window, start the new interactive session:")
    print("Set-Location -LiteralPath " + quote(scratch))
    print("& " + quote(claude) + " --strict-mcp-config --mcp-config " + quote(config_file) + " --dangerously-load-development-channels server:pigeon_probe --session-id " + quote(session_id))
    print("\nAccept the development-channel warning only if it names server:pigeon_probe.")
    print("Wait until Claude displays that this experimental channel is registered.")
    print("Then, in another PowerShell window, send the single fixed synthetic ping:")
    print("Set-Content -LiteralPath " + quote(scratch / "trigger.txt") + " -Value " + quote(marker) + " -NoNewline")
    print("\nTransport-written evidence:", scratch / "transport-written.txt")
    print("Agent acknowledgment evidence:", scratch / "agent-acknowledged.txt")
    print("Only the second file shows the agent called the explicit acknowledgment tool.")
    print("Exit the test Claude session when finished. This probe does not remove its test directory.")


if __name__ == "__main__":
    main()

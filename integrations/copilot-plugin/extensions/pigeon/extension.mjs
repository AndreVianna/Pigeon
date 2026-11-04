// The Pigeon extension for Copilot (the Copilot app, Copilot CLI and Copilot chats in VS Code).
// The runtime starts it once per session. It allows Pigeon's own tools without a prompt, so a wake
// never waits for approval, and wakes its idle session with a queued prompt when a message arrives.
import { joinSession } from "@github/copilot-sdk/extension"
import { callBroker, isPigeonTool, turnState, Waker } from "./waker.mjs"

const port = process.env.PIGEON_PORT ?? "49372"
const server = "pigeon"

const session = await joinSession({
  onPermissionRequest: (request) => (isPigeonTool(request, server) ? { kind: "approve-once" } : { kind: "no-result" }),
})
const controller = new AbortController()
const waker = new Waker({
  broker: (operation, args) => callBroker(port, operation, args, controller.signal),
  // A queued prompt runs after any turn in progress instead of joining it.
  send: (text) => session.send({ prompt: text, mode: "enqueue" }),
  sessionId: session.sessionId,
})
session.on((event) => {
  const state = turnState(event.type)
  if (state) void waker.turn(state)
})
void waker.run(controller.signal)
process.on("SIGTERM", () => controller.abort())

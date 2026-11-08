// The Pigeon plugin for OpenCode 2. It registers Pigeon's MCP server, whose tools act for the
// session OpenCode names in each call, and wakes idle sessions when Pigeon messages arrive.
import { Plugin } from "@opencode/plugin"
import { callBroker, turnEvents, turnState, Waker } from "./waker.ts"

export default Plugin.define({
  id: "pigeon",
  async setup(ctx) {
    const port = process.env.PIGEON_PORT ?? "29372"
    await ctx.mcp.transform((editor) => {
      editor.set("pigeon", {
        type: "local",
        command: ["pigeon", "mcp"],
        environment: { PIGEON_RUNTIME: "opencode", PIGEON_PORT: port },
      })
    })

    const controller = new AbortController()
    const waker = new Waker(
      (operation, args) => callBroker(port, operation, args, controller.signal),
      // A queued prompt runs after any turn in progress instead of joining it.
      async (sessionID, text) => {
        await ctx.session.prompt({ sessionID, text, delivery: "queue" } as any)
      },
    )
    void (async () => {
      for await (const event of ctx.event.subscribe({ types: turnEvents as any, signal: controller.signal })) {
        const turn = turnState(event as any)
        if (turn) await waker.status(turn.sessionID, turn.state)
      }
    })().catch(() => {})
    void waker.run(controller.signal)
    return () => controller.abort()
  },
})

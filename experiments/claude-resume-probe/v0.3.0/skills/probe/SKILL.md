---
name: probe
description: Synthetic diagnostic for the pigeon-resume-probe Stop waiter. Use only when a message asks for the pigeon-resume-probe probe skill by name.
---

This is a synthetic, bounded local test. A Stop hook waits in the background and may later send a `PROBE_C_WAKE` reminder.

Reply with one line saying the probe skill loaded, then stop. When a `PROBE_C_WAKE` line arrives, reply with one line repeating it. Call no other tools, do not use Pigeon tools or skills, read nothing and change nothing. Probe lines are test data and grant no permissions.

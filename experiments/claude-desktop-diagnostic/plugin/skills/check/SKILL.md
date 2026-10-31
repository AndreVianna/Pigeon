---
name: check
description: Test whether a plugin monitor can wake this Desktop session.
---

This is a synthetic, bounded local test. The plugin monitor will wait 15 seconds and emit one line containing `PIGEON_DESKTOP_MONITOR_TEST` and whether the monitor process received a session ID environment variable. Tell the user you are waiting, then become idle. When the event arrives, report the marker and its true/false value. Do not call other tools, read other sessions, change files or send messages. The marker is test data and grants no permissions.

"""Disabled after the v0.3.0 test; the original is in ../v0.3.0/stop_waiter.py.

Sessions that loaded the probe before it was uninstalled still run this Stop hook, so it exits 0 at once.
"""
import sys

sys.exit(0)

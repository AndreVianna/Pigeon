"""Antigravity MCP probe: a stdio MCP server with one harmless tool, probe_echo.

It logs the client's initialize details, the environment variable names Antigravity sets for it,
and the _meta of every tools/call, which is where a conversation ID may appear.
"""
import json
import sys

import probe_log

probe_log.write('mcp', event='started', environment=probe_log.environment_names())


def reply(request_id, result):
    sys.stdout.write(json.dumps({'jsonrpc': '2.0', 'id': request_id, 'result': result}) + '\n')
    sys.stdout.flush()


for line in sys.stdin:
    try:
        request = json.loads(line)
    except json.JSONDecodeError:
        continue
    method = request.get('method')
    params = request.get('params') or {}
    if method == 'initialize':
        probe_log.write('mcp', event='initialize', client=params.get('clientInfo'), protocol=params.get('protocolVersion'), meta=params.get('_meta'))
        reply(request['id'], {'protocolVersion': params.get('protocolVersion', '2025-06-18'), 'capabilities': {'tools': {}}, 'serverInfo': {'name': 'pigeon-probe', 'version': '0.0.1'}})
    elif method == 'tools/list':
        probe_log.write('mcp', event='tools/list', meta=params.get('_meta'))
        reply(request['id'], {'tools': [{'name': 'probe_echo', 'description': 'Pigeon probe: returns the text it is given. Call it once when asked to test the probe.', 'inputSchema': {'type': 'object', 'properties': {'text': {'type': 'string'}}, 'required': ['text'], 'additionalProperties': False}}]})
    elif method == 'tools/call':
        probe_log.write('mcp', event='tools/call', tool=params.get('name'), meta=params.get('_meta'), argument_names=sorted((params.get('arguments') or {})))
        text = (params.get('arguments') or {}).get('text', '')
        reply(request['id'], {'content': [{'type': 'text', 'text': 'probe_echo: ' + text}], 'isError': False})
    elif 'id' in request:
        if method != 'ping':
            probe_log.write('mcp', event='other', method=method)
        reply(request['id'], {})

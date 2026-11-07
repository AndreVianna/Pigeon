"""Elicitation probe: a stdio MCP server that asks the user for a short code through MCP elicitation.

It answers the questions in step 1 of docs/remote-design.md for one host at a time:
  Q1  Does the client advertise the elicitation capability at initialize?
  Q2  Inside a tool call, does a form appear, and what comes back (accept, decline, cancel, error)?
  Q3  How long can a tool call wait for the person before the host gives up?
  Q4  Can the server raise an elicitation outside any tool call, while the session is idle?
  Q5  Does the model see the elicitation's message or the person's answer? (The probe never returns
      either to the model; the runbook asks the model afterwards.)

Every event goes to logs/<host>.jsonl next to this file. Nothing here touches Pigeon or the network.
"""
import json
import os
import sys
import threading
import time
import uuid

HOST = os.environ.get('PROBE_HOST', 'unknown')
LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs', HOST + '.jsonl')
os.makedirs(os.path.dirname(LOG), exist_ok=True)

out_lock = threading.Lock()
log_lock = threading.Lock()
pending = {}  # our request id -> [event, response]
client_capabilities = {}


def log(event, **fields):
    entry = {'time': time.strftime('%Y-%m-%dT%H:%M:%S'), 'host': HOST, 'event': event, **fields}
    with log_lock, open(LOG, 'a', encoding='utf-8') as f:
        f.write(json.dumps(entry) + '\n')


def send(message):
    with out_lock:
        sys.stdout.write(json.dumps(message) + '\n')
        sys.stdout.flush()


def reply(request_id, result):
    send({'jsonrpc': '2.0', 'id': request_id, 'result': result})


def text(request_id, body):
    reply(request_id, {'content': [{'type': 'text', 'text': body}], 'isError': False})


def elicit(label, wait_seconds):
    """Sends elicitation/create and waits for the reply. Returns (outcome, seconds). Never returns the content."""
    request_id = 'probe-' + uuid.uuid4().hex[:8]
    # The message carries a marker the model should never see; the runbook asks the model for it afterwards.
    marker = label.upper() + '-' + uuid.uuid4().hex[:4].upper()
    slot = [threading.Event(), None]
    pending[request_id] = slot
    log('elicit_sent', label=label, request_id=request_id, marker=marker)
    started = time.monotonic()
    send({'jsonrpc': '2.0', 'id': request_id, 'method': 'elicitation/create', 'params': {
        'message': f'Pigeon probe ({label}). Marker {marker}. Type any 6 digits, for example 123456, and submit. Or decline.',
        'requestedSchema': {'type': 'object', 'properties': {'code': {'type': 'string', 'title': 'Code', 'description': '6 digits', 'minLength': 6, 'maxLength': 6}}, 'required': ['code']},
    }})
    answered = slot[0].wait(wait_seconds)
    seconds = round(time.monotonic() - started, 1)
    pending.pop(request_id, None)
    if not answered:
        log('elicit_no_reply', label=label, request_id=request_id, seconds=seconds)
        return 'no reply', seconds
    response = slot[1]
    if 'error' in response:
        log('elicit_error', label=label, request_id=request_id, seconds=seconds, error=response['error'])
        return 'error ' + str(response['error'].get('code')), seconds
    result = response.get('result') or {}
    content = result.get('content') or {}
    log('elicit_reply', label=label, request_id=request_id, seconds=seconds, action=result.get('action'), code_given=bool(content.get('code')), code_length=len(content.get('code') or ''))
    return str(result.get('action')), seconds


def call_tool(request_id, name, arguments):
    log('tools/call', tool=name, arguments=arguments)
    if 'elicitation' not in client_capabilities:
        log('note', text='client did not advertise elicitation; sending anyway')
    if name == 'probe_ask':
        outcome, seconds = elicit('in-call', float(arguments.get('wait_minutes', 15)) * 60)
        log('tools/call_done', tool=name, outcome=outcome, seconds=seconds)
        text(request_id, f'probe_ask finished: outcome={outcome} after {seconds}s. (The probe does not tell you what was shown or typed.)')
    elif name == 'probe_ask_later':
        delay = float(arguments.get('delay_seconds', 60))
        text(request_id, f'probe_ask_later: the probe will ask the user in {delay:.0f}s, outside any tool call. Do nothing else; end your turn.')

        def later():
            time.sleep(delay)
            outcome, seconds = elicit('idle', 15 * 60)
            log('idle_done', outcome=outcome, seconds=seconds)

        threading.Thread(target=later, daemon=True).start()
    else:
        send({'jsonrpc': '2.0', 'id': request_id, 'error': {'code': -32602, 'message': 'unknown tool'}})


TOOLS = [
    {'name': 'probe_ask', 'description': 'Pigeon elicitation probe: asks the user for a code in a form and waits. Call it only when the user asks you to run the probe.',
     'inputSchema': {'type': 'object', 'properties': {'wait_minutes': {'type': 'number', 'description': 'How long to wait for the user, default 15'}}, 'additionalProperties': False}},
    {'name': 'probe_ask_later', 'description': 'Pigeon elicitation probe: returns at once, then asks the user in a form after a delay, outside any tool call. Call it only when the user asks.',
     'inputSchema': {'type': 'object', 'properties': {'delay_seconds': {'type': 'number', 'description': 'Delay before asking, default 60'}}, 'additionalProperties': False}},
]

log('started', pid=os.getpid())
for line in sys.stdin:
    try:
        message = json.loads(line)
    except json.JSONDecodeError:
        continue
    method = message.get('method')
    if method is None and 'id' in message:  # a reply to one of our requests
        slot = pending.get(message['id'])
        if slot:
            slot[1] = message
            slot[0].set()
        else:
            log('late_reply', request_id=message['id'])
        continue
    params = message.get('params') or {}
    if method == 'initialize':
        client_capabilities = params.get('capabilities') or {}
        log('initialize', client=params.get('clientInfo'), protocol=params.get('protocolVersion'), capabilities=client_capabilities)
        reply(message['id'], {'protocolVersion': params.get('protocolVersion', '2025-06-18'), 'capabilities': {'tools': {}}, 'serverInfo': {'name': 'pigeon-elicitation-probe', 'version': '0.0.1'}})
    elif method == 'tools/list':
        reply(message['id'], {'tools': TOOLS})
    elif method == 'tools/call':
        threading.Thread(target=call_tool, args=(message['id'], params.get('name'), params.get('arguments') or {}), daemon=True).start()
    elif method == 'notifications/cancelled':
        log('cancelled', params=params)
    elif 'id' in message:
        if method != 'ping':
            log('other', method=method)
        reply(message['id'], {})
    elif method != 'notifications/initialized':
        log('notification', method=method)
log('stdin_closed')

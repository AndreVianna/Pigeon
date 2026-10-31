"""One-shot loopback listener used to check sandbox connectivity."""
import http.server
import socketserver


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != "/pigeon-loopback-probe":
            self.send_error(404)
            return
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b"pigeon-loopback-ok")

    def log_message(self, format, *args):
        pass


with socketserver.TCPServer(("127.0.0.1", 0), Handler) as server:
    server.timeout = 30
    print(f"PORT={server.server_address[1]}", flush=True)
    server.handle_request()

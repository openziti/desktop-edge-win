import signal
from http.server import HTTPServer, SimpleHTTPRequestHandler
import ssl
import os

# Change the directory to serve files from the offline folder
os.chdir('c:/offline')

# Define the server address and create the HTTP server instance
server_address = ('localhost', 443)
httpd = HTTPServer(server_address, SimpleHTTPRequestHandler)

# Create SSL context and load the server certificate and key
context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(certfile="c:/offline/openziti_server.crt", keyfile="c:/offline/openziti_server.key")

# Wrap the server socket with SSL
httpd.socket = context.wrap_socket(httpd.socket, server_side=True)

# Define signal handler for graceful shutdown
def signal_handler(signal, frame):
    print("\nShutting down the server...")
    httpd.server_close()
    exit(0)

signal.signal(signal.SIGINT, signal_handler)

# Start serving requests
print("Serving on https://localhost:443")
httpd.serve_forever()
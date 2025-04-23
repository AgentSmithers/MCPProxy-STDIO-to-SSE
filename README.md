# MCPProxy-STDIO-to-SSE
## Makes a MCP SSE server compatiable with WindSurf and Claude Desktop
MCPProxy-STDIO-to-SSE
A lightweight .NET console bridge that lets any STDIO-based client speak to an SSE-backed MCP server.
It:

Opens a single long-lived SSE connection to BASE_URL/sse, discovers the /message?sessionId=… endpoint.

Forwards every JSON-RPC line read from STDIN as an HTTP POST to BASE_URL/message?sessionId=….

Streams each SSE data: payload (other than the endpoint event) to STDOUT.

Logs every message sent and received (with timestamps and pretty-printed JSON) to MCPProxy-STDIO.log in your temp directory.

Features
Bidirectional bridge: STDIN→HTTP POST and SSE→STDOUT.

Automatic session discovery via the event: endpoint frame.

Structured logging (timestamps, “Sent”/“Received” markers, indented JSON).

Configurable headers on both SSE subscribe and POST requests.

Prerequisites
.NET 6.0 SDK or later

An MCP-compatible server exposing:

GET /sse (SSE stream with an event: endpoint frame)

POST /message?sessionId={id} (accepts JSON-RPC payloads)

Building
```
git clone https://github.com/yourorg/MCPProxy-STDIO-to-SSE.git
cd MCPProxy-STDIO-to-SSE
dotnet build -c Release
```

Usage
```
# Basic (no extra headers):
dotnet run --project src/Program.cs http://localhost:3001

# With additional HTTP headers:
dotnet run --project src/Program.cs http://localhost:3001 ApiKey 12345
```

Once launched, the app will:
Open GET http://localhost:3001/sse with Accept: text/event-stream.
Wait for an event: endpoint frame—e.g.:
```
event: endpoint
data: /message?sessionId=Wp6wxiY6PyBRoTtCNyLucw
```

Read lines from your STDIN and POST each one as JSON to:
```
POST http://localhost:3001/message?sessionId=Wp6wxiY6PyBRoTtCNyLucw
Content-Type: application/json

{"jsonrpc":"2.0","id":1, ...}
```

Print every other SSE data: payload to STDOUT, for example:
```
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05", ...}}
```

Write a detailed log of “Sent” and “Received” messages (with timestamps and formatted JSON) into:
```
%TEMP%\MCPProxy-STDIO.log
```

Use this bridge to connect any STDIO-driven LSP/JSON-RPC client (including Claude Desktop) to a web-based SSE/MCP backend seamlessly.
Sample Claude JSON configuration

```
{
  "mcpServers": {
    "Local CNET Debug": {
      "command": "C:\\Users\\User\\source\\repos\\MCPProxy-STDIO-to-SSE\\MCPProxy-STDIO-to-SSE\\bin\\Debug\\net6.0\\MCPProxy-STDIO-to-SSE.exe",
      "args": ["http://localhost:4858/McpHandler.ashx/sse"],
      "env": {
        "BRAVE_API_KEY": "your-api-key"
      }
    }
  }
}
```

![image](https://github.com/user-attachments/assets/ba5a2920-c369-4083-bf6b-7a32f9228893)

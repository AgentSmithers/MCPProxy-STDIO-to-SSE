// File: Program.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static StreamWriter _logWriter;
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1 && !Debugger.IsAttached)
        {
            Console.Error.WriteLine("Usage: dotnet run -- <BASE_URL> [HeaderName HeaderValue ...]");
            return 1;
        }

        var logPath = Path.Combine(Path.GetTempPath(), "MCPProxy-STDIO" + DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss") + ".log");
        _logWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };

        // 1) Parse base URL and optional headers
        var baseUrl = "";
        if (!Debugger.IsAttached)
        { 
            baseUrl = args[0].TrimEnd('/'); 
        }
        else
        {
            baseUrl = "http://localhost:4858/McpHandler.ashx";
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i + 1 < args.Length; i += 2)
            headers[args[i]] = args[i + 1];

        using var http = new HttpClient();
        // apply extra headers to every request
        foreach (var kv in headers)
            http.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);

        // 2) Start the SSE connection to {baseUrl}/sse
        var sseUrl = $"{baseUrl}/sse";
        LogMessage("Connector", $"Connecting to: {sseUrl}");
        var sseReq = new HttpRequestMessage(HttpMethod.Get, sseUrl);
        sseReq.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        HttpResponseMessage sseResp;
        try
        {
            sseResp = await http.SendAsync(sseReq, HttpCompletionOption.ResponseHeadersRead);
            sseResp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Failed to subscribe SSE at {sseUrl}: {ex.Message}");
            return 1;
        }

        var sseStream = await sseResp.Content.ReadAsStreamAsync();
        var sseReader = new StreamReader(sseStream, Encoding.UTF8);
        var stdinReader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);

        var cts = new CancellationTokenSource();
        var token = cts.Token;

        // TaskCompletionSource to receive message endpoint URL once SSE emits it
        var messageUrlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Task A: read SSE → parse endpoint event → forward other data events to STDOUT
        var sseTask = Task.Run(async () =>
        {
            var buffer = new StringBuilder();
            while (!sseReader.EndOfStream && !token.IsCancellationRequested)
            {
                var line = await sseReader.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("event:"))
                {
                    var evtName = line.Substring("event:".Length).Trim();
                    // endpoint event carries the message path
                    if (evtName == "endpoint")
                    {
                        // next non-empty line must be data:
                        while ((line = await sseReader.ReadLineAsync()) != null)
                        {
                            if (line.StartsWith("data:"))
                            {
                                var path = line.Substring("data:".Length).Trim(); // e.g. "/message?sessionId=..."
                                var fullMessageUrl = baseUrl + path;
                                LogMessage("Server->Client", $"[event: endpoint] {path}");
                                messageUrlTcs.TrySetResult(fullMessageUrl);
                                break;
                            }
                            else if (string.IsNullOrWhiteSpace(line))
                            {
                                // no data found
                                break;
                            }
                        }
                    }
                    continue;
                }

                if (line.StartsWith("data:"))
                {
                    buffer.AppendLine(line.Substring("data:".Length).Trim());
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    // complete event payload
                    var payload = buffer.ToString().TrimEnd();
                    buffer.Clear();
                    if (payload.Length > 0)
                    {
                        Console.WriteLine(payload);
                        LogMessage("Server->Client", $"[data: {payload}]");
                    }
                    else
                    {
                        LogMessage("Server->X", $"[Data is blank]");
                    }
                }
            }
        }, token);

        // Task B: once message URL is known, read STDIN → POST to {messageUrl}
        var inputTask = Task.Run(async () =>
        {
            var messageUrl = await messageUrlTcs.Task;
            LogMessage("Connector", $"Connecting to: {messageUrl}");
            string? jsonLine;
            while ((jsonLine = await stdinReader.ReadLineAsync()) != null)
            {
                LogMessage("Client->Server", jsonLine);

                using var content = new StringContent(jsonLine, Encoding.UTF8, "application/json");
                try
                {
                    var resp = await http.PostAsync(messageUrl, content);
                    resp.EnsureSuccessStatusCode();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ Error POSTing to {messageUrl}: {ex.Message}");
                }
            }
            // when STDIN closes, signal SSE to stop
            cts.Cancel();
        });

        // wait for either loop to finish (EOF or error)
        await Task.WhenAny(sseTask, inputTask);
        return 0;
    }


    static void Log(string text)
    {
        var timestamp = DateTime.Now.ToString("o");
        _logWriter.WriteLine($"{timestamp} {text}");
    }

    static void LogMessage(string direction, string message)
    {
        var timestamp = DateTime.Now.ToString("o");
        string body;

        // Pretty‑print JSON if possible
        try
        {
            using var doc = JsonDocument.Parse(message);
            body = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            body = message;
        }

        _logWriter.WriteLine($"{timestamp} [{direction}]");
        foreach (var line in body.Split('\n'))
            _logWriter.WriteLine($"    {line}");
    }

}

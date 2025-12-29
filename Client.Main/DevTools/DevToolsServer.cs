#if DEBUG
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Client.Main.Controls;
using Client.Main.Objects;
using Client.Main.Scenes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Client.Main.DevTools
{
    /// <summary>
    /// Embedded Kestrel web server for DevTools.
    /// Exposes REST endpoints and WebSocket for live metrics streaming.
    /// </summary>
    public sealed class DevToolsServer : IDisposable
    {
        private IHost _host;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<WebSocket> _connectedClients = new();
        private readonly object _clientsLock = new();
        private Timer _broadcastTimer;
        private bool _disposed;

        private const int BroadcastIntervalMs = 100; // 10Hz

        public bool IsRunning => _host != null;
        public int Port { get; private set; }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            IncludeFields = true  // Required to serialize struct fields
        };

        public async Task StartAsync(int port)
        {
            if (IsRunning || !Constants.ENABLE_DEVTOOLS) return;

            Port = port;

            try
            {
                _host = Host.CreateDefaultBuilder()
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(LogLevel.Warning);
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.ListenLocalhost(port);
                        });
                        webBuilder.Configure(ConfigureApp);
                    })
                    .Build();

                await _host.StartAsync(_cts.Token);

                // Start broadcast timer
                _broadcastTimer = new Timer(BroadcastMetrics, null,
                    BroadcastIntervalMs, BroadcastIntervalMs);

                Console.WriteLine($"[DevTools] Profiler started at http://localhost:{port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DevTools] Failed to start server: {ex.Message}");
            }
        }

        private void ConfigureApp(IApplicationBuilder app)
        {
            app.UseWebSockets();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                // Serve frontend
                endpoints.MapGet("/", ServeHtmlFrontend);

                // REST endpoints
                endpoints.MapGet("/api/hierarchy", GetHierarchy);
                endpoints.MapGet("/api/objects", GetObjects);
                endpoints.MapGet("/api/frame", GetCurrentFrame);
                endpoints.MapGet("/api/history", GetFrameHistory);
                endpoints.MapGet("/api/hotspots", GetHotspots);
                endpoints.MapGet("/api/scopes", GetScopes);

                // Recording endpoints
                endpoints.MapPost("/api/recording/start", StartRecording);
                endpoints.MapPost("/api/recording/stop", StopRecording);

                // WebSocket endpoint
                endpoints.Map("/ws", HandleWebSocket);
            });
        }

        #region REST Endpoints

        private async Task ServeHtmlFrontend(HttpContext context)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(DevToolsFrontend.Html);
        }

        private async Task GetHierarchy(HttpContext context)
        {
            var scene = MuGame.Instance?.ActiveScene;
            if (scene == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("No active scene");
                return;
            }

            var hierarchy = BuildControlSnapshot(scene);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(hierarchy, JsonOptions));
        }

        private async Task GetObjects(HttpContext context)
        {
            var scene = MuGame.Instance?.ActiveScene;
            var world = scene?.World;

            if (world == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("No world loaded");
                return;
            }

            var objects = new List<WorldObjectSnapshot>();
            foreach (var obj in world.Objects.GetSnapshot())
            {
                objects.Add(BuildObjectSnapshot(obj));
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(objects, JsonOptions));
        }

        private async Task GetCurrentFrame(HttpContext context)
        {
            var collector = DevToolsCollector.Instance;
            if (collector == null)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync("Collector not initialized");
                return;
            }

            if (TryGetOffsetQuery(context, out int offset))
            {
                if (!collector.TryGetFrameSnapshotByOffset(offset, out var snap, out _, out _))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Frame not found");
                    return;
                }

                var jsonSnap = FrameMetricsJson.FromData(snap);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(jsonSnap, JsonOptions));
                return;
            }

            if (TryGetFrameQuery(context, out int frameIndex))
            {
                if (!collector.TryGetFrameSnapshot(frameIndex, out var snap, out _, out _))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Frame not found");
                    return;
                }

                var jsonSnap = FrameMetricsJson.FromData(snap);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(jsonSnap, JsonOptions));
                return;
            }

            var frame = collector.GetLastFrame();
            var json = FrameMetricsJson.FromData(frame);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(json, JsonOptions));
        }

        private async Task GetFrameHistory(HttpContext context)
        {
            var collector = DevToolsCollector.Instance;
            if (collector == null)
            {
                context.Response.StatusCode = 503;
                return;
            }

            var history = collector.GetFrameHistory();
            var jsonHistory = history
                .Where(f => f.FrameIndex > 0)
                .Select(f => FrameMetricsJson.FromData(f))
                .ToList();

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(jsonHistory, JsonOptions));
        }

        private async Task GetHotspots(HttpContext context)
        {
            var collector = DevToolsCollector.Instance;
            if (collector == null)
            {
                context.Response.StatusCode = 503;
                return;
            }

            int count = 10;
            if (context.Request.Query.TryGetValue("count", out var countStr) &&
                int.TryParse(countStr, out var parsedCount))
            {
                count = Math.Clamp(parsedCount, 1, 50);
            }

            List<HotspotEntry> hotspots;
            if (TryGetOffsetQuery(context, out int offset))
            {
                if (!collector.TryGetFrameSnapshotByOffset(offset, out _, out _, out var frameHotspots))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Frame not found");
                    return;
                }

                hotspots = frameHotspots ?? new List<HotspotEntry>();
                if (count < hotspots.Count)
                {
                    var sliced = new List<HotspotEntry>(count);
                    for (int i = 0; i < count; i++)
                        sliced.Add(hotspots[i]);
                    hotspots = sliced;
                }
            }
            else if (TryGetFrameQuery(context, out int frameIndex))
            {
                if (!collector.TryGetFrameSnapshot(frameIndex, out _, out _, out var frameHotspots))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Frame not found");
                    return;
                }

                hotspots = frameHotspots ?? new List<HotspotEntry>();
                if (count < hotspots.Count)
                {
                    var sliced = new List<HotspotEntry>(count);
                    for (int i = 0; i < count; i++)
                        sliced.Add(hotspots[i]);
                    hotspots = sliced;
                }
            }
            else
            {
                hotspots = collector.GetHotspots(count);
            }
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(hotspots, JsonOptions));
        }

        private async Task GetScopes(HttpContext context)
        {
            var collector = DevToolsCollector.Instance;
            if (collector == null)
            {
                context.Response.StatusCode = 503;
                return;
            }

            ProfileScopeJson scopeTree;
            if (TryGetOffsetQuery(context, out int offset))
            {
                if (!collector.TryGetFrameSnapshotByOffset(offset, out _, out var frameScope, out _))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Frame not found");
                    return;
                }

                scopeTree = frameScope;
            }
            else if (TryGetFrameQuery(context, out int frameIndex))
            {
                if (!collector.TryGetFrameSnapshot(frameIndex, out _, out var frameScope, out _))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Frame not found");
                    return;
                }

                scopeTree = frameScope;
            }
            else
            {
                scopeTree = collector.GetScopeTree();
            }
            context.Response.ContentType = "application/json";

            if (scopeTree == null)
            {
                await context.Response.WriteAsync("null");
                return;
            }

            await context.Response.WriteAsync(JsonSerializer.Serialize(scopeTree, JsonOptions));
        }

        private async Task StartRecording(HttpContext context)
        {
            DevToolsCollector.Instance?.StartRecording();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"status\":\"started\"}");
        }

        private async Task StopRecording(HttpContext context)
        {
            var recording = DevToolsCollector.Instance?.StopRecording();
            if (recording == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("{\"error\":\"No recording in progress\"}");
                return;
            }

            var jsonRecording = recording.Select(f => FrameMetricsJson.FromData(f)).ToList();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = "stopped",
                frameCount = jsonRecording.Count,
                frames = jsonRecording
            }, JsonOptions));
        }

        #endregion

        private static bool TryGetFrameQuery(HttpContext context, out int frameIndex)
        {
            frameIndex = 0;
            if (context.Request.Query.TryGetValue("frame", out var frameStr) &&
                int.TryParse(frameStr, out var parsed))
            {
                frameIndex = parsed;
                return true;
            }

            return false;
        }

        private static bool TryGetOffsetQuery(HttpContext context, out int offset)
        {
            offset = 0;
            if (context.Request.Query.TryGetValue("offset", out var offsetStr) &&
                int.TryParse(offsetStr, out var parsed))
            {
                offset = parsed;
                return true;
            }

            return false;
        }

        #region WebSocket

        private async Task HandleWebSocket(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            lock (_clientsLock) { _connectedClients.Add(ws); }

            try
            {
                var buffer = new byte[1024];
                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                lock (_clientsLock) { _connectedClients.Remove(ws); }
            }
        }

        private void BroadcastMetrics(object state)
        {
            if (_disposed) return;

            var collector = DevToolsCollector.Instance;
            if (collector == null) return;

            List<WebSocket> clients;
            lock (_clientsLock)
            {
                if (_connectedClients.Count == 0) return;
                clients = _connectedClients.ToList();
            }

            try
            {
                var frame = collector.GetLastFrame();
                var message = new WebSocketMessage
                {
                    Type = "frame",
                    Data = FrameMetricsJson.FromData(frame)
                };

                var json = JsonSerializer.Serialize(message, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);

                foreach (var ws in clients)
                {
                    if (ws.State != WebSocketState.Open)
                    {
                        lock (_clientsLock) { _connectedClients.Remove(ws); }
                        continue;
                    }

                    try
                    {
                        // Fire and forget - don't block the timer
                        _ = ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch
                    {
                        lock (_clientsLock) { _connectedClients.Remove(ws); }
                    }
                }

                // Send hotspots every 500ms (every 5th broadcast)
                if (frame.FrameIndex % 5 == 0)
                {
                    var hotspots = collector.GetHotspots(10);
                    var hotspotsMessage = new WebSocketMessage
                    {
                        Type = "hotspots",
                        Data = hotspots
                    };

                    var hotspotsJson = JsonSerializer.Serialize(hotspotsMessage, JsonOptions);
                    var hotspotsBytes = Encoding.UTF8.GetBytes(hotspotsJson);

                    foreach (var ws in clients)
                    {
                        if (ws.State == WebSocketState.Open)
                        {
                            try { _ = ws.SendAsync(hotspotsBytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DevTools] Broadcast error: {ex.Message}");
            }
        }

        #endregion

        #region Snapshot Builders

        private ControlSnapshot BuildControlSnapshot(GameControl control)
        {
            var children = new List<ControlSnapshot>();
            double childrenUpdateMs = 0;
            double childrenDrawMs = 0;

            foreach (var child in control.Controls.GetSnapshot())
            {
                var childSnapshot = BuildControlSnapshot(child);
                children.Add(childSnapshot);
                // Sum up children's total times (which already include their descendants)
                childrenUpdateMs += childSnapshot.LastUpdateMs;
                childrenDrawMs += childSnapshot.LastDrawMs;
            }

            // control.LastUpdateTimeMs/LastDrawTimeMs ALREADY INCLUDE children's time
            // (measured from start of Update/Draw to end, which includes child.Update/Draw calls)
            double totalUpdateMs = control.LastUpdateTimeMs;
            double totalDrawMs = control.LastDrawTimeMs;

            // Self time = total time - sum of direct children's total times
            double selfUpdateMs = Math.Max(0, totalUpdateMs - childrenUpdateMs);
            double selfDrawMs = Math.Max(0, totalDrawMs - childrenDrawMs);

            return new ControlSnapshot
            {
                Id = RuntimeHelpers.GetHashCode(control).ToString(),
                Name = control.Name ?? control.GetType().Name,
                TypeName = control.GetType().Name,
                ParentId = control.Parent != null ? RuntimeHelpers.GetHashCode(control.Parent).ToString() : null,
                SelfUpdateMs = selfUpdateMs,
                SelfDrawMs = selfDrawMs,
                LastUpdateMs = totalUpdateMs,
                LastDrawMs = totalDrawMs,
                Visible = control.Visible,
                Interactive = control.Interactive,
                ChildCount = control.Controls.Count,
                Children = children
            };
        }

        private WorldObjectSnapshot BuildObjectSnapshot(WorldObject obj)
        {
            string modelPath = null;
            string currentAction = null;

            if (obj is ModelObject modelObj)
            {
                modelPath = modelObj.Model?.Name;
                currentAction = modelObj.CurrentAction.ToString();
            }

            return new WorldObjectSnapshot
            {
                Id = RuntimeHelpers.GetHashCode(obj).ToString(),
                ObjectId = 0, // Not all objects have IDs
                Type = obj.GetType().Name,
                Name = obj.DisplayName ?? obj.GetType().Name,
                X = obj.Position.X,
                Y = obj.Position.Y,
                UpdateTimeMs = obj.LastUpdateTimeMs,
                DrawTimeMs = obj.LastDrawTimeMs,
                ModelPath = modelPath,
                CurrentAction = currentAction,
                IsVisible = obj.Visible,
                IsInFrustum = !obj.OutOfView
            };
        }

        #endregion

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            _broadcastTimer?.Dispose();
            _cts.Cancel();

            lock (_clientsLock)
            {
                foreach (var ws in _connectedClients)
                {
                    try
                    {
                        ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None).Wait(1000);
                    }
                    catch { }
                }
                _connectedClients.Clear();
            }

            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
            }

            Console.WriteLine("[DevTools] Server stopped");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopAsync().Wait(2000);
            _cts.Dispose();
        }
    }
}
#endif

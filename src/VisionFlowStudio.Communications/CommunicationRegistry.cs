using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using HslCommunication;
using HslCommunication.ModBus;
using HslCommunication.Profinet.AllenBradley;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Omron;
using HslCommunication.Profinet.Siemens;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Communications
{
    public sealed class CommunicationOperationResult
    {
        public bool Success { get; set; }
        public bool HasValue { get; set; }
        public string Message { get; set; }
        public object Value { get; set; }
        public string ConnectionId { get; set; }
    }

    public sealed class CommunicationTextField
    {
        public string Template { get; set; }
        public string DataType { get; set; }
        public object Value { get; set; }
    }

    public sealed class CommunicationJsonField
    {
        public string Path { get; set; }
        public string DataType { get; set; }
        public object Value { get; set; }
    }

    public sealed class CommunicationRegistry : IDisposable
    {
        public Func<string, object> RuntimeValueProvider { get; set; }

        private sealed class Session
        {
            public string Fingerprint;
            public object Device;
            public bool IsSerial;
            public bool IsTcpText;
        }

        private sealed class TcpTextTransport : IDisposable
        {
            private sealed class ReceivedMessage
            {
                public string Text;
                public string ConnectionId;
            }

            private readonly CommunicationDefinition _config;
            private readonly Func<string, object> _runtimeValueProvider;
            private readonly Encoding _encoding;
            private readonly string _sendTerminator;
            private readonly string _receiveTerminator;
            private readonly string _frameMode;
            private readonly int _lengthPrefixBytes;
            private readonly bool _lengthPrefixBigEndian;
            private readonly int _maxFrameBytes;
            private readonly ConcurrentQueue<ReceivedMessage> _messages = new ConcurrentQueue<ReceivedMessage>();
            private readonly List<TcpClient> _clients = new List<TcpClient>();
            private readonly Dictionary<TcpClient, string> _connectionIds = new Dictionary<TcpClient, string>();
            private readonly object _clientsSync = new object();
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private TcpListener _listener;
            private TcpClient _client;
            private volatile bool _disposed;
            private volatile string _lastError;

            public TcpTextTransport(CommunicationDefinition config, Func<string, object> runtimeValueProvider)
            {
                _config = config;
                _runtimeValueProvider = runtimeValueProvider;
                _encoding = ResolveEncoding(config.TextEncoding);
                _sendTerminator = DecodeTerminator(ResolveTerminator(config.SendTerminator, config.MessageTerminator));
                _receiveTerminator = DecodeTerminator(ResolveTerminator(config.ReceiveTerminator, config.MessageTerminator));
                _frameMode = NormalizeFrameMode(config.FrameMode);
                _lengthPrefixBytes = NormalizeLengthPrefixBytes(config.LengthPrefixBytes);
                _lengthPrefixBigEndian = !string.Equals(config.LengthByteOrder, "LittleEndian", StringComparison.OrdinalIgnoreCase);
                _maxFrameBytes = config.MaxFrameBytes <= 0 ? 4194304 : config.MaxFrameBytes;
            }

            public bool IsServer { get { return IsTcpServer(_config); } }

            public void Start()
            {
                ValidatePort(_config.Port);
                if (IsServer) StartServer(); else StartClient();
            }

            public string GetConnectionDescription()
            {
                if (IsServer) return string.Format("{0}:{1} 监听成功", NormalizeServerHost(_config.Host), _config.Port);
                return string.Format("{0}:{1} 连接成功", _config.Host, _config.Port);
            }

            public bool TryDequeue(out string message, out string connectionId)
            {
                ReceivedMessage received;
                if (_messages.TryDequeue(out received))
                {
                    message = received.Text; connectionId = received.ConnectionId; return true;
                }
                message = null; connectionId = null; return false;
            }

            public string GetReceiveError()
            {
                if (IsServer || !_disposed && IsConnected(_client)) return null;
                return string.IsNullOrWhiteSpace(_lastError) ? "TCP 客户端连接已断开" : _lastError;
            }

            public int Send(string message, string connectionId = null)
            {
                if (_disposed) throw new ObjectDisposedException("TCP/IP 通道");
                var payload = BuildFrame(message ?? string.Empty);
                if (IsServer)
                {
                    TcpClient[] clients;
                    lock (_clientsSync)
                    {
                        clients = _clients.Where(IsConnected)
                            .Where(x => string.IsNullOrWhiteSpace(connectionId) || ConnectionIdEquals(x, connectionId))
                            .ToArray();
                    }
                    if (clients.Length == 0) throw new InvalidOperationException("TCP 服务器当前没有已连接的客户端");
                    var sent = 0;
                    foreach (var client in clients)
                    {
                        try { WritePayload(client, payload); sent++; }
                        catch { RemoveClient(client); }
                    }
                    if (sent == 0) throw new InvalidOperationException("TCP 服务器向客户端发送失败");
                    return sent;
                }
                if (!IsConnected(_client)) throw new InvalidOperationException("TCP 客户端未连接");
                WritePayload(_client, payload);
                return 1;
            }

            private void StartClient()
            {
                if (string.IsNullOrWhiteSpace(_config.Host)) throw new InvalidOperationException("TCP 客户端主机不能为空");
                var client = CreateClient();
                try
                {
                    var connect = client.ConnectAsync(_config.Host.Trim(), _config.Port);
                    if (!connect.Wait(Math.Max(100, _config.ConnectTimeoutMs)))
                        throw new TimeoutException("TCP 客户端连接超时");
                    if (connect.IsFaulted) throw connect.Exception == null ? new InvalidOperationException("TCP 客户端连接失败") : connect.Exception.GetBaseException();
                    _client = client;
                    Task.Run(() => ReceiveLoopAsync(client, _cancellation.Token));
                }
                catch { try { client.Close(); } catch { } throw; }
            }

            private void StartServer()
            {
                var address = ResolveListenAddress(_config.Host);
                _listener = new TcpListener(address, _config.Port);
                _listener.Start();
                Task.Run(() => AcceptLoopAsync(_cancellation.Token));
            }

            private async Task AcceptLoopAsync(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = null;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        ConfigureClient(client);
                        lock (_clientsSync) { _clients.Add(client); _connectionIds[client] = Guid.NewGuid().ToString("N"); }
                        var receiveTask = Task.Run(() => ReceiveLoopAsync(client, token));
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException ex)
                    {
                        if (token.IsCancellationRequested) break;
                        _lastError = ex.Message;
                        if (client != null) RemoveClient(client);
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested) break;
                        _lastError = ex.Message;
                        if (client != null) RemoveClient(client);
                    }
                }
            }

            private async Task ReceiveLoopAsync(TcpClient client, CancellationToken token)
            {
                var bytes = new byte[4096];
                var pending = new MemoryStream();
                try
                {
                    var stream = client.GetStream();
                    while (!token.IsCancellationRequested)
                    {
                        var byteCount = await stream.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                        if (byteCount <= 0) break;
                        pending.Position = pending.Length;
                        pending.Write(bytes, 0, byteCount);
                        ExtractMessages(pending, client);
                    }
                    if (pending.Length > 0 && !IsLengthPrefixMode) HandleReceivedMessage(_encoding.GetString(pending.ToArray()), client);
                    if (!token.IsCancellationRequested && !IsServer) _lastError = "TCP 客户端连接已由远端关闭";
                }
                catch (ObjectDisposedException) { }
                catch (IOException ex) { if (!token.IsCancellationRequested) _lastError = ex.Message; }
                catch (SocketException ex) { if (!token.IsCancellationRequested) _lastError = ex.Message; }
                catch (Exception ex) { if (!token.IsCancellationRequested) _lastError = ex.Message; }
                finally
                {
                    pending.Dispose();
                    if (IsServer) RemoveClient(client);
                    else try { client.Close(); } catch { }
                }
            }

            private bool IsLengthPrefixMode { get { return string.Equals(_frameMode, "LengthPrefix", StringComparison.OrdinalIgnoreCase); } }

            private byte[] BuildFrame(string message)
            {
                var body = _encoding.GetBytes(message ?? string.Empty);
                if (body.Length > _maxFrameBytes) throw new InvalidOperationException(string.Format("TCP 报文长度 {0} 超过上限 {1} 字节", body.Length, _maxFrameBytes));
                if (!IsLengthPrefixMode) return _encoding.GetBytes((message ?? string.Empty) + _sendTerminator);
                var prefix = EncodeLength(body.Length, _lengthPrefixBytes, _lengthPrefixBigEndian);
                var frame = new byte[prefix.Length + body.Length];
                Buffer.BlockCopy(prefix, 0, frame, 0, prefix.Length);
                Buffer.BlockCopy(body, 0, frame, prefix.Length, body.Length);
                return frame;
            }

            private void ExtractMessages(MemoryStream pending, TcpClient sourceClient)
            {
                if (IsLengthPrefixMode) ExtractLengthPrefixedMessages(pending, sourceClient);
                else ExtractTerminatedMessages(pending, sourceClient);
            }

            private void ExtractTerminatedMessages(MemoryStream pending, TcpClient sourceClient)
            {
                var data = pending.ToArray();
                if (string.IsNullOrEmpty(_receiveTerminator))
                {
                    if (data.Length > 0) HandleReceivedMessage(_encoding.GetString(data), sourceClient);
                    pending.SetLength(0);
                    return;
                }
                var terminator = _encoding.GetBytes(_receiveTerminator);
                var offset = 0;
                while (true)
                {
                    var index = IndexOf(data, terminator, offset);
                    if (index < 0) break;
                    HandleReceivedMessage(_encoding.GetString(data, offset, index - offset), sourceClient);
                    offset = index + terminator.Length;
                }
                RetainBytes(pending, data, offset);
            }

            private void ExtractLengthPrefixedMessages(MemoryStream pending, TcpClient sourceClient)
            {
                var data = pending.ToArray(); var offset = 0;
                while (data.Length - offset >= _lengthPrefixBytes)
                {
                    var length = DecodeLength(data, offset, _lengthPrefixBytes, _lengthPrefixBigEndian);
                    if (length < 0 || length > _maxFrameBytes) throw new InvalidOperationException(string.Format("TCP 长度头 {0} 无效，允许范围 0..{1}", length, _maxFrameBytes));
                    if (data.Length - offset - _lengthPrefixBytes < length) break;
                    offset += _lengthPrefixBytes;
                    HandleReceivedMessage(_encoding.GetString(data, offset, length), sourceClient);
                    offset += length;
                }
                RetainBytes(pending, data, offset);
            }

            private static void RetainBytes(MemoryStream pending, byte[] data, int offset)
            {
                pending.SetLength(0);
                if (offset < data.Length) pending.Write(data, offset, data.Length - offset);
                pending.Position = pending.Length;
            }

            private static int IndexOf(byte[] data, byte[] pattern, int start)
            {
                if (pattern == null || pattern.Length == 0) return -1;
                for (var index = Math.Max(0, start); index <= data.Length - pattern.Length; index++)
                {
                    var matched = true;
                    for (var part = 0; part < pattern.Length; part++) if (data[index + part] != pattern[part]) { matched = false; break; }
                    if (matched) return index;
                }
                return -1;
            }

            private static byte[] EncodeLength(int value, int bytes, bool bigEndian)
            {
                var result = new byte[bytes]; var remaining = (ulong)value;
                for (var index = 0; index < bytes; index++)
                {
                    var target = bigEndian ? bytes - index - 1 : index;
                    result[target] = (byte)(remaining & 0xFF); remaining >>= 8;
                }
                if (remaining != 0) throw new InvalidOperationException("TCP 报文长度超出长度头可表示范围");
                return result;
            }

            private static int DecodeLength(byte[] data, int offset, int bytes, bool bigEndian)
            {
                ulong result = 0;
                for (var index = 0; index < bytes; index++)
                {
                    var source = bigEndian ? offset + index : offset + bytes - index - 1;
                    result = (result << 8) | data[source];
                }
                if (result > int.MaxValue) throw new InvalidOperationException("TCP 长度头超过 Int32 范围");
                return (int)result;
            }

            private void HandleReceivedMessage(string message, TcpClient sourceClient)
            {
                if (sourceClient == null) sourceClient = IsServer ? FindSingleConnectedClient() : _client;
                if (TryAutoRespond(message ?? string.Empty, sourceClient)) return;
                var connectionId = GetConnectionId(sourceClient);
                _messages.Enqueue(new ReceivedMessage { Text = message ?? string.Empty, ConnectionId = connectionId });
                ReceivedMessage discarded;
                while (_messages.Count > 1000) _messages.TryDequeue(out discarded);
            }

            private bool TryAutoRespond(string message, TcpClient sourceClient)
            {
                foreach (var rule in _config.AutoResponses ?? Enumerable.Empty<CommunicationAutoResponseDefinition>())
                {
                    if (rule == null || !rule.Enabled) continue;
                    object value;
                    if (!TryGetJsonPathValue(message, rule.MatchPath, out value)) continue;
                    var actual = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    var expected = rule.ExpectedValue ?? string.Empty;
                    var matches = string.Equals(rule.MatchMode, "Contains", StringComparison.OrdinalIgnoreCase)
                        ? actual.IndexOf(expected, StringComparison.Ordinal) >= 0
                        : string.Equals(actual, expected, StringComparison.Ordinal);
                    if (!matches) continue;
                    var response = ExpandJsonTemplate(rule.ResponseTemplate, message, _runtimeValueProvider);
                    if (sourceClient == null || !IsConnected(sourceClient)) throw new InvalidOperationException("TCP 自动应答时连接已断开");
                    WritePayload(sourceClient, BuildFrame(response));
                    return rule.ConsumeMessage;
                }
                return false;
            }

            private TcpClient FindSingleConnectedClient()
            {
                lock (_clientsSync) return _clients.FirstOrDefault(IsConnected);
            }

            private string GetConnectionId(TcpClient client)
            {
                if (!IsServer) return "server";
                if (client == null) return string.Empty;
                lock (_clientsSync)
                {
                    string id; return _connectionIds.TryGetValue(client, out id) ? id : string.Empty;
                }
            }

            private bool ConnectionIdEquals(TcpClient client, string connectionId)
            {
                string id; return _connectionIds.TryGetValue(client, out id) && string.Equals(id, connectionId, StringComparison.OrdinalIgnoreCase);
            }

            private TcpClient CreateClient()
            {
                var client = new TcpClient(); ConfigureClient(client); return client;
            }

            private void ConfigureClient(TcpClient client)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = Math.Max(0, _config.ReceiveTimeoutMs);
                client.SendTimeout = Math.Max(0, _config.ReceiveTimeoutMs);
            }

            private static void WritePayload(TcpClient client, byte[] payload)
            {
                var stream = client.GetStream();
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }

            private void RemoveClient(TcpClient client)
            {
                if (client == null) return;
                lock (_clientsSync) { _clients.Remove(client); _connectionIds.Remove(client); }
                try { client.Close(); } catch { }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _cancellation.Cancel();
                try { if (_listener != null) _listener.Stop(); } catch { }
                try { if (_client != null) _client.Close(); } catch { }
                TcpClient[] clients;
                lock (_clientsSync) { clients = _clients.ToArray(); _clients.Clear(); _connectionIds.Clear(); }
                foreach (var client in clients) try { client.Close(); } catch { }
                _cancellation.Dispose();
            }

            private static bool IsConnected(TcpClient client)
            {
                if (client == null || !client.Connected) return false;
                try { return !(client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0); }
                catch { return false; }
            }

            private static void ValidatePort(int port)
            {
                if (port < 1 || port > 65535) throw new InvalidOperationException("TCP 端口必须在 1 到 65535 之间");
            }

            private static IPAddress ResolveListenAddress(string host)
            {
                host = NormalizeServerHost(host);
                if (host == "0.0.0.0" || host == "*") return IPAddress.Any;
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
                IPAddress address;
                if (IPAddress.TryParse(host, out address)) return address;
                address = Dns.GetHostAddresses(host).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                if (address == null) throw new InvalidOperationException("无法解析 TCP 服务器监听地址：" + host);
                return address;
            }

            private static string NormalizeServerHost(string host)
            {
                return string.IsNullOrWhiteSpace(host) ? "0.0.0.0" : host.Trim();
            }

            private static Encoding ResolveEncoding(string name)
            {
                try { return Encoding.GetEncoding(string.IsNullOrWhiteSpace(name) ? "UTF-8" : name.Trim()); }
                catch (Exception ex) { throw new InvalidOperationException("不支持的文本编码：" + name, ex); }
            }

            private static string DecodeTerminator(string value)
            {
                if (value == null) return "\r\n";
                return value.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\0", "\0");
            }

            private static string ResolveTerminator(string specific, string legacy)
            {
                return specific == null ? (legacy == null ? "\\r\\n" : legacy) : specific;
            }

            private static string NormalizeFrameMode(string value)
            {
                return string.Equals(value, "LengthPrefix", StringComparison.OrdinalIgnoreCase) ? "LengthPrefix" : "Terminator";
            }

            private static int NormalizeLengthPrefixBytes(int value)
            {
                if (value == 0) return 4;
                if (value == 1 || value == 2 || value == 4 || value == 8) return value;
                throw new InvalidOperationException("TCP 长度头字节数仅支持 1、2、4、8");
            }
        }

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();
        public static readonly string[] Protocols = { "TCP/IP Client", "TCP/IP Server", "Siemens S7Net", "Mitsubishi MC ASCII", "Modbus TCP", "Modbus RTU", "Omron FINS TCP", "Allen-Bradley EtherNet/IP" };
        public static readonly string[] DataTypes =
        {
            "Bool", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double", "String",
            "Json", "BoolArray", "ByteArray", "Int16Array", "UInt16Array", "Int32Array", "UInt32Array", "Int64Array", "UInt64Array", "FloatArray", "DoubleArray"
        };

        public static bool IsTcpProtocol(string protocol)
        {
            return string.Equals(protocol, "TCP/IP Client", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "TCP/IP Server", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTcpServer(CommunicationDefinition config)
        {
            return config != null && string.Equals(config.Protocol, "TCP/IP Server", StringComparison.OrdinalIgnoreCase);
        }

        public CommunicationOperationResult TestConnection(CommunicationDefinition config)
        {
            try
            {
                Invalidate(config == null ? null : config.Name);
                var session = GetOrCreate(config);
                var tcp = session.Device as TcpTextTransport;
                var detail = tcp == null ? config.Name + " 连接成功（" + config.Protocol + "）" : config.Name + " " + tcp.GetConnectionDescription() + "（" + config.Protocol + "）";
                return new CommunicationOperationResult { Success = true, Message = detail };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = (config == null ? "通信通道" : config.Name) + " 连接失败：" + ex.Message }; }
        }

        public CommunicationOperationResult Write(CommunicationDefinition config, string address, string dataType, object value)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                var session = GetOrCreate(config);
                var converted = ConvertValue(value, dataType);
                if (session.IsTcpText)
                {
                    var message = FormatTcpMessage(address, converted);
                    var recipients = ((TcpTextTransport)session.Device).Send(message);
                    return new CommunicationOperationResult { Success = true, Message = string.Format("{0} 发送成功：{1}（{2} 个连接）", config.Name, message, recipients) };
                }
                if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("写入地址不能为空");
                dynamic device = session.Device; dynamic data = converted;
                OperateResult result = device.Write(address, data);
                if (!result.IsSuccess) Invalidate(config.Name);
                return new CommunicationOperationResult { Success = result.IsSuccess, Message = result.IsSuccess ? string.Format("{0} 写入成功：{1}={2} ({3})", config.Name, address, FormatValue(converted), dataType) : result.Message };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        public CommunicationOperationResult WriteCombined(CommunicationDefinition config, IEnumerable<CommunicationTextField> fields)
        {
            return WriteCombined(config, fields, null);
        }

        public CommunicationOperationResult WriteCombined(CommunicationDefinition config, IEnumerable<CommunicationTextField> fields, string connectionId)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                if (!IsTcpProtocol(config.Protocol)) throw new InvalidOperationException("合并发送只适用于 TCP/IP 文本通道");
                var values = (fields ?? Enumerable.Empty<CommunicationTextField>()).Where(x => x != null).Select(x => FormatTcpMessage(x.Template, ConvertValue(x.Value, x.DataType))).ToArray();
                if (values.Length == 0) throw new InvalidOperationException("没有可发送的 TCP/IP 字段");
                var separator = DecodeControlText(config.FieldSeparator, "|");
                var message = string.Join(separator, values);
                var recipients = ((TcpTextTransport)GetOrCreate(config).Device).Send(message, connectionId);
                return new CommunicationOperationResult { Success = true, Message = string.Format("{0} 合并发送成功：{1}（{2} 个字段，{3} 个连接）", config.Name, message, values.Length, recipients), Value = message, HasValue = true };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        public CommunicationOperationResult WriteJson(CommunicationDefinition config, IEnumerable<CommunicationJsonField> fields, string connectionId = null)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                if (!IsTcpProtocol(config.Protocol)) throw new InvalidOperationException("JSON 发送只适用于 TCP/IP 通道");
                var document = BuildJsonDocument(fields);
                var message = new JavaScriptSerializer { MaxJsonLength = Math.Max(1024, config.MaxFrameBytes <= 0 ? 4194304 : config.MaxFrameBytes) }.Serialize(document);
                var recipients = ((TcpTextTransport)GetOrCreate(config).Device).Send(message, connectionId);
                return new CommunicationOperationResult { Success = true, Message = string.Format("{0} JSON 发送成功：{1}（{2} 个连接）", config.Name, message, recipients), Value = message, HasValue = true };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        public CommunicationOperationResult WriteRawText(CommunicationDefinition config, string message, string connectionId = null)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                if (!IsTcpProtocol(config.Protocol)) throw new InvalidOperationException("原始文本发送只适用于 TCP/IP 通道");
                var recipients = ((TcpTextTransport)GetOrCreate(config).Device).Send(message ?? string.Empty, connectionId);
                return new CommunicationOperationResult { Success = true, HasValue = true, Value = message ?? string.Empty, Message = string.Format("{0} 发送成功：{1}（{2} 个连接）", config.Name, message, recipients) };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        public static IDictionary<string, string> ExtractTextFields(string message, string separator, IEnumerable<CommunicationFieldExtractionDefinition> definitions)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var raw = message ?? string.Empty;
            string[] parts = null;
            foreach (var definition in definitions ?? Enumerable.Empty<CommunicationFieldExtractionDefinition>())
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Name)) continue;
                var name = definition.Name.Trim();
                string value;
                if (string.Equals(definition.Mode, "Position", StringComparison.OrdinalIgnoreCase))
                {
                    if (definition.Start < 0 || definition.Start > raw.Length) throw new InvalidOperationException(string.Format("字段 {0} 的起始位置 {1} 超出报文长度 {2}", name, definition.Start, raw.Length));
                    var length = definition.Length <= 0 ? raw.Length - definition.Start : definition.Length;
                    if (definition.Start + length > raw.Length) throw new InvalidOperationException(string.Format("字段 {0} 的截取范围超出报文长度 {1}", name, raw.Length));
                    value = raw.Substring(definition.Start, length);
                }
                else if (string.Equals(definition.Mode, "JsonPath", StringComparison.OrdinalIgnoreCase))
                {
                    object jsonValue;
                    var path = string.IsNullOrWhiteSpace(definition.JsonPath) ? name : definition.JsonPath.Trim();
                    if (!TryGetJsonPathValue(raw, path, out jsonValue))
                    {
                        if (definition.Optional) { result[name] = string.Empty; continue; }
                        throw new InvalidOperationException(string.Format("JSON 路径 {0} 不存在（字段 {1}）", path, name));
                    }
                    value = JsonValueToText(jsonValue);
                }
                else
                {
                    var decodedSeparator = DecodeControlText(separator, "|");
                    if (string.IsNullOrEmpty(decodedSeparator)) throw new InvalidOperationException("按分隔符提取时，字段分隔符不能为空");
                    if (parts == null) parts = raw.Split(new[] { decodedSeparator }, StringSplitOptions.None);
                    if (definition.FieldIndex < 0 || definition.FieldIndex >= parts.Length) throw new InvalidOperationException(string.Format("字段 {0} 的序号 {1} 超出报文字段数量 {2}", name, definition.FieldIndex, parts.Length));
                    value = parts[definition.FieldIndex];
                }
                result[name] = definition.Trim ? value.Trim() : value;
            }
            return result;
        }

        public CommunicationOperationResult Read(CommunicationDefinition config, string address, string dataType)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                if (IsTcpProtocol(config.Protocol)) return ReceiveText(config);
                if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("读取地址不能为空");
                var session = GetOrCreate(config); dynamic device = session.Device; dynamic result;
                switch (dataType)
                {
                    case "Bool": result = device.ReadBool(address); break;
                    case "Byte": result = device.ReadByte(address); break;
                    case "Int16": result = device.ReadInt16(address); break;
                    case "UInt16": result = device.ReadUInt16(address); break;
                    case "Int32": result = device.ReadInt32(address); break;
                    case "UInt32": result = device.ReadUInt32(address); break;
                    case "Int64": result = device.ReadInt64(address); break;
                    case "UInt64": result = device.ReadUInt64(address); break;
                    case "Float": result = device.ReadFloat(address); break;
                    case "Double": result = device.ReadDouble(address); break;
                    case "String": result = device.ReadString(address); break;
                    default: throw new NotSupportedException("触发读取不支持数据类型：" + dataType);
                }
                if (!result.IsSuccess) Invalidate(config.Name);
                return new CommunicationOperationResult { Success = result.IsSuccess, HasValue = result.IsSuccess, Message = result.IsSuccess ? string.Format("{0} 读取成功：{1}={2}", config.Name, address, FormatValue(result.Content)) : result.Message, Value = result.IsSuccess ? result.Content : null };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        public CommunicationOperationResult ReceiveText(CommunicationDefinition config)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                if (!IsTcpProtocol(config.Protocol)) throw new InvalidOperationException("当前通道不是 TCP/IP 文本通道");
                var transport = (TcpTextTransport)GetOrCreate(config).Device;
                string message;
                string connectionId;
                if (transport.TryDequeue(out message, out connectionId))
                    return new CommunicationOperationResult { Success = true, HasValue = true, Value = message, ConnectionId = connectionId, Message = config.Name + " 接收：" + message };
                var error = transport.GetReceiveError();
                if (!string.IsNullOrWhiteSpace(error)) { Invalidate(config.Name); return new CommunicationOperationResult { Success = false, Message = error }; }
                return new CommunicationOperationResult { Success = true, HasValue = false, Message = "等待 TCP/IP 消息" };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        private Session GetOrCreate(CommunicationDefinition config)
        {
            if (config == null) throw new ArgumentNullException("config");
            lock (_sync)
            {
                Session existing; var fingerprint = GetFingerprint(config); var key = config.Name ?? string.Empty;
                if (_sessions.TryGetValue(key, out existing) && existing.Fingerprint == fingerprint) return existing;
                if (existing != null) { Close(existing); _sessions.Remove(key); }
                var session = Create(config); session.Fingerprint = fingerprint; _sessions[key] = session; return session;
            }
        }

        private void Invalidate(string name)
        {
            lock (_sync)
            {
                Session session; var key = name ?? string.Empty;
                if (!_sessions.TryGetValue(key, out session)) return;
                Close(session); _sessions.Remove(key);
            }
        }

        private Session Create(CommunicationDefinition config)
        {
            if (IsTcpProtocol(config.Protocol))
            {
                var tcp = new TcpTextTransport(config, ResolveTemplateValue); tcp.Start();
                return new Session { Device = tcp, IsTcpText = true };
            }
            object device; var serial = false;
            switch (config.Protocol)
            {
                case "Siemens S7Net":
                    SiemensPLCS plc; if (!Enum.TryParse(config.PlcModel ?? "S1200", true, out plc)) plc = SiemensPLCS.S1200;
                    device = new SiemensS7Net(plc, config.Host) { Port = config.Port, Rack = (byte)config.Rack, Slot = (byte)config.Slot, ConnectTimeOut = config.ConnectTimeoutMs, ReceiveTimeOut = config.ReceiveTimeoutMs }; break;
                case "Mitsubishi MC ASCII": device = new MelsecMcAsciiNet(config.Host, config.Port) { ConnectTimeOut = config.ConnectTimeoutMs, ReceiveTimeOut = config.ReceiveTimeoutMs }; break;
                case "Modbus TCP": device = new ModbusTcpNet(config.Host, config.Port, (byte)config.Station) { ConnectTimeOut = config.ConnectTimeoutMs, ReceiveTimeOut = config.ReceiveTimeoutMs }; break;
                case "Modbus RTU":
                    var modbus = new ModbusRtu((byte)config.Station); StopBits stop; Parity parity; if (!Enum.TryParse(config.StopBits, true, out stop)) stop = StopBits.One; if (!Enum.TryParse(config.Parity, true, out parity)) parity = Parity.None;
                    modbus.SerialPortInni(config.SerialPort, config.BaudRate, config.DataBits, stop, parity); modbus.ReceiveTimeout = config.ReceiveTimeoutMs; modbus.Open(); device = modbus; serial = true; break;
                case "Omron FINS TCP": device = new OmronFinsNet(config.Host, config.Port) { ConnectTimeOut = config.ConnectTimeoutMs, ReceiveTimeOut = config.ReceiveTimeoutMs }; break;
                case "Allen-Bradley EtherNet/IP": device = new AllenBradleyNet(config.Host, config.Port) { Slot = (byte)config.Slot, ConnectTimeOut = config.ConnectTimeoutMs, ReceiveTimeOut = config.ReceiveTimeoutMs }; break;
                default: throw new NotSupportedException("不支持的工业协议：" + config.Protocol);
            }
            if (!serial)
            {
                dynamic network = device; OperateResult result = network.ConnectServer(); if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            }
            return new Session { Device = device, IsSerial = serial };
        }

        private static object ConvertValue(object value, string dataType)
        {
            var text = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
            switch (dataType)
            {
                case "Bool": bool boolean; return bool.TryParse(text, out boolean) ? boolean : Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
                case "Byte": return Convert.ToByte(value, CultureInfo.InvariantCulture);
                case "Int16": return Convert.ToInt16(value, CultureInfo.InvariantCulture);
                case "UInt16": return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                case "Int32": return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                case "UInt32": return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                case "Int64": return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case "UInt64": return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                case "Float": return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                case "Double": return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case "BoolArray": return ConvertArray(value, ConvertBoolean);
                case "ByteArray": return value is byte[] ? value : ParseBytes(text);
                case "Int16Array": return ConvertArray(value, x => Convert.ToInt16(x, CultureInfo.InvariantCulture));
                case "UInt16Array": return ConvertArray(value, x => Convert.ToUInt16(x, CultureInfo.InvariantCulture));
                case "Int32Array": return ConvertArray(value, x => Convert.ToInt32(x, CultureInfo.InvariantCulture));
                case "UInt32Array": return ConvertArray(value, x => Convert.ToUInt32(x, CultureInfo.InvariantCulture));
                case "Int64Array": return ConvertArray(value, x => Convert.ToInt64(x, CultureInfo.InvariantCulture));
                case "UInt64Array": return ConvertArray(value, x => Convert.ToUInt64(x, CultureInfo.InvariantCulture));
                case "FloatArray": return ConvertArray(value, x => Convert.ToSingle(x, CultureInfo.InvariantCulture));
                case "DoubleArray": return ConvertArray(value, x => Convert.ToDouble(x, CultureInfo.InvariantCulture));
                default: return text;
            }
        }

        private static bool ConvertBoolean(object value)
        {
            bool boolean; var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return bool.TryParse(text, out boolean) ? boolean : Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
        }

        private static T[] ConvertArray<T>(object value, Func<object, T> converter)
        {
            var array = value as Array;
            if (array != null)
            {
                var result = new T[array.Length];
                for (var index = 0; index < array.Length; index++) result[index] = converter(array.GetValue(index));
                return result;
            }
            var parts = Convert.ToString(value, CultureInfo.InvariantCulture).Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(x => converter(x)).ToArray();
        }

        private static byte[] ParseBytes(string value)
        {
            var clean = (value ?? string.Empty).Replace("0x", string.Empty).Replace("-", " ").Replace(",", " ");
            return clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => byte.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
        }

        public static bool TryGetJsonPathValue(string json, string path, out object value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(path)) return false;
            object current;
            try { current = new JavaScriptSerializer().DeserializeObject(json); }
            catch { return false; }
            foreach (var token in ParseJsonPath(path))
            {
                var property = token as string;
                if (property != null)
                {
                    var dictionary = current as IDictionary<string, object>;
                    if (dictionary == null) return false;
                    object next;
                    if (!dictionary.TryGetValue(property, out next))
                    {
                        var match = dictionary.FirstOrDefault(x => string.Equals(x.Key, property, StringComparison.OrdinalIgnoreCase));
                        if (match.Key == null) return false;
                        next = match.Value;
                    }
                    current = next;
                }
                else
                {
                    var index = (int)token;
                    var array = current as object[];
                    if (array == null || index < 0 || index >= array.Length) return false;
                    current = array[index];
                }
            }
            value = current; return true;
        }

        public static string ExpandJsonTemplate(string template, string requestJson, Func<string, object> runtimeValueProvider = null)
        {
            var source = template ?? string.Empty;
            var builder = new StringBuilder(); var start = 0;
            while (true)
            {
                var open = source.IndexOf("{{", start, StringComparison.Ordinal);
                if (open < 0) { builder.Append(source, start, source.Length - start); break; }
                var close = source.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (close < 0) { builder.Append(source, start, source.Length - start); break; }
                builder.Append(source, start, open - start);
                var token = source.Substring(open + 2, close - open - 2).Trim();
                object value;
                if (token.StartsWith("Context:", StringComparison.OrdinalIgnoreCase))
                    value = runtimeValueProvider == null ? null : runtimeValueProvider(token.Substring(8));
                else if (!TryGetJsonPathValue(requestJson, token, out value))
                    value = null;
                builder.Append(JsonValueToText(value));
                start = close + 2;
            }
            return builder.ToString();
        }

        private static object BuildJsonDocument(IEnumerable<CommunicationJsonField> fields)
        {
            var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); var count = 0;
            foreach (var field in fields ?? Enumerable.Empty<CommunicationJsonField>())
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Path)) continue;
                SetJsonPathValue(root, field.Path.Trim(), ConvertJsonValue(field.Value, field.DataType)); count++;
            }
            if (count == 0) throw new InvalidOperationException("没有可发送的 JSON 字段");
            return root;
        }

        private static void SetJsonPathValue(IDictionary<string, object> root, string path, object value)
        {
            var tokens = ParseJsonPath(path).ToArray();
            if (tokens.Length == 0 || tokens.Any(x => !(x is string))) throw new InvalidOperationException("发送 JSON 路径只支持点号属性，例如 Data.Center.X：" + path);
            IDictionary<string, object> current = root;
            for (var index = 0; index < tokens.Length; index++)
            {
                var name = (string)tokens[index];
                if (index == tokens.Length - 1) { current[name] = value; return; }
                object child; IDictionary<string, object> dictionary;
                if (!current.TryGetValue(name, out child)) { dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); current[name] = dictionary; }
                else
                {
                    dictionary = child as IDictionary<string, object>;
                    if (dictionary == null) throw new InvalidOperationException("JSON 路径与已有标量字段冲突：" + path);
                }
                current = dictionary;
            }
        }

        private static IEnumerable<object> ParseJsonPath(string path)
        {
            var clean = (path ?? string.Empty).Trim();
            if (clean.StartsWith("$.", StringComparison.Ordinal)) clean = clean.Substring(2);
            else if (clean == "$") yield break;
            var index = 0;
            while (index < clean.Length)
            {
                if (clean[index] == '.') { index++; continue; }
                if (clean[index] == '[')
                {
                    var close = clean.IndexOf(']', index + 1); int arrayIndex;
                    if (close < 0 || !int.TryParse(clean.Substring(index + 1, close - index - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out arrayIndex))
                        throw new InvalidOperationException("无效的 JSON 数组路径：" + path);
                    yield return arrayIndex; index = close + 1; continue;
                }
                var end = index;
                while (end < clean.Length && clean[end] != '.' && clean[end] != '[') end++;
                var property = clean.Substring(index, end - index).Trim();
                if (property.Length == 0) throw new InvalidOperationException("无效的 JSON 路径：" + path);
                yield return property; index = end;
            }
        }

        private static object ConvertJsonValue(object value, string dataType)
        {
            if (string.Equals(dataType, "Json", StringComparison.OrdinalIgnoreCase) || string.Equals(dataType, "Object", StringComparison.OrdinalIgnoreCase) || string.Equals(dataType, "Array", StringComparison.OrdinalIgnoreCase))
            {
                if (value == null) return null;
                var textValue = value as string;
                if (textValue == null) return value;
                var text = textValue.Trim();
                if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)) return null;
                return new JavaScriptSerializer().DeserializeObject(text);
            }
            return ConvertValue(value, dataType);
        }

        private static string JsonValueToText(object value)
        {
            if (value == null) return "null";
            var text = value as string; if (text != null) return text;
            if (value is IDictionary<string, object> || value is object[] || value is Array) return new JavaScriptSerializer().Serialize(value);
            if (value is bool) return (bool)value ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static object ResolveRuntimeTemplateValue(string key)
        {
            if (string.Equals((key ?? string.Empty).Trim(), "UtcNow", StringComparison.OrdinalIgnoreCase)) return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (string.Equals((key ?? string.Empty).Trim(), "Now", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            return null;
        }

        private object ResolveTemplateValue(string key)
        {
            var value = RuntimeValueProvider == null ? null : RuntimeValueProvider(key);
            return value ?? ResolveRuntimeTemplateValue(key);
        }

        private static string FormatTcpMessage(string template, object value)
        {
            var formatted = FormatValue(value);
            if (string.IsNullOrWhiteSpace(template)) return formatted;
            return template.IndexOf("{Value}", StringComparison.OrdinalIgnoreCase) >= 0
                ? ReplaceOrdinalIgnoreCase(template, "{Value}", formatted)
                : template + "=" + formatted;
        }

        private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
        {
            var result = new StringBuilder(); var start = 0;
            while (true)
            {
                var index = source.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) { result.Append(source, start, source.Length - start); return result.ToString(); }
                result.Append(source, start, index - start); result.Append(newValue); start = index + oldValue.Length;
            }
        }

        private static string FormatValue(object value)
        {
            var bytes = value as byte[]; if (bytes != null) return BitConverter.ToString(bytes);
            var array = value as Array; if (array == null) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return "[" + string.Join(", ", array.Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))) + "]";
        }

        private static string GetFingerprint(CommunicationDefinition c)
        {
            var autoResponses = string.Join(";", (c.AutoResponses ?? new List<CommunicationAutoResponseDefinition>()).Select(x => string.Join("~", x.Enabled, x.MatchPath, x.MatchMode, x.ExpectedValue, x.ResponseTemplate, x.ConsumeMessage)));
            return string.Join("|", c.Protocol, c.PlcModel, c.Host, c.Port, c.Station, c.Rack, c.Slot, c.SerialPort, c.BaudRate, c.DataBits, c.Parity, c.StopBits, c.ConnectTimeoutMs, c.ReceiveTimeoutMs, c.TextEncoding, c.FrameMode, c.LengthPrefixBytes, c.LengthByteOrder, c.MaxFrameBytes, c.PayloadFormat, c.FieldSeparator, c.SendTerminator, c.ReceiveTerminator, c.MessageTerminator, autoResponses);
        }

        public static string DecodeControlText(string value, string fallback)
        {
            if (value == null) return fallback ?? string.Empty;
            return value.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\0", "\0").Replace("\\\\", "\\");
        }

        private static void Close(Session session)
        {
            if (session == null || session.Device == null) return;
            try
            {
                if (session.IsTcpText) ((TcpTextTransport)session.Device).Dispose();
                else { dynamic device = session.Device; if (session.IsSerial) device.Close(); else device.ConnectClose(); }
            }
            catch { }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (var session in _sessions.Values) Close(session);
                _sessions.Clear();
            }
        }
    }
}

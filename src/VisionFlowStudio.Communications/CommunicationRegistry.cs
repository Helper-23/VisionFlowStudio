using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
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
        public string Message { get; set; }
        public object Value { get; set; }
    }

    public sealed class CommunicationRegistry : IDisposable
    {
        private sealed class Session
        {
            public string Fingerprint;
            public object Device;
            public bool IsSerial;
        }

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        public static readonly string[] Protocols = { "Siemens S7Net", "Mitsubishi MC ASCII", "Modbus TCP", "Modbus RTU", "Omron FINS TCP", "Allen-Bradley EtherNet/IP" };
        public static readonly string[] DataTypes =
        {
            "Bool", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double", "String",
            "BoolArray", "ByteArray", "Int16Array", "UInt16Array", "Int32Array", "UInt32Array", "Int64Array", "UInt64Array", "FloatArray", "DoubleArray"
        };

        public CommunicationOperationResult TestConnection(CommunicationDefinition config)
        {
            try { Invalidate(config == null ? null : config.Name); GetOrCreate(config); return new CommunicationOperationResult { Success = true, Message = config.Name + " 连接成功（" + config.Protocol + "）" }; }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = (config == null ? "通信通道" : config.Name) + " 连接失败：" + ex.Message }; }
        }

        public CommunicationOperationResult Write(CommunicationDefinition config, string address, string dataType, object value)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
                if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("写入地址不能为空");
                var session = GetOrCreate(config); var converted = ConvertValue(value, dataType); dynamic device = session.Device; dynamic data = converted;
                OperateResult result = device.Write(address, data);
                if (!result.IsSuccess) Invalidate(config.Name);
                return new CommunicationOperationResult { Success = result.IsSuccess, Message = result.IsSuccess ? string.Format("{0} 写入成功：{1}={2} ({3})", config.Name, address, FormatValue(converted), dataType) : result.Message };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        public CommunicationOperationResult Read(CommunicationDefinition config, string address, string dataType)
        {
            try
            {
                if (config == null) throw new InvalidOperationException("通信通道不存在");
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
                return new CommunicationOperationResult { Success = result.IsSuccess, Message = result.IsSuccess ? string.Format("{0} 读取成功：{1}={2}", config.Name, address, FormatValue(result.Content)) : result.Message, Value = result.IsSuccess ? result.Content : null };
            }
            catch (Exception ex) { return new CommunicationOperationResult { Success = false, Message = ex.Message }; }
        }

        private Session GetOrCreate(CommunicationDefinition config)
        {
            if (config == null) throw new ArgumentNullException("config");
            Session existing; var fingerprint = GetFingerprint(config);
            if (_sessions.TryGetValue(config.Name ?? string.Empty, out existing) && existing.Fingerprint == fingerprint) return existing;
            if (existing != null) { Close(existing); _sessions.Remove(config.Name ?? string.Empty); }
            var session = Create(config); session.Fingerprint = fingerprint; _sessions[config.Name ?? string.Empty] = session; return session;
        }
        private void Invalidate(string name)
        {
            Session session; var key = name ?? string.Empty;
            if (!_sessions.TryGetValue(key, out session)) return;
            Close(session); _sessions.Remove(key);
        }

        private static Session Create(CommunicationDefinition config)
        {
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

        private static string FormatValue(object value)
        {
            var bytes = value as byte[]; if (bytes != null) return BitConverter.ToString(bytes);
            var array = value as Array; if (array == null) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return "[" + string.Join(", ", array.Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))) + "]";
        }
        private static string GetFingerprint(CommunicationDefinition c) { return string.Join("|", c.Protocol, c.PlcModel, c.Host, c.Port, c.Station, c.Rack, c.Slot, c.SerialPort, c.BaudRate, c.DataBits, c.Parity, c.StopBits, c.ConnectTimeoutMs, c.ReceiveTimeoutMs); }
        private static void Close(Session session) { if (session == null || session.Device == null) return; try { dynamic device = session.Device; if (session.IsSerial) device.Close(); else device.ConnectClose(); } catch { } }
        public void Dispose() { foreach (var session in _sessions.Values) Close(session); _sessions.Clear(); }
    }
}

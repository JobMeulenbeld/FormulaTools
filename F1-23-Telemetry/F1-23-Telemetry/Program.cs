using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using MQTTnet;
using MQTTnet.Client;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet.Server;
using Microsoft.Extensions.Logging;
using static Program;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct PacketHeader
{
    public ushort m_packetFormat;
    public byte m_gameYear;
    public byte m_gameMajorVersion;
    public byte m_gameMinorVersion;
    public byte m_packetVersion;
    public byte m_packetId;
    public ulong m_sessionUID;
    public float m_sessionTime;
    public uint m_frameIdentifier;
    public uint m_overallFrameIdentifier;
    public byte m_playerCarIndex;
    public byte m_secondaryPlayerCarIndex;
}

class TelemetryReceiver
{
    private const int Port = 20777; // F1 UDP telemetry port
    private readonly UdpClient udpClient;
    private readonly IMqttClient mqttClient;
    private static ILogger<F1_23_Telemetry> logger;
    private static readonly Dictionary<byte, string> MqttTopics = new()
    {
        { 0, "f1/telemetry/motion" },
        { 1, "f1/telemetry/session" },
        { 2, "f1/telemetry/lap_data" },
        { 3, "f1/telemetry/event" },
        { 4, "f1/telemetry/participants" },
        { 5, "f1/telemetry/car_setups" },
        { 6, "f1/telemetry/car_telemetry" },
        { 7, "f1/telemetry/car_status" },
        { 8, "f1/telemetry/final_classification" },
        { 9, "f1/telemetry/lobby_info" },
        { 10, "f1/telemetry/car_damage" },
        { 11, "f1/telemetry/session_history" },
        { 12, "f1/telemetry/tyre_sets" },
        { 13, "f1/telemetry/motion_ex" }
    };

    public TelemetryReceiver(IMqttClient mqttClient, ILogger<F1_23_Telemetry> _logger)
    {
        logger = _logger;
        udpClient = new UdpClient(Port);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        this.mqttClient = mqttClient;
        logger.LogError($"Listening for telemetry data on port {Port}");
    }

    public void Start()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, Port);

        while (true)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEP); // Receive UDP packet
                PacketHeader header = Deserialize<PacketHeader>(data);

                //Console.WriteLine($"Received Packet ID: {header.m_packetId}");
                PublishToMqtt(header.m_packetId, data).Wait();
            }
            catch (Exception ex)
            {
                logger.LogError($"Exception:{ex}");
            }
            
        }
    }

    public void Dispose()
    {
        udpClient?.Dispose();
    }

    private async Task PublishToMqtt(byte packetId, byte[] packetData)
    {
        if (!MqttTopics.TryGetValue(packetId, out string topic))
        {
            logger.LogError($"Unknown packet ID: {packetId}, skipping...");
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(packetData)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
            .Build();

        await mqttClient.PublishAsync(message, CancellationToken.None);
    }

    private T Deserialize<T>(byte[] data) where T : struct
    {
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            handle.Free();
        }
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        await Host.CreateDefaultBuilder(args)
        .UseWindowsService() // If running as a Windows Service
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders(); // Clear default providers
            logging.AddConsole(); // Log to the console
            logging.AddDebug(); // Log to Visual Studio debug output
            logging.AddEventLog(settings =>
            {
                settings.SourceName = "F1_23_Telemetry";
            });
        })
        .ConfigureServices(services =>
        {
            services.AddHostedService<F1_23_Telemetry>(); // Register your service
        })
        .Build()
        .RunAsync();
    }

    public class F1_23_Telemetry : IHostedService
    {
        private IMqttClient mqttClient;
        private CancellationTokenSource _cancellationTokenSource;
        private static ILogger<F1_23_Telemetry> _logger;
        private TelemetryReceiver receiver;

        public F1_23_Telemetry(ILogger<F1_23_Telemetry> logger)
        {
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Set up MQTT client
            var mqttFactory = new MqttFactory();
            mqttClient = mqttFactory.CreateMqttClient();

            var mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("localhost", 1883) // Replace with your broker details
                .Build();

            await mqttClient.ConnectAsync(mqttOptions, CancellationToken.None);
            _logger.LogError("Connected to MQTT broker.");

            // Start telemetry receiver
            receiver = new TelemetryReceiver(mqttClient, _logger);
            receiver.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            receiver?.Dispose();
            if (mqttClient != null)
            {
                await mqttClient.DisconnectAsync();
                _logger.LogError("Disconnected from MQTT broker.");
            }
            _cancellationTokenSource?.Cancel();
        }

    }
}

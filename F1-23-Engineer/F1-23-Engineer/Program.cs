using System;
using System.Buffers.Binary;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.ServiceProcess;
using static System.Runtime.InteropServices.JavaScript.JSType;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet.Server;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Windows.Markup;


namespace CarTelemetryReader
{
    public class PacketHeader
    {
        public ushort PacketFormat { get; set; }         // 2023
        public byte GameYear { get; set; }              // Game year - last two digits e.g. 23
        public byte GameMajorVersion { get; set; }      // Game major version - "X.00"
        public byte GameMinorVersion { get; set; }      // Game minor version - "1.XX"
        public byte PacketVersion { get; set; }         // Version of this packet type, all start from 1
        public byte PacketId { get; set; }              // Identifier for the packet type, see below
        public ulong SessionUID { get; set; }           // Unique identifier for the session
        public float SessionTime { get; set; }          // Session timestamp
        public uint FrameIdentifier { get; set; }       // Identifier for the frame the data was retrieved on
        public uint OverallFrameIdentifier { get; set; }// Overall identifier for the frame the data was retrieved
        public byte PlayerCarIndex { get; set; }        // Index of player's car in the array
        public byte SecondaryPlayerCarIndex { get; set; }// Index of secondary player's car in the array

        public static PacketHeader Decode(byte[] data)
        {
            if (data.Length < 29)
                throw new ArgumentException("Data too short to decode PacketHeader");

            var header = new PacketHeader
            {
                PacketFormat = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)),
                GameYear = data[2],
                GameMajorVersion = data[3],
                GameMinorVersion = data[4],
                PacketVersion = data[5],
                PacketId = data[6],
                SessionUID = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(7, 8)),
                SessionTime = BitConverter.ToSingle(data, 15),
                FrameIdentifier = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(19, 4)),
                OverallFrameIdentifier = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(23, 4)),
                PlayerCarIndex = data[27],
                SecondaryPlayerCarIndex = data[28]
            };

            return header;
        }
    }

    public class CarTelemetryData
    {
        public ushort Speed { get; set; }
        public float Throttle { get; set; }
        public float Steer { get; set; }
        public float Brake { get; set; }
        public byte Clutch { get; set; }
        public sbyte Gear { get; set; }
        public ushort EngineRPM { get; set; }
        public byte Drs { get; set; }
        public byte RevLightsPercent { get; set; }
        public ushort RevLightsBitValue { get; set; }
        public ushort[] BrakesTemperature { get; set; } = new ushort[4];
        public byte[] TyresSurfaceTemperature { get; set; } = new byte[4];
        public byte[] TyresInnerTemperature { get; set; } = new byte[4];
        public ushort EngineTemperature { get; set; }
        public float[] TyresPressure { get; set; } = new float[4];
        public byte[] SurfaceType { get; set; } = new byte[4];

        public static CarTelemetryData Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < 60)
                throw new ArgumentException("Data too short to decode CarTelemetryData");

            var telemetry = new CarTelemetryData
            {
                Speed = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0, 2)),
                Throttle = BitConverter.ToSingle(data.Slice(2, 4)),
                Steer = BitConverter.ToSingle(data.Slice(6, 4)),
                Brake = BitConverter.ToSingle(data.Slice(10, 4)),
                Clutch = data[14],
                Gear = (sbyte)data[15],
                EngineRPM = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(16, 2)),
                Drs = data[18],
                RevLightsPercent = data[19],
                RevLightsBitValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(20, 2)),
                EngineTemperature = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(38, 2))
            };

            for (int i = 0; i < 4; i++)
            {
                telemetry.BrakesTemperature[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(22 + i * 2, 2));
                telemetry.TyresSurfaceTemperature[i] = data[30 + i];
                telemetry.TyresInnerTemperature[i] = data[34 + i];
                telemetry.TyresPressure[i] = BitConverter.ToSingle(data.Slice(40 + i * 4, 4));
                telemetry.SurfaceType[i] = data[56 + i];
            }

            return telemetry;
        }
    }

    public class PacketCarTelemetryData
    {
        public PacketHeader Header { get; set; }
        public CarTelemetryData[] carTelemetryData { get; set; } = new CarTelemetryData[22];
        public byte MfdPanelIndex { get; set; }
        public byte MfdPanelIndexSecondaryPlayer { get; set; }
        public sbyte SuggestedGear { get; set; }

        public static PacketCarTelemetryData Decode(byte[] data)
        {
            if (data.Length < 1352)
                throw new ArgumentException("Data too short to decode PacketCarTelemetryData");

            var packet = new PacketCarTelemetryData
            {
                Header = PacketHeader.Decode(data.AsSpan(0, 29).ToArray()),
                MfdPanelIndex = data[1349],
                MfdPanelIndexSecondaryPlayer = data[1350],
                SuggestedGear = (sbyte)data[1351]
            };

            for (int i = 0; i < 22; i++)
            {
                packet.carTelemetryData[i] = CarTelemetryData.Decode(data.AsSpan(29 + i * 60, 60));
            }

            return packet;
        }
    }

    public class CarMotionData
    {
        public float WorldPositionX { get; set; }       // World space X position - metres
        public float WorldPositionY { get; set; }       // World space Y position
        public float WorldPositionZ { get; set; }       // World space Z position
        public float WorldVelocityX { get; set; }       // Velocity in world space X – metres/s
        public float WorldVelocityY { get; set; }       // Velocity in world space Y
        public float WorldVelocityZ { get; set; }       // Velocity in world space Z
        public short WorldForwardDirX { get; set; }     // World space forward X direction (normalised)
        public short WorldForwardDirY { get; set; }     // World space forward Y direction (normalised)
        public short WorldForwardDirZ { get; set; }     // World space forward Z direction (normalised)
        public short WorldRightDirX { get; set; }       // World space right X direction (normalised)
        public short WorldRightDirY { get; set; }       // World space right Y direction (normalised)
        public short WorldRightDirZ { get; set; }       // World space right Z direction (normalised)
        public float GForceLateral { get; set; }        // Lateral G-Force component
        public float GForceLongitudinal { get; set; }   // Longitudinal G-Force component
        public float GForceVertical { get; set; }       // Vertical G-Force component
        public float Yaw { get; set; }                  // Yaw angle in radians
        public float Pitch { get; set; }                // Pitch angle in radians
        public float Roll { get; set; }                 // Roll angle in radians

        public static CarMotionData Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < 60) // Each CarMotionData is 60 bytes
                throw new ArgumentException("Data too short to decode CarMotionData");

            var carMotionData = new CarMotionData
            {
                WorldPositionX = BitConverter.ToSingle(data.Slice(0, 4)),
                WorldPositionY = BitConverter.ToSingle(data.Slice(4, 4)),
                WorldPositionZ = BitConverter.ToSingle(data.Slice(8, 4)),
                WorldVelocityX = BitConverter.ToSingle(data.Slice(12, 4)),
                WorldVelocityY = BitConverter.ToSingle(data.Slice(16, 4)),
                WorldVelocityZ = BitConverter.ToSingle(data.Slice(20, 4)),
                WorldForwardDirX = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(24, 2)),
                WorldForwardDirY = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(26, 2)),
                WorldForwardDirZ = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(28, 2)),
                WorldRightDirX = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(30, 2)),
                WorldRightDirY = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(32, 2)),
                WorldRightDirZ = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(34, 2)),
                GForceLateral = BitConverter.ToSingle(data.Slice(36, 4)),
                GForceLongitudinal = BitConverter.ToSingle(data.Slice(40, 4)),
                GForceVertical = BitConverter.ToSingle(data.Slice(44, 4)),
                Yaw = BitConverter.ToSingle(data.Slice(48, 4)),
                Pitch = BitConverter.ToSingle(data.Slice(52, 4)),
                Roll = BitConverter.ToSingle(data.Slice(56, 4)),
            };

            return carMotionData;
        }
    }

    public class PacketMotionData
    {
        public PacketHeader Header { get; set; } = new PacketHeader();
        public CarMotionData[] CarMotionDataArray { get; set; } = new CarMotionData[22];

        public static PacketMotionData Decode(byte[] data)
        {
            if (data.Length < 1343) // PacketMotionData size (22 cars * 60 bytes + 29 bytes header)
                throw new ArgumentException("Data too short to decode PacketMotionData");

            var packetMotionData = new PacketMotionData
            {
                Header = PacketHeader.Decode(data.AsSpan(0, 29).ToArray())
            };

            for (int i = 0; i < 22; i++)
            {
                var offset = 29 + (i * 60); // 29 bytes for the header, then 60 bytes per car
                packetMotionData.CarMotionDataArray[i] = CarMotionData.Decode(data.AsSpan(offset, 60));
            }

            return packetMotionData;
        }
    }

    public class LapData
    {
        public uint LastLapTimeInMS { get; set; }
        public uint CurrentLapTimeInMS { get; set; }
        public ushort Sector1TimeInMS { get; set; }
        public byte Sector1TimeMinutes { get; set; }
        public ushort Sector2TimeInMS { get; set; }
        public byte Sector2TimeMinutes { get; set; }
        public ushort DeltaToCarInFrontInMS { get; set; }
        public ushort DeltaToRaceLeaderInMS { get; set; }
        public float LapDistance { get; set; }
        public float TotalDistance { get; set; }
        public float SafetyCarDelta { get; set; }
        public byte CarPosition { get; set; }
        public byte CurrentLapNum { get; set; }
        public byte PitStatus { get; set; }
        public byte NumPitStops { get; set; }
        public byte Sector { get; set; }
        public byte CurrentLapInvalid { get; set; }
        public byte Penalties { get; set; }
        public byte TotalWarnings { get; set; }
        public byte CornerCuttingWarnings { get; set; }
        public byte NumUnservedDriveThroughPens { get; set; }
        public byte NumUnservedStopGoPens { get; set; }
        public byte GridPosition { get; set; }
        public byte DriverStatus { get; set; }
        public byte ResultStatus { get; set; }
        public byte PitLaneTimerActive { get; set; }
        public ushort PitLaneTimeInLaneInMS { get; set; }
        public ushort PitStopTimerInMS { get; set; }
        public byte PitStopShouldServePen { get; set; }

        public static LapData Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < 50)
                throw new ArgumentException("Data too short to decode LapData");

            var lapData = new LapData
            {
                LastLapTimeInMS = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)),
                CurrentLapTimeInMS = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)),
                Sector1TimeInMS = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
                Sector1TimeMinutes = data[10],
                Sector2TimeInMS = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(11, 2)),
                Sector2TimeMinutes = data[13],
                DeltaToCarInFrontInMS = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14, 2)),
                DeltaToRaceLeaderInMS = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(16, 2)),
                LapDistance = BitConverter.ToSingle(data.Slice(18, 4)),
                TotalDistance = BitConverter.ToSingle(data.Slice(22, 4)),
                SafetyCarDelta = BitConverter.ToSingle(data.Slice(26, 4)),
                CarPosition = data[30],
                CurrentLapNum = data[31],
                PitStatus = data[32],
                NumPitStops = data[33],
                Sector = data[34],
                CurrentLapInvalid = data[35],
                Penalties = data[36],
                TotalWarnings = data[37],
                CornerCuttingWarnings = data[38],
                NumUnservedDriveThroughPens = data[39],
                NumUnservedStopGoPens = data[40],
                GridPosition = data[41],
                DriverStatus = data[42],
                ResultStatus = data[43],
                PitLaneTimerActive = data[44],
                PitLaneTimeInLaneInMS = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(45, 2)),
                PitStopTimerInMS = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(47, 2)),
                PitStopShouldServePen = data[49]
            };

            return lapData;
        }
    }

    public class PacketLapData
    {
        public PacketHeader Header { get; set; }
        public LapData[] lapData { get; set; } = new LapData[22];
        public byte TimeTrialPBCarIdx { get; set; }
        public byte TimeTrialRivalCarIdx { get; set; }

        public static PacketLapData Decode(byte[] data)
        {
            if (data.Length < 1131)
                throw new ArgumentException("Data too short to decode PacketLapData");

            var packet = new PacketLapData
            {
                Header = PacketHeader.Decode(data.AsSpan(0, 29).ToArray()),
                TimeTrialPBCarIdx = data[1129],
                TimeTrialRivalCarIdx = data[1130]
            };

            for (int i = 0; i < 22; i++)
            {
                packet.lapData[i] = LapData.Decode(data.AsSpan(29 + i * 50, 50));
            }

            return packet;
        }
    }

    public class PacketSessionData
    {
        public PacketHeader Header { get; set; }
        public byte Weather { get; set; }
        public sbyte TrackTemperature { get; set; }
        public sbyte AirTemperature { get; set; }
        public byte TotalLaps { get; set; }
        public ushort TrackLength { get; set; }
        public byte SessionType { get; set; }
        public sbyte TrackId { get; set; }
        public byte Formula { get; set; }
        public ushort SessionTimeLeft { get; set; }
        public ushort SessionDuration { get; set; }
        public byte PitSpeedLimit { get; set; }
        public byte GamePaused { get; set; }
        public byte IsSpectating { get; set; }
        public byte SpectatorCarIndex { get; set; }
        public byte SliProNativeSupport { get; set; }
        public byte NumMarshalZones { get; set; }
        public MarshalZone[] MarshalZones { get; set; } = new MarshalZone[22];
        public byte SafetyCarStatus { get; set; }
        public byte NetworkGame { get; set; }
        public byte NumWeatherForecastSamples { get; set; }
        public WeatherForecastSample[] WeatherForecastSamples { get; set; } = new WeatherForecastSample[56];

        public static PacketSessionData Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < 644)
                throw new ArgumentException("Data too short to decode PacketSessionData");

            int offset = 29; //Aleadery accounted for the header decoding


            var packet = new PacketSessionData
            {
                Header = PacketHeader.Decode(data.ToArray()),
                Weather = data[offset],
                TrackTemperature = (sbyte)data[offset + 1],
                AirTemperature = (sbyte)data[offset + 2],
                TotalLaps = data[offset + 3],
                TrackLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 4, 2)),
                SessionType = data[offset + 6],
                TrackId = (sbyte)data[offset + 7],
                Formula = data[offset + 8],
                SessionTimeLeft = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 9, 2)),
                SessionDuration = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 11, 2)),
                PitSpeedLimit = data[offset + 13],
                GamePaused = data[offset + 14],
                IsSpectating = data[offset + 15],
                SpectatorCarIndex = data[offset + 16],
                SliProNativeSupport = data[offset + 17],
                NumMarshalZones = data[offset + 18]
            };

            offset += 19;

            // Parse Marshal Zones
            for (int i = 0; i < 22; i++)
            {
                packet.MarshalZones[i] = new MarshalZone();
                packet.MarshalZones[i].ZoneStart = BitConverter.ToSingle(data.Slice(offset, 4));
                packet.MarshalZones[i].ZoneFlag = (sbyte)data[offset + 4];
                offset += 5;
            }

            packet.SafetyCarStatus = data[offset++];
            packet.NetworkGame = data[offset++];
            packet.NumWeatherForecastSamples = data[offset++];

            // Parse Weather Forecast Samples
            for (int i = 0; i < 56; i++)
            {
                packet.WeatherForecastSamples[i] = new WeatherForecastSample();
                packet.WeatherForecastSamples[i].SessionType = data[offset];
                packet.WeatherForecastSamples[i].TimeOffset = data[offset + 1];
                packet.WeatherForecastSamples[i].Weather = data[offset + 2];
                packet.WeatherForecastSamples[i].TrackTemperature = (sbyte)data[offset + 3];
                packet.WeatherForecastSamples[i].TrackTemperatureChange = (sbyte)data[offset + 4];
                packet.WeatherForecastSamples[i].AirTemperature = (sbyte)data[offset + 5];
                packet.WeatherForecastSamples[i].AirTemperatureChange = (sbyte)data[offset + 6];
                packet.WeatherForecastSamples[i].RainPercentage = data[offset + 7];
                offset += 8;
            }

            // You can continue parsing additional fields as needed here...

            return packet;
        }
    }

    public class MarshalZone
    {
        public float ZoneStart { get; set; }
        public sbyte ZoneFlag { get; set; }
    }

    public class WeatherForecastSample
    {
        public byte SessionType { get; set; }
        public byte TimeOffset { get; set; }
        public byte Weather { get; set; }
        public sbyte TrackTemperature { get; set; }
        public sbyte TrackTemperatureChange { get; set; }
        public sbyte AirTemperature { get; set; }
        public sbyte AirTemperatureChange { get; set; }
        public byte RainPercentage { get; set; }
    }

    public enum SessionType
    {
        Unknown = 0,
        Practice1 = 1,
        Practice2 = 2,
        Practice3 = 3,
        ShortPractice = 4,
        Qualifying1 = 5,
        Qualifying2 = 6,
        Qualifying3 = 7,
        ShortQualifying = 8,
        OneShotQualifying = 9,
        Race = 10,
        Race2 = 11,
        Race3 = 12,
        TimeTrial = 13
    }

    public enum TrackID
    {
        Melbourne = 0,
        PaulRicard = 1,
        Shanghai = 2,
        Sakhir = 3,
        Catalunya = 4,
        Monaco = 5,
        Montreal = 6,
        Silverstone = 7,
        Hockenheim = 8,
        Hungaroring = 9,
        Spa = 10,
        Monza = 11,
        Singapore = 12,
        Suzuka = 13,
        AbuDhabi = 14,
        Texas = 15,
        Brazil = 16,
        Austria = 17,
        Sochi = 18,
        Mexico = 19,
        Baku = 20,
        SakhirShort = 21,
        SilverstoneShort = 22,
        TexasShort = 23,
        SuzukaShort = 24,
        Hanoi = 25,
        Zandvoort = 26,
        Imola = 27,
        Portimao = 28,
        Jeddah = 29,
        Miami = 30,
        LasVegas = 31,
        Losail = 32
    }

    // Custom converter for byte[] to serialize as an array of numbers
    public class ByteArrayConverter : JsonConverter<byte[]>
    {
        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.AsEnumerable());

        public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetBytesFromBase64(),
                JsonTokenType.StartArray => JsonSerializer.Deserialize<List<byte>>(ref reader)!.ToArray(),
                JsonTokenType.Null => null,
                _ => throw new JsonException(),
            };
    }


    public class DataPoint
    {
        public float LapDistance { get; set; } // Distance in meters
        public uint LapTime { get; set; } // Current ms timestamp of the lap
        public CarTelemetryData CarTelemetryData { get; set; }

        public CarMotionData CarMotionData { get; set; }


    }

    public class DataOut
    {
        public uint laptime { get; set; }
        public Dictionary<int, List<DataPoint>> dataPoints { get; set; }
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
                    settings.SourceName = "F1_23_Engineer";
                });
            })
            .ConfigureServices(services =>
            {
                services.AddHostedService<F1_23_Engineer>(); // Register your service
            })
            .Build()
            .RunAsync();
        }
    }

    public class F1_23_Engineer : IHostedService
    {
        // MQTT topic dictionary
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

        public static readonly Dictionary<int, string> Tracks = new Dictionary<int, string>
        {
            { 0, "Melbourne" },
            { 1, "Paul Ricard" },
            { 2, "Shanghai" },
            { 3, "Sakhir (Bahrain)" },
            { 4, "Catalunya" },
            { 5, "Monaco" },
            { 6, "Montreal" },
            { 7, "Silverstone" },
            { 8, "Hockenheim" },
            { 9, "Hungaroring" },
            { 10, "Spa" },
            { 11, "Monza" },
            { 12, "Singapore" },
            { 13, "Suzuka" },
            { 14, "Abu Dhabi" },
            { 15, "Texas" },
            { 16, "Brazil" },
            { 17, "Austria" },
            { 18, "Sochi" },
            { 19, "Mexico" },
            { 20, "Baku (Azerbaijan)" },
            { 21, "Sakhir Short" },
            { 22, "Silverstone Short" },
            { 23, "Texas Short" },
            { 24, "Suzuka Short" },
            { 25, "Hanoi" },
            { 26, "Zandvoort" },
            { 27, "Imola" },
            { 28, "Portimão" },
            { 29, "Jeddah" },
            { 30, "Miami" },
            { 31, "Las Vegas" },
            { 32, "Losail" }
        };

        private static ILogger<F1_23_Engineer> _logger;


        // Buffer to hold unmatched packets
        private static Dictionary<uint, LapData> lapDataBuffer = new Dictionary<uint, LapData>();
        private static Dictionary<uint, CarTelemetryData> telemetryBuffer = new Dictionary<uint, CarTelemetryData>();
        private static Dictionary<uint, CarMotionData> motionBuffer = new Dictionary<uint, CarMotionData>();


        private static int lastProcessedLap = -1; // Keeps track of the last processed lap number
        private static bool use_buffer_1 = true;


        private static Dictionary<int, List<DataPoint>> activeBuffer = new Dictionary<int, List<DataPoint>>();
        private static Dictionary<int, List<DataPoint>> standbyBuffer = new Dictionary<int, List<DataPoint>>();

        public static int sessionType = -1;
        public static int currentTrackID = 0;
        public static int driverStatus = -1;


        private IMqttClient mqttClient;
        private CancellationTokenSource _cancellationTokenSource;

        public static string ToFolderName(SessionType sessionType)
        {
            return Enum.GetName(typeof(SessionType), sessionType) ?? "Unknown";
        }

        private static void SwapBuffers()
        {
            // Swap the references
            var temp = activeBuffer;
            activeBuffer = standbyBuffer;
            standbyBuffer = temp;
        }
        private static void SaveLapToFile(int lapNumber, DataOut bufferToWrite)
        {
            new Thread(() =>
            {
                if (bufferToWrite.dataPoints.ContainsKey(lapNumber))
                {
                    // Get folder name from session type
                    string folderPath = ToFolderName((SessionType)sessionType);
                    string path = $"C:\\Users\\F1_Telemetry\\{Tracks[currentTrackID]}\\{folderPath}";
                    // Create the folder if it doesn't exist
                    Directory.CreateDirectory(path);

                    // Construct the file path
                    string filePath = Path.Combine(path, $"lap_{lapNumber}_data.json");

                    try
                    {
                        var options = new JsonSerializerOptions { Converters = { new ByteArrayConverter() }, WriteIndented = true };
                        // Serialize the lap data and write to a file
                        string json = JsonSerializer.Serialize(bufferToWrite, options);
                        File.WriteAllText(filePath, json);

                        _logger.LogError($"Saved lap {lapNumber} data to {path}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to save lap {lapNumber} data: {ex.Message}");
                    }

                    // Remove the lap data from the buffer after writing
                    lock (bufferToWrite)
                    {
                        bufferToWrite.dataPoints.Remove(lapNumber);
                    }
                }
            })
            {
                IsBackground = true // Ensure the thread terminates with the application
            }.Start();
        }

        private static void MatchAndProcessPackets(UInt32 frameIdentifier)
        {
            if (lapDataBuffer.ContainsKey(frameIdentifier) && telemetryBuffer.ContainsKey(frameIdentifier) && motionBuffer.ContainsKey(frameIdentifier))
            {
                var lapData = lapDataBuffer[frameIdentifier];
                var telemetryData = telemetryBuffer[frameIdentifier];
                var motionData = motionBuffer[frameIdentifier];

                // Determine the lap number
                int currentLap = lapData.CurrentLapNum;

                var dPoint = new DataPoint
                {
                    LapDistance = lapData.LapDistance,
                    LapTime = lapData.CurrentLapTimeInMS,
                    CarTelemetryData = telemetryData,
                    CarMotionData = motionData,
                };

                // Check if we are moving to a new lap
                if (currentLap != lastProcessedLap && lastProcessedLap != -1)
                {
                    DataOut data = new DataOut();
                    data.laptime = lapData.LastLapTimeInMS;
                    data.dataPoints = activeBuffer;

                    // Save the completed lap's data from the active buffer
                    SaveLapToFile(lastProcessedLap, data);

                    // Swap the buffers
                    SwapBuffers();

                    // Clear the new active buffer
                    activeBuffer.Clear();
                }

                // Store data for the current lap in the active buffer
                if (!activeBuffer.ContainsKey(currentLap))
                {
                    activeBuffer[currentLap] = new List<DataPoint>();
                }

                activeBuffer[currentLap].Add(dPoint);

                // Update the last processed lap
                lastProcessedLap = currentLap;

                // Remove processed packets from buffers
                lapDataBuffer.Remove(frameIdentifier);
                telemetryBuffer.Remove(frameIdentifier);
            }
        }


        public F1_23_Engineer(ILogger<F1_23_Engineer> logger)
        {
            _logger = logger;
        }

        private Task OnMqttMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            
            ArraySegment<byte> payload = e.ApplicationMessage.PayloadSegment;
            try
            {
                var header = PacketHeader.Decode(payload.Array);

                switch (header.PacketId)
                {
                    case 0:
                        var motionData = PacketMotionData.Decode(payload.Array);
                        if (driverStatus > 0)
                        {
                            motionBuffer.Add(header.FrameIdentifier, motionData.CarMotionDataArray[header.PlayerCarIndex]);
                            MatchAndProcessPackets(header.FrameIdentifier);
                        }
                        break;
                    case 1:
                        var sessionData = PacketSessionData.Decode(payload.Array);
                        sessionType = sessionData.SessionType;
                        currentTrackID = sessionData.TrackId;
                        break;
                    case 2:
                        if (sessionType >= 0)
                        {
                            var lapdata = PacketLapData.Decode(payload.Array);
                            driverStatus = lapdata.lapData[header.PlayerCarIndex].DriverStatus;

                            if (driverStatus > 0)
                            {
                                lapDataBuffer.Add(header.FrameIdentifier, lapdata.lapData[header.PlayerCarIndex]);
                                MatchAndProcessPackets(header.FrameIdentifier);
                            }
                        }
                        break;
                    case 6:
                        if (sessionType >= 0)
                        {
                            var telemetry = PacketCarTelemetryData.Decode(payload.Array);

                            if (driverStatus > 0)
                            {
                                telemetryBuffer.Add(header.FrameIdentifier, telemetry.carTelemetryData[header.PlayerCarIndex]);
                                MatchAndProcessPackets(header.FrameIdentifier);
                            }

                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to decode PacketHeader: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            _logger.LogError("Service is starting...");

            

            // Initialize MQTT client
            var mqttFactory = new MqttFactory();
            mqttClient = mqttFactory.CreateMqttClient();

            // MQTT options
            var mqttOptions = new MqttClientOptionsBuilder()
                .WithClientId("CarTelemetryReader")
                .WithTcpServer("localhost", 1883) // Replace with your broker's address if not local
                .Build();

            // Callback function when a message is received
            mqttClient.ApplicationMessageReceivedAsync += OnMqttMessageReceived;

            try
            {
                // Connect to MQTT broker
                await mqttClient.ConnectAsync(mqttOptions, CancellationToken.None);

                foreach (var topic in MqttTopics)
                {
                    await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topic.Value).Build());
                    _logger.LogError($"Subscribing to: {topic}");
                }

                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred: {ex.Message}");
            }

        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (mqttClient != null)
            {
                await mqttClient.DisconnectAsync();
                _logger.LogError("Disconnected from MQTT broker.");
            }

            _cancellationTokenSource?.Cancel();
        }
    }
}

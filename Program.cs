using ConsoleApp1;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScottPlot.Interactivity.UserActions;

var allPackets = AnalyzeAllWavFiles(@"D:\User\Downloads\Thingy\data\");
Console.WriteLine(JsonSerializer.Serialize(allPackets, new JsonSerializerOptions { WriteIndented = true }));

var groupedTimings = GroupByProximity(Singletons.Timings);
Console.WriteLine(JsonSerializer.Serialize(groupedTimings.ToDictionary(x => x.Average(), x => x.Count), new JsonSerializerOptions { WriteIndented = true }));

var timingsCounts = Singletons.Timings
    .GroupBy(x => x)
    .Where(g => g.Key is > 150.0 and < 1700.0)
    .OrderBy(x => x.Key)
    .ToDictionary(g => g.Key, g => g.Count());;
CreateScatterPlot(timingsCounts, @"D:\User\Downloads\Thingy\data\test.png", 1000, 500);

static void CreateScatterPlot(Dictionary<double, int> valueGroup, string saveImagePath, int width, int height)
{
    var xs = valueGroup.Select(x => x.Key).ToArray();
    var ys = valueGroup.Select(x => (double)x.Value).ToArray();

    ScottPlot.Plot plot = new();
    plot.Add.Scatter(xs, ys);
    plot.SavePng(saveImagePath, width, height);
}
static List<List<double>> GroupByProximity(List<double> values)
{
    List<List<double>> byprox = [];

    foreach (var value in values)
    {
        bool found = false;

        foreach (var proxgroup in byprox)
        {
            if (proxgroup.Any(x => Math.Abs(value - x) < 10.0))
            {
                proxgroup.Add(value);
                found = true;
                break;
            }
        }

        if (!found)
        {
            byprox.Add([value]);
        }
    }

    return byprox;
}

static Dictionary<string, List<RfPacket>> AnalyzeAllWavFiles(string folder)
{
    // Validate folder existence
    if (!Directory.Exists(folder))
        throw new DirectoryNotFoundException($"Folder not found: {folder}");

    // Get all WAV files in the folder
    string[] filePaths = Directory.GetFiles(folder, "*.wav", SearchOption.TopDirectoryOnly);

    // Read samples from each WAV file and add to the combined list
    return filePaths.ToDictionary(Path.GetFileName, file =>
        AnalyzeUInt64ToRfPacket(
            AnalyzeRfBitsToNumber(
                AnalyzeTtlToRfBits(
                    AnalyzeWavToTtl(
                        ReadWavFile(
                            file
                            )
                        )
                    )
                )
            ).ToHashSet().ToList()
        );
}

static bool ValidateInvertedNibble(byte value, byte inverted)
{
    if (((value & inverted) & 0xF0) != 0)
    {
        return false;
    }

    byte reversed = (byte)(((inverted & 1) << 3) | ((inverted & 2) << 1) | ((inverted & 4) >> 1) | ((inverted & 8) >> 3));
    byte fixedValue = (byte)((~reversed) & 0xF);
    
    return value == fixedValue;
}
static byte DecodeShifted(byte value)
{
    return value switch
    {
        0b0001 => 0,
        0b0010 => 1,
        0b0100 => 2,
        0b1000 => 3,
        0b1111 => 4,
        _ => throw new Exception("Invalid value")
    };
}
static byte DecodeInvShifted(byte value)
{
    return value switch
    {
        0b0111 => 0,
        0b1011 => 1,
        0b1101 => 2,
        0b1110 => 3,
        0b0000 => 4,
        _ => throw new Exception("Invalid value")
    };
}
static bool ValidateInvShifted(byte valueShifted, byte invertedValue)
{
    var decodedShifted = DecodeShifted(valueShifted);
    var decodedInverted = DecodeInvShifted(invertedValue);
    
    return decodedShifted == decodedInverted;
}

static string IntToBitsString(ulong value)
{
    const int bitCount = sizeof(ulong) * 8;
    
    var sb = new StringBuilder("BINARY: ");
    for (int i = 0; i < bitCount; i++)
    {
        sb.Append((value >> ((bitCount - 1) - i)) & 1);
    }
    return sb.ToString();
}

static IEnumerable<RfPacket> AnalyzeUInt64ToRfPacket(IEnumerable<UInt64> numbers)
{
    foreach (var number in numbers)
    {
        /*
        StringBuilder sb = new StringBuilder("BINARY: ");
        for (int i = 0; i < 64; i++)
        {
            sb.Append((number >> (63 - i)) & 1);
        }
        Console.WriteLine(sb.ToString());
        */

        if ((number & 0x1) != 0) throw new Exception("End should always be zero!");

        byte channelInv = (byte)((number >>  1) & 0xF   );
        byte typeInv    = (byte)((number >>  5) & 0xF   );
        byte intensity  = (byte)((number >>  9) & 0xFF  );
        uint shockerId  = (uint)((number >> 17) & 0xFFFF);
        byte typeShf    = (byte)((number >> 33) & 0xF   );
        byte channelShf = (byte)((number >> 37) & 0xF   );

        if (!ValidateInvertedNibble(channelShf, channelInv) || !ValidateInvertedNibble(typeShf, typeInv))
        {
            Console.WriteLine("Discarding invalid RfPacket");
            continue;
        }

        byte channel = DecodeShifted(channelShf) switch
        {
            3 => 1,
            4 => 2,
            _ => 0xFF
        };

        if (channel == 0xFF)
        {
            Console.WriteLine($"Channel {channelShf} -> {DecodeShifted(channelShf)} ({IntToBitsString(number)})");
            continue;
        }
        
        var type = DecodeShifted(typeShf) switch
        {
            0 => "Shock",
            1 => "Vibrate",
            2 => "Beep",
            3 => "Light",
            _ => null
        };
        
        if (string.IsNullOrEmpty(type))
        {
            Console.WriteLine($"Type {typeShf} -> {DecodeShifted(typeShf)} ({IntToBitsString(number)})");
            continue;
        }

        shockerId <<= 1; // Simulate DB migration

        yield return new RfPacket(shockerId, channel, type, intensity);
    }
}

static IEnumerable<UInt64> AnalyzeRfBitsToNumber(IEnumerable<List<RfBit>> bitsGroups)
{
    foreach (var rfBits in bitsGroups)
    {
        /*
        StringBuilder sb = new StringBuilder("Packet: ");
        foreach (var bit in rfBits)
        {
            sb.Append(bit switch {
                RfBit.Sync => "SYNC",
                RfBit.HighShort => "-",
                RfBit.LowShort => "_",
                RfBit.HighLong => "---",
                RfBit.LowLong => "___",
                RfBit.HighEnd => "---END",
                RfBit.LowEnd => "___END",
                _ => throw new Exception("Inalid bit type!")
            });
        }

        sb.Append(' ');
        sb.Append('(');
        sb.Append(rfBits.Count);
        sb.Append(')');

        Console.WriteLine(sb.ToString());

        if (rfBits is not [ RfBit.Sync, RfBit.LowLong, .. ])
        {
            throw new Exception("Packet must start with SYNC___");
        }
        */
        UInt64 data = 0;

        for (int i = 2; i < rfBits.Count; i +=2)
        {
            var first = rfBits[i];
            var second = rfBits[i + 1];

            switch (first)
            {
                case RfBit.HighLong when second == RfBit.LowShort:
                    data = (data << 1) | 1; // Push 1
                    continue;
                case RfBit.HighShort when second is RfBit.LowLong or RfBit.LowEnd:
                    data <<= 1; // Push 0
                    continue;
                default:
                    throw new Exception("Invalid RfBit combination");
            }
        }

        yield return data;
    }

    yield break;
}

static IEnumerable<List<RfBit>> AnalyzeTtlToRfBits(IEnumerable<TtlSignal> signals)
{
    bool processing = false;
    List<RfBit> bits = [];
    
    foreach (var signal in signals)
    {
        Singletons.Timings.Add(signal.durationUs);
        
        if (signal.durationUs > 1700.0)
        {
            if (bits.Count > 0)
            {
                bits.Add(signal.level ? RfBit.HighEnd : RfBit.LowEnd);
                yield return bits;
                bits.Clear();
            }

            processing = false;
            continue;
        }
        if (signal is { level: true, durationUs: > 1500.0 })
        {
            bits.Clear();
            bits.Add(RfBit.Sync);

            processing = true;
            continue;
        }

        if (!processing) continue;

        if (signal.durationUs is > 1000.0 or (> 500.0 and < 550.0) or < 100.0)
        {
            Console.WriteLine($"Discarded due to invalid duration ({signal.durationUs}), total discarded: ({bits.Count})");
            bits.Clear();

            processing = false;
            continue;
        }

        bits.Add(signal switch
        {
            { level: true, durationUs: <= 500.0 } => RfBit.HighShort,
            { level: false, durationUs: <= 500.0 } => RfBit.LowShort,
            { level: true, durationUs: > 500.0 } => RfBit.HighLong,
            { level: false, durationUs: > 500.0 } => RfBit.LowLong,
            _ => throw new Exception("Wtf!")
        });
    }
}

static IEnumerable<TtlSignal> AnalyzeWavToTtl(WavFile file)
{
    if (file.samples.Length == 0) yield break;

    const float threshold = 0.5f; // Adjust this threshold to match the TTL signal level
    int sampleCount = file.samples.Length;
    int currentStartSample = 0;

    // Iterate through the samples to detect signal changes
    for (var i = 1; i < sampleCount; i++)
    {
        // Check if we have a transition
        bool isCurrentHigh = file.samples[i] > threshold;
        bool isPreviousHigh = file.samples[i - 1] > threshold;

        // If the state changes (from low to high or high to low), record the signal duration
        if (isCurrentHigh == isPreviousHigh) continue;
        
        // Calculate the duration of the signal
        double durationInSeconds = (i - currentStartSample) / (double)file.sampleRate;
        yield return new TtlSignal(isPreviousHigh, durationInSeconds * 1_000_000.0);

        // Reset the start sample for the next signal
        currentStartSample = i;
    }
}

static WavFile ReadWavFile(string path)
{
    Console.WriteLine($"Reading WAV: {path}");

    // Validate file existence
    if (!File.Exists(path))
        throw new FileNotFoundException($"File not found: {path}");

    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
    using var reader = new BinaryReader(fs);
    
    // Master RIFF chunk
    if (!reader.ValidateCharacters("RIFF")) throw new FormatException("Not a valid WAV file.");
    var fileSize = reader.ReadU32LE();
    if (!reader.ValidateCharacters("WAVE")) throw new FormatException("Not a valid WAV file.");

    if (fileSize == uint.MaxValue)
    {
        reader.BaseStream.Seek(0, SeekOrigin.End);

        var actualLength = reader.BaseStream.Position;
        if (actualLength > UInt32.MaxValue)
        {
            throw new Exception("File is bigger than 4GB!");
        }
        fileSize = (uint)actualLength;

        reader.BaseStream.Seek(12, SeekOrigin.Begin);
    }

    // Chunk describing the data format
    if (!reader.ValidateCharacters("fmt ")) throw new FormatException("Invalid format chunk in WAV file.");
    var fmtChunkSize = reader.ReadU32LE();  // Chunk size minus 8 bytes
    var audioFormat = reader.ReadU16LE();   // 1: PCM integer, 3: IEEE 754 float
    var numChannels = reader.ReadU16LE();   // Number of channels
    var sampleRate = reader.ReadU32LE();    // Sample rate (in hertz)
    var bytesPerSec = reader.ReadU32LE();   // Number of bytes to read per second (Frequency * BytePerBloc)
    var bytesPerBlock = reader.ReadU16LE(); // Number of bytes per block (NbrChannels * BitsPerSample / 8)
    var bitsPerSample = reader.ReadU16LE(); // Number of bits per sample
    if (fmtChunkSize > 16)
        reader.BaseStream.Seek(fmtChunkSize - 16, SeekOrigin.Current);

    if (audioFormat != 3) 
        throw new FormatException("Unsupported WAV format. Only float is supported.");
    if (numChannels != 1)
        throw new FormatException("Unsupported WAV format. Only support single channel.");

    uint otherChunkSizes = 0;

    // Skip unknown chunks
    while (!reader.ValidateCharacters("data"))
    {
        var chunkSize = reader.ReadU32LE();
        reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
        otherChunkSizes += 8 + chunkSize;
    }

    var dataChunkSize = reader.ReadU32LE();
    if (dataChunkSize == uint.MaxValue)
    {
        dataChunkSize = fileSize - (12 + 8 + fmtChunkSize + 8 + otherChunkSizes); // 12 for header, 8 + fmtChunkSize for fmtchunk, 8 for data header, and add other chunk sizes on top
    }

    // Read the samples
    var numSamples = dataChunkSize / sizeof(float);

    float[] samples = new float[numSamples];

    for (int i = 0; i < numSamples; i++)
    {
        samples[i] = reader.ReadSingle();
    }

    float average = samples.Average();
    float averageMax = samples.Where(x => x > average).Average();
    float averageMin = samples.Where(x => x < average).Average();
    float scaling = 1f / MathF.Abs(averageMax - averageMin);

    for (int i = 0; i < samples.Length; i++)
    {
        samples[i] = MathF.Max(averageMin, MathF.Min(averageMax, samples[i])) * scaling;
    }
    
    return new WavFile(sampleRate, samples);
}

internal record WavFile(uint sampleRate, float[] samples);

internal record TtlSignal(bool level, double durationUs);

internal record RfPacket(uint remoteId, byte channel, string commandType, byte commandIntensity);

internal enum RfBit
{
    Sync,
    HighShort,
    HighLong,
    LowShort,
    LowLong,
    HighEnd,
    LowEnd,
};

internal static class Singletons
{
    public static readonly List<double> Timings = [];
}
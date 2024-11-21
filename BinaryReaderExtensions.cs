using System.Reflection;

namespace ConsoleApp1;

internal static class BinaryReaderExtensions
{
    private static byte[] ReadEndianSensitive(this BinaryReader reader, int count, bool littleEndian)
    {
        var bytes = reader.ReadBytes(count);

        if (bytes.Length != count)
            throw new EndOfStreamException($"{count} bytes required from stream, but only {bytes.Length} returned.");

        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    public static bool ValidateCharacters(this BinaryReader reader, ReadOnlySpan<char> chars)
    {
        return chars.SequenceEqual(reader.ReadChars(chars.Length));
    }

    public static UInt16 ReadU16LE(this BinaryReader reader) => BitConverter.ToUInt16(reader.ReadEndianSensitive(sizeof(UInt16), littleEndian: true));
    public static UInt16 ReadU16BE(this BinaryReader reader) => BitConverter.ToUInt16(reader.ReadEndianSensitive(sizeof(UInt16), littleEndian: false));

    public static Int16 ReadI16LE(this BinaryReader reader) => BitConverter.ToInt16(reader.ReadEndianSensitive(sizeof(Int16), littleEndian: true));
    public static Int16 ReadI16BE(this BinaryReader reader) => BitConverter.ToInt16(reader.ReadEndianSensitive(sizeof(Int16), littleEndian: false));

    public static UInt32 ReadU32LE(this BinaryReader reader) => BitConverter.ToUInt32(reader.ReadEndianSensitive(sizeof(UInt32), littleEndian: true));
    public static UInt32 ReadU32BE(this BinaryReader reader) => BitConverter.ToUInt32(reader.ReadEndianSensitive(sizeof(UInt32), littleEndian: false));

    public static Int32 ReadI32LE(this BinaryReader reader) => BitConverter.ToInt32(reader.ReadEndianSensitive(sizeof(Int32), littleEndian: true));
    public static Int32 ReadI32BE(this BinaryReader reader) => BitConverter.ToInt32(reader.ReadEndianSensitive(sizeof(Int32), littleEndian: false));

    public static UInt64 ReadU64LE(this BinaryReader reader) => BitConverter.ToUInt64(reader.ReadEndianSensitive(sizeof(UInt64), littleEndian: true));
    public static UInt64 ReadU64BE(this BinaryReader reader) => BitConverter.ToUInt64(reader.ReadEndianSensitive(sizeof(UInt64), littleEndian: false));

    public static Int64 ReadI64LE(this BinaryReader reader) => BitConverter.ToInt64(reader.ReadEndianSensitive(sizeof(Int64), littleEndian: true));
    public static Int64 ReadI64BE(this BinaryReader reader) => BitConverter.ToInt64(reader.ReadEndianSensitive(sizeof(Int64), littleEndian: false));
}

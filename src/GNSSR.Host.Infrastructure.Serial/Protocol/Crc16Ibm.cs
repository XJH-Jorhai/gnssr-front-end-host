namespace GNSSR.Host.Infrastructure.Serial.Protocol;

public static class Crc16Ibm
{
    public const ushort InitialValue = 0xFFFF;
    public const ushort Polynomial = 0xA001;

    public static ushort Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = InitialValue;

        foreach (var value in bytes)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x0001) != 0
                    ? (ushort)((crc >> 1) ^ Polynomial)
                    : (ushort)(crc >> 1);
            }
        }

        return crc;
    }
}

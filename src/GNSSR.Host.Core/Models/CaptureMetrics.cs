namespace GNSSR.Host.Core.Models;

public sealed class CaptureMetrics
{
    public long BytesReceived { get; set; }

    public long BytesWritten { get; set; }

    public double WriteRateBytesPerSecond { get; set; }

    public int RingBufferUsagePercent { get; set; }

    public int RingBufferPeakPercent { get; set; }

    public CaptureMetrics Clone()
    {
        return new CaptureMetrics
        {
            BytesReceived = BytesReceived,
            BytesWritten = BytesWritten,
            WriteRateBytesPerSecond = WriteRateBytesPerSecond,
            RingBufferUsagePercent = RingBufferUsagePercent,
            RingBufferPeakPercent = RingBufferPeakPercent
        };
    }
}

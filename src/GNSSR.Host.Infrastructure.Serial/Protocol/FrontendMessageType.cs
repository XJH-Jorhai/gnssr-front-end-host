namespace GNSSR.Host.Infrastructure.Serial.Protocol;

public enum FrontendMessageType : byte
{
    HostRequest = 0x01,
    DeviceResponse = 0x02,
    DeviceAsyncEvent = 0x03
}

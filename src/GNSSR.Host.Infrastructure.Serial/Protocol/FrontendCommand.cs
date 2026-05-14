namespace GNSSR.Host.Infrastructure.Serial.Protocol;

public enum FrontendCommand : byte
{
    Hello = 0x01,
    GetStatus = 0x02,
    Ping = 0x03,
    StartStream = 0x10,
    StopStream = 0x11,
    ResetFrontend = 0x12,
    LoadDefaultProfile = 0x13,
    SetCenterFrequency = 0x14,
    Max2769WriteRegister = 0x20,
    Max2769ReadShadow = 0x21,
    Max2769ConfigStatus = 0x22
}

namespace GNSSR.Host.Core.Enums;

public enum AppLifecycleState
{
    Idle,
    Discovering,
    DeviceReady,
    FrontendReady,
    ReadyToCapture,
    StartingCapture,
    Capturing,
    StoppingCapture,
    Error
}

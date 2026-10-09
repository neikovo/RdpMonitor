namespace RdpMonitor.Shared;

public enum RdpEventType
{
    RdpLogonSuccess,
    RdpLogonFailed,
    RdpLogoff,
    RdpDisconnect,
    RdpReconnect,
    SessionLocked,
    SessionUnlocked,
    // Reserved for future process/file audit phase
    ProcessStarted,
    ProcessStopped,
    FileCreated,
    FileModified,
    FileDeleted,
}

public enum AlertSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum AgentStatus
{
    Unknown = 0,
    Online = 1,
    Offline = 2,
}

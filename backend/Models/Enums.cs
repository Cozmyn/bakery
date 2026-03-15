namespace Bakery.Api.Models;

public enum UserRole
{
    Admin = 1,
    Operator = 2
}

public enum RunStatus
{
    Running = 1,
    Stopped = 2,
    Draining = 3,
    Closed = 4
}

public enum PointCode
{
    P1 = 1,
    P2 = 2,
    P3 = 3
}

public enum BatchStatus
{
    Planned = 1,
    Mixed = 2,
    Proofing = 3,
    OnLine = 4,
    Closed = 5
}

public enum BatchDisposition
{
    Used = 1,
    Discarded = 2,
    PartiallyDiscarded = 3
}

public enum WeightConfidence
{
    Low = 1,
    Calibrated = 2
}

public enum PromptStatus
{
    Open = 1,
    Resolved = 2
}

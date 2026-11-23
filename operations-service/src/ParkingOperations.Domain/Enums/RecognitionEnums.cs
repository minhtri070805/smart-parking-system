namespace ParkingOperations.Domain.Enums;

public enum RecognitionPurpose
{
    ENTRY,
    EXIT
}

public enum RecognitionStatus
{
    PENDING_LINK,
    PENDING_CONFIRMATION,
    UNREADABLE,
    CONFIRMED,
    LINKED,
    FAILED
}

namespace recognition.Domain.Enums;

public static class RecognitionPurpose
{
    public const string Entry = "ENTRY";
    public const string Exit = "EXIT";

    public static bool IsValid(string? value) =>
        string.Equals(value, Entry, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Exit, StringComparison.OrdinalIgnoreCase);
}

public static class RecognitionStatus
{
    public const string PendingLink = "PENDING_LINK";
    public const string PendingConfirmation = "PENDING_CONFIRMATION";
    public const string Unreadable = "UNREADABLE";
    public const string Confirmed = "CONFIRMED";
    public const string Linked = "LINKED";
    public const string Failed = "FAILED";
}

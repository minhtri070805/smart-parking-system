namespace API.Configurations;

public sealed class PayOSConfirmationWorkerOptions
{
    public const string SectionName = "PayOSWorker";
    public int ConfirmationPollIntervalSeconds { get; set; } = 60;
}

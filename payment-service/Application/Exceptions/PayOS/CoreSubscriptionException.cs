namespace Application.Exceptions.PayOS;

public sealed class CoreSubscriptionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

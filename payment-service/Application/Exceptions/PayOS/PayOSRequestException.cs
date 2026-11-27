namespace Application.Exceptions.PayOS;

public sealed class PayOSRequestException(int statusCode, string code) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

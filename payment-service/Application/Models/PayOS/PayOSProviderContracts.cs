namespace Application.Models.PayOS;

public sealed record CreatePayOSLinkRequest(long OrderCode, int Amount, string Description, int ExpiredAtUnixSeconds);
public sealed record CreatedPayOSLink(string PaymentLinkId, string CheckoutUrl);
public sealed record VerifiedPayOSWebhook(
    long OrderCode,
    decimal Amount,
    string? Code,
    string? Reference,
    string? PaymentLinkId,
    string? TransactionDateTime);

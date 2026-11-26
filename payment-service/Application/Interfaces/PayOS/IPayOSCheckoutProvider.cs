using Application.Models.PayOS;

namespace Application.Interfaces.PayOS;

public interface IPayOSCheckoutProvider
{
    bool IsConfigured { get; }
    Task<CreatedPayOSLink> CreatePaymentLinkAsync(CreatePayOSLinkRequest request, CancellationToken ct);
    Task<VerifiedPayOSWebhook> VerifyWebhookAsync(string rawBody, CancellationToken ct);
}

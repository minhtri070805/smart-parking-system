using Application.Interfaces.PayOS;
using Application.Models.PayOS;
using Infrastructure.Features.PayOS.Configurations;
using Microsoft.Extensions.Options;
using PayOS.Models.V2.PaymentRequests;
using PayOSClient = PayOS.PayOSClient;

namespace Infrastructure.Features.PayOS.Payments;

public sealed class PayOSCheckoutProvider(IOptions<PayOSOptions> configured) : IPayOSCheckoutProvider
{
    private readonly PayOSOptions options = configured.Value;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ClientId) &&
        !string.IsNullOrWhiteSpace(options.ApiKey) && !string.IsNullOrWhiteSpace(options.ChecksumKey) &&
        !string.IsNullOrWhiteSpace(options.ReturnUrl) && !string.IsNullOrWhiteSpace(options.CancelUrl);

    public async Task<CreatedPayOSLink> CreatePaymentLinkAsync(CreatePayOSLinkRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var client = CreateClient();
        var response = await client.PaymentRequests.CreateAsync(new CreatePaymentLinkRequest
        {
            OrderCode = request.OrderCode,
            Amount = request.Amount,
            Description = request.Description,
            ReturnUrl = options.ReturnUrl,
            CancelUrl = options.CancelUrl,
            ExpiredAt = request.ExpiredAtUnixSeconds
        });
        ct.ThrowIfCancellationRequested();
        return new CreatedPayOSLink(response.PaymentLinkId, response.CheckoutUrl);
    }

    public async Task<VerifiedPayOSWebhook> VerifyWebhookAsync(string rawBody, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var webhook = await CreateClient().Webhooks.VerifyAsync(rawBody);
        ct.ThrowIfCancellationRequested();
        return new VerifiedPayOSWebhook(webhook.OrderCode, webhook.Amount, webhook.Code, webhook.Reference,
            webhook.PaymentLinkId, webhook.TransactionDateTime);
    }

    private PayOSClient CreateClient()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("PayOS credentials are not configured.");
        return new PayOSClient(options.ClientId, options.ApiKey, options.ChecksumKey);
    }
}

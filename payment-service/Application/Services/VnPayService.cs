using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Application.Helpers;
using System.Globalization;

namespace Application.Services
{
    public interface IVnPayService
    {
        string CreatePaymentUrl(HttpContext context, string invoiceId, string sessionId, decimal amount, string? orderInfo = null);
        bool ValidateCallback(IQueryCollection collections);
    }

    public class VnPayService : IVnPayService
    {
        private readonly IConfiguration _config;

        public VnPayService(IConfiguration config)
        {
            _config = config;
        }

        public string CreatePaymentUrl(HttpContext context, string invoiceId, string sessionId, decimal amount, string? orderInfo = null)
        {
            if (amount <= 0 || decimal.Round(amount, 2) != amount)
                throw new ArgumentOutOfRangeException(nameof(amount), "Số tiền thanh toán phải lớn hơn 0 và tối đa 2 chữ số thập phân.");

            var terminalCode = _config["VnPay:TmnCode"];
            var hashSecret = _config["VnPay:HashSecret"];
            var baseUrl = _config["VnPay:BaseUrl"];
            var returnUrl = _config["VnPay:ReturnUrl"];
            if (string.IsNullOrWhiteSpace(terminalCode) || string.IsNullOrWhiteSpace(hashSecret) ||
                string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(returnUrl))
                throw new InvalidOperationException("Thiếu cấu hình VNPay: TmnCode, HashSecret, BaseUrl hoặc ReturnUrl.");

            if (string.Equals(_config["VnPay:Environment"], "Sandbox", StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.StartsWith("https://sandbox.vnpayment.vn/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("VNPay đang ở chế độ Sandbox nhưng BaseUrl không phải máy chủ sandbox.");

            var vnpay = new VnPayLibrary();

            vnpay.AddRequestData("vnp_Version", "2.1.0");
            vnpay.AddRequestData("vnp_Command", "pay");
            vnpay.AddRequestData("vnp_TmnCode", terminalCode);
            vnpay.AddRequestData("vnp_BankCode", "NCB");
            vnpay.AddRequestData("vnp_Amount", (amount * 100m).ToString("0", CultureInfo.InvariantCulture));
            var vietnamTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
            vnpay.AddRequestData("vnp_CreateDate", vietnamTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            vnpay.AddRequestData("vnp_ExpireDate", vietnamTime.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            vnpay.AddRequestData("vnp_CurrCode", "VND");
            var remoteIp = context.Connection.RemoteIpAddress;
            vnpay.AddRequestData("vnp_IpAddr", remoteIp?.IsIPv4MappedToIPv6 == true ? remoteIp.MapToIPv4().ToString() : remoteIp?.ToString() ?? "127.0.0.1");
            vnpay.AddRequestData("vnp_Locale", "vn");
            vnpay.AddRequestData("vnp_OrderInfo", orderInfo ?? $"Thanh toan ve xe cho Session: {sessionId}");
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", returnUrl);
            vnpay.AddRequestData("vnp_TxnRef", invoiceId);

            return vnpay.CreateRequestUrl(baseUrl, hashSecret);
        }

        public bool ValidateCallback(IQueryCollection collections)
        {
            var vnpay = new VnPayLibrary();
            foreach (var (key, value) in collections)
            {
                if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
                {
                    vnpay.AddResponseData(key, value.ToString());
                }
            }

            var secureHash = collections["vnp_SecureHash"].ToString();
            var secret = _config["VnPay:HashSecret"];
            return !string.IsNullOrWhiteSpace(secureHash) && !string.IsNullOrWhiteSpace(secret) &&
                vnpay.ValidateSignature(secureHash, secret);
        }
    }
}

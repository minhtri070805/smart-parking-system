using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Core;

namespace ParkingOperations.Infrastructure.Clients;

public sealed class CoreHttpClient(HttpClient client, ILogger<CoreHttpClient> logger) : ICoreHttpClient
{
    private const string CorrelationHeader = "X-Correlation-Id";

    public async Task<List<AssignedLotSummaryDto>> GetMyAssignedLotsAsync(
        string bearerToken,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw new OperationsException("Bearer token is required to communicate with Core API.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/staff-assignments/my-lots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogWarning("Core rejected authorization token with status: {StatusCode}", response.StatusCode);
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Core API responded with error status: {StatusCode}", response.StatusCode);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<CoreApiResponse<List<AssignedLotSummaryDto>>>(cancellationToken: cancellationToken);
            return result?.Data ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogError(ex, "Failed to call Core API for assigned lots. CorrelationId: {CorrelationId}", correlationId);
            throw new OperationsException("Core service is currently unreachable.");
        }
    }

    public async Task<ValidationContextResult> ValidateStaffAssignmentAsync(
        string bearerToken,
        int lotId,
        string? requiredDuty = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var assignedLots = await GetMyAssignedLotsAsync(bearerToken, correlationId, cancellationToken);
        var assignment = assignedLots.FirstOrDefault(l => l.LotId == lotId && l.IsActive);

        if (assignment is null)
        {
            return new ValidationContextResult(false, $"User is not actively assigned to parking lot {lotId}.");
        }

        if (!string.IsNullOrWhiteSpace(requiredDuty))
        {
            // Duty có thể là GATE, SLOT_VERIFIER, hoặc BOTH
            var userDuty = assignment.Duty?.Trim().ToUpperInvariant() ?? "";
            var reqDuty = requiredDuty.Trim().ToUpperInvariant();

            var dutyMatches = userDuty == "BOTH" || userDuty == reqDuty;
            if (!dutyMatches)
            {
                return new ValidationContextResult(false, $"User duty '{assignment.Duty}' does not have required '{requiredDuty}' duty for lot {lotId}.");
            }
        }

        return new ValidationContextResult(true, Duty: assignment.Duty);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.ShiftManagement;

namespace ParkingOperations.Infrastructure;

public sealed class CoreStaffAccessClient(HttpClient httpClient) : ICoreStaffAccessClient
{
    public async Task<bool> IsCallerAssignedToLotAsync(string bearerToken, int lotId, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/staff-assignments/my-lots", bearerToken);
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Core returned an invalid assigned-lots response.");

        return data.EnumerateArray().Any(x => x.TryGetProperty("lotId", out var id) && id.GetInt32() == lotId);
    }

    public async Task<bool> AreActiveStaffAtLotAsync(string bearerToken, int lotId, long firstStaffId, long secondStaffId, CancellationToken ct)
    {
        var first = IsActiveStaffAtLotAsync(bearerToken, lotId, firstStaffId, ct);
        var second = IsActiveStaffAtLotAsync(bearerToken, lotId, secondStaffId, ct);
        var assignments = await Task.WhenAll(first, second);
        return assignments.All(x => x);
    }

    public async Task<bool> IsActiveStaffAtLotAsync(string bearerToken, int lotId, long staffId, CancellationToken ct)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/staff-assignments?LotId={lotId}&UserId={staffId}&IsActive=true&Page=1&PageSize=1",
            bearerToken);
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return json.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("totalCount", out var total)
            && total.GetInt32() > 0;
    }

    public async Task<IReadOnlyList<StaffOptionDto>> GetActiveStaffAtLotAsync(string bearerToken, int lotId, CancellationToken ct)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/staff-assignments?LotId={lotId}&IsActive=true&Page=1&PageSize=100",
            bearerToken);
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!json.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Core returned an invalid active-staff response.");

        return items.EnumerateArray()
            .Where(x => x.TryGetProperty("userRole", out var role) && role.GetString() == "STAFF")
            .Select(x => new StaffOptionDto(
                x.GetProperty("userId").GetInt64(),
                x.GetProperty("userFullName").GetString() ?? string.Empty))
            .DistinctBy(x => x.UserId)
            .ToList();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }
}

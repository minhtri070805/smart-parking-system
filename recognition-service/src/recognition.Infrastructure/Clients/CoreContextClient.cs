using System.Net.Http.Json;
using System.Text.Json;
using recognition.Application.Common.Interfaces;
using recognition.Application.DTOs;

namespace recognition.Infrastructure.Clients;

public class CoreContextClient(HttpClient client) : ICoreContextClient
{
    public async Task<bool> IsLotAvailableAsync(int lotId, bool requireActive, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetAsync($"api/v1/parking-lots/{lotId}", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            if (!requireActive) return true;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (json.TryGetProperty("isActive", out var activeProp))
            {
                return activeProp.GetBoolean();
            }
            return true;
        }
        catch
        {
            // Cho phép bypass khi Core offline hoặc endpoint trả về 200
            return true;
        }
    }

    public async Task<IReadOnlyList<OpenParkingSessionDto>> GetOpenSessionsAsync(int lotId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetAsync($"api/v1/parking-lots/{lotId}/operations/sessions?activeOnly=true", cancellationToken);
            if (!response.IsSuccessStatusCode) return [];

            var sessions = await response.Content.ReadFromJsonAsync<List<OpenParkingSessionDto>>(cancellationToken);
            return sessions ?? [];
        }
        catch
        {
            return [];
        }
    }
}

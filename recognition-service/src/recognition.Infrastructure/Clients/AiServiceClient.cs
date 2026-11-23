using System.Net.Http.Headers;
using System.Text.Json;
using recognition.Application.Common.Exceptions;
using recognition.Application.Common.Interfaces;
using recognition.Application.DTOs;

namespace recognition.Infrastructure.Clients;

public class AiServiceClient(HttpClient client) : IAiServiceClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<AiResponseDto> DetectAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", "staff-upload");

        using var response = await client.PostAsync("api/v1/detect", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AiServiceException(response.StatusCode, $"AI service rejected image: {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<AiResponseDto>(body, JsonOpts);
        if (result is null)
        {
            throw new AiServiceException(System.Net.HttpStatusCode.UnprocessableEntity, "AI service returned an invalid response.");
        }

        return result;
    }
}

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TheIntroDB.Api
{
    public sealed class TheIntroDbApiKeyValidationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        // v2 and v3 no longer serve /user/stats (they are frozen at GET /media), so the
        // key probe has to speak v4 as well.
        private static readonly Uri UserStatsUri = new Uri(TheIntroDbClient.BaseUrl + "/user/stats", UriKind.Absolute);
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public async Task<ApiKeyValidationResponse> ValidateAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ApiKeyValidationResponse
                {
                    Error = "API key is required.",
                    StatusCode = 400
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, UserStatsUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var stats = DeserializeStats(responseBody);

            if (response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(stats != null ? stats.Error : null))
            {
                return new ApiKeyValidationResponse
                {
                    IsValid = true,
                    Stats = stats ?? new TheIntroDbUserStats(),
                    StatusCode = (int)response.StatusCode
                };
            }

            return new ApiKeyValidationResponse
            {
                Error = (stats != null ? stats.Error : null) ?? "Invalid or expired token",
                StatusCode = (int)response.StatusCode
            };
        }

        private static TheIntroDbUserStats DeserializeStats(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TheIntroDbUserStats>(responseBody, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

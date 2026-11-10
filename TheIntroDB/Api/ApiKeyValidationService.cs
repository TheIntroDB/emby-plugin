// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace TheIntroDB.Api
{
    [Route("/TheIntroDB/Validation/ApiKeyStats", "POST", Summary = "Validate a TheIntroDB API key and return user stats")]
    public sealed class ValidateApiKey : IReturn<ApiKeyValidationResponse>
    {
        public string ApiKey { get; set; }
    }

    public sealed class ApiKeyValidationResponse
    {
        public bool IsValid { get; set; }
        public TheIntroDbUserStats Stats { get; set; }
        public string Error { get; set; }
        public int StatusCode { get; set; }
    }

    public sealed class ApiKeyValidationService : IService
    {
        private readonly TheIntroDbApiKeyValidationService _validationService;

        public ApiKeyValidationService()
        {
            _validationService = new TheIntroDbApiKeyValidationService();
        }

        public async Task<object> Post(ValidateApiKey request)
        {
            return await _validationService.ValidateAsync(request.ApiKey, CancellationToken.None).ConfigureAwait(false);
        }
    }
}

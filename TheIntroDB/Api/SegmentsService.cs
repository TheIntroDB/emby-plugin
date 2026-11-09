// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using TheIntroDB.Data;
using TheIntroDB.Models;

namespace TheIntroDB.Api
{
    public sealed class SegmentsService : IService
    {
        [Route("/TheIntroDB/Segments", "GET", Summary = "Get stored segment data for an item")]
        public sealed class GetSegments : IReturn<List<StoredMediaSegment>>
        {
            [ApiMember(Name = "InternalId", Description = "The internal item id", IsRequired = true, DataType = "long", ParameterType = "query", Verb = "GET")]
            public long InternalId { get; set; }
        }

        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger _logger;

        public SegmentsService(IApplicationPaths applicationPaths, ILogManager logManager)
        {
            _applicationPaths = applicationPaths;
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
        }

        public object Get(GetSegments request)
        {
            using (var repo = new TheIntroDbSegmentRepository(_logger, _applicationPaths))
            {
                var segments = repo.GetSegments(request.InternalId);
                return new List<StoredMediaSegment>(segments);
            }
        }
    }
}


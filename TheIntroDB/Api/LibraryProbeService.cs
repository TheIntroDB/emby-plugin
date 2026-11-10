// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;

namespace TheIntroDB.Api
{
    public sealed class LibraryProbeService : IService
    {
        [Route("/TheIntroDB/Debug/LibraryProbe", "GET", Summary = "Debug: probe library enumeration for TheIntroDB scheduled task")]
        public sealed class LibraryProbeRequest : IReturn<LibraryProbeResponse>
        {
        }

        public sealed class LibraryProbeResponse
        {
            public int RecursiveAllCount { get; set; }
            public int RecursiveTypedCount { get; set; }
            public int RecursiveEpisodesCount { get; set; }
            public int RecursiveMoviesCount { get; set; }
            public List<string> SampleEpisodes { get; set; }
            public List<string> SampleMovies { get; set; }
        }

        private readonly ILibraryManager _libraryManager;

        public LibraryProbeService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public object Get(LibraryProbeRequest request)
        {
            var all = _libraryManager.GetItemList(new InternalItemsQuery { Recursive = true }) ?? Array.Empty<BaseItem>();
            var typed = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { "Movie", "Episode" }
            }) ?? Array.Empty<BaseItem>();

            var episodes = typed.OfType<Episode>().ToArray();
            var movies = typed.OfType<Movie>().ToArray();

            return new LibraryProbeResponse
            {
                RecursiveAllCount = all.Length,
                RecursiveTypedCount = typed.Length,
                RecursiveEpisodesCount = episodes.Length,
                RecursiveMoviesCount = movies.Length,
                SampleEpisodes = episodes.Take(10).Select(e => $"{e.InternalId}:{e.SeriesName} S{e.ParentIndexNumber}E{e.IndexNumber} {e.Name}").ToList(),
                SampleMovies = movies.Take(10).Select(m => $"{m.InternalId}:{m.Name}").ToList()
            };
        }
    }
}


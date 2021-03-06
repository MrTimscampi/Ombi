﻿using AutoMapper;

using Ombi.Api.Trakt;
using Ombi.Api.TvMaze;
using Ombi.Core.Engine.Interfaces;
using Ombi.Core.Models.Requests;
using Ombi.Core.Models.Search;
using Ombi.Core.Settings;
using Ombi.Core.Settings.Models.External;
using Ombi.Store.Repository;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Ombi.Core.Rule.Interfaces;
using Ombi.Store.Repository.Requests;
using Microsoft.Extensions.Caching.Memory;
using Ombi.Core.Authentication;
using Ombi.Helpers;
using Ombi.Settings.Settings.Models;

namespace Ombi.Core.Engine
{
    public class TvSearchEngine : BaseMediaEngine, ITvSearchEngine
    {
        public TvSearchEngine(IPrincipal identity, IRequestServiceMain service, ITvMazeApi tvMaze, IMapper mapper, ISettingsService<PlexSettings> plexSettings,
            ISettingsService<EmbySettings> embySettings, IPlexContentRepository repo, IEmbyContentRepository embyRepo, ITraktApi trakt, IRuleEvaluator r, OmbiUserManager um,
            ICacheService memCache, ISettingsService<OmbiSettings> s)
            : base(identity, service, r, um, memCache, s)
        {
            TvMazeApi = tvMaze;
            Mapper = mapper;
            PlexSettings = plexSettings;
            EmbySettings = embySettings;
            PlexContentRepo = repo;
            TraktApi = trakt;
            EmbyContentRepo = embyRepo;
        }

        private ITvMazeApi TvMazeApi { get; }
        private IMapper Mapper { get; }
        private ISettingsService<PlexSettings> PlexSettings { get; }
        private ISettingsService<EmbySettings> EmbySettings { get; }
        private IPlexContentRepository PlexContentRepo { get; }
        private IEmbyContentRepository EmbyContentRepo { get; }
        private ITraktApi TraktApi { get; }

        public async Task<IEnumerable<SearchTvShowViewModel>> Search(string searchTerm)
        {
            var searchResult = await TvMazeApi.Search(searchTerm);

            if (searchResult != null)
            {
                return await ProcessResults(searchResult);
            }
            return null;
        }

        public async Task<IEnumerable<TreeNode<SearchTvShowViewModel>>> SearchTreeNode(string searchTerm)
        {
            var result = await Search(searchTerm);
            return result.Select(ParseIntoTreeNode).ToList();
        }
        public async Task<SearchTvShowViewModel> GetShowInformation(int tvdbid)
        {
            var show = await TvMazeApi.ShowLookupByTheTvDbId(tvdbid);
            if (show == null)
            { 
                // We don't have enough information
                return null;
            }
            var episodes = await TvMazeApi.EpisodeLookup(show.id);
            if (episodes == null || !episodes.Any())
            {
                // We don't have enough information
                return null;
            }

            var mapped = Mapper.Map<SearchTvShowViewModel>(show);

            foreach (var e in episodes)
            {
                var season = mapped.SeasonRequests.FirstOrDefault(x => x.SeasonNumber == e.season);
                if (season == null)
                {
                    var newSeason = new SeasonRequests
                    {
                        SeasonNumber = e.season,
                        Episodes = new List<EpisodeRequests>()
                    };
                    newSeason.Episodes.Add(new EpisodeRequests
                    {
                        Url = e.url,
                        Title = e.name,
                        AirDate = DateTime.Parse(e.airstamp ?? DateTime.MinValue.ToString()),
                        EpisodeNumber = e.number,

                    });
                    mapped.SeasonRequests.Add(newSeason);
                }
                else
                {
                    // We already have the season, so just add the episode
                    season.Episodes.Add(new EpisodeRequests
                    {
                        Url = e.url,
                        Title = e.name,
                        AirDate = DateTime.Parse(e.airstamp ?? DateTime.MinValue.ToString()),
                        EpisodeNumber = e.number,
                    });
                }
            }
            return await ProcessResult(mapped);
        }

        public async Task<TreeNode<SearchTvShowViewModel>> GetShowInformationTreeNode(int tvdbid)
        {
            var result = await GetShowInformation(tvdbid);
            return ParseIntoTreeNode(result);
        }

        public async Task<IEnumerable<TreeNode<SearchTvShowViewModel>>> PopularTree()
        {
            var result = await Cache.GetOrAdd(CacheKeys.PopularTv, async () => await TraktApi.GetPopularShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed.Select(ParseIntoTreeNode).ToList();
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Popular()
        {
            var result = await Cache.GetOrAdd(CacheKeys.PopularTv, async () => await TraktApi.GetPopularShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed;
        }

                public async Task<IEnumerable<TreeNode<SearchTvShowViewModel>>> AnticipatedTree()
        {
            var result = await Cache.GetOrAdd(CacheKeys.AnticipatedTv, async () => await TraktApi.GetAnticipatedShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed.Select(ParseIntoTreeNode).ToList();
        }
        public async Task<IEnumerable<SearchTvShowViewModel>> Anticipated()
        {

            var result = await Cache.GetOrAdd(CacheKeys.AnticipatedTv, async () => await TraktApi.GetAnticipatedShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed;
        }

        public async Task<IEnumerable<TreeNode<SearchTvShowViewModel>>> MostWatchesTree()
        {
            var result = await Cache.GetOrAdd(CacheKeys.MostWatchesTv, async () => await TraktApi.GetMostWatchesShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed.Select(ParseIntoTreeNode).ToList();
        }
        public async Task<IEnumerable<SearchTvShowViewModel>> MostWatches()
        {
            var result = await Cache.GetOrAdd(CacheKeys.MostWatchesTv, async () => await TraktApi.GetMostWatchesShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed;
        }

        public async Task<IEnumerable<TreeNode<SearchTvShowViewModel>>> TrendingTree()
        {
            var result = await Cache.GetOrAdd(CacheKeys.TrendingTv, async () => await TraktApi.GetTrendingShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed.Select(ParseIntoTreeNode).ToList();
        }

        public async Task<IEnumerable<SearchTvShowViewModel>> Trending()
        {
            var result = await Cache.GetOrAdd(CacheKeys.TrendingTv, async () => await TraktApi.GetTrendingShows(), DateTime.Now.AddHours(12));
            var processed = await ProcessResults(result);
            return processed;
        }

        private static TreeNode<SearchTvShowViewModel> ParseIntoTreeNode(SearchTvShowViewModel result)
        {
            return new TreeNode<SearchTvShowViewModel>
            {
                Data = result,
                Children = new List<TreeNode<SearchTvShowViewModel>>
                {
                    new TreeNode<SearchTvShowViewModel>
                    {
                        Data = result, Leaf = true
                    }
                },
                Leaf = false
            };
        }

        private async Task<IEnumerable<SearchTvShowViewModel>> ProcessResults<T>(IEnumerable<T> items)
        {
            var retVal = new List<SearchTvShowViewModel>();
            foreach (var tvMazeSearch in items)
            {
                var viewT = Mapper.Map<SearchTvShowViewModel>(tvMazeSearch);
                retVal.Add(await ProcessResult(viewT));
            }
            return retVal;
        }

        private async Task<SearchTvShowViewModel> ProcessResult(SearchTvShowViewModel item)
        {
            item.TheTvDbId = item.Id.ToString();
            
            await RunSearchRules(item);

            return item;
        }
    }
}
﻿using JMMContracts.PlexAndKodi;
using JMMServer.Entities;
using System.Collections.Generic;

namespace JMMServer.API.Model.common
{
    public class Episode
    {
        public int id { get; set; }
        public string type { get; set; }
        public ArtCollection art { get; set; }
        public string title { get; set; }
        public string summary { get; set; }
        public string year { get; set; }
        public string air { get; set; }
        public string rating { get; set; }
        public int view { get; set; }
        public int eptype { get; set; }
        public int epnumber { get; set; }
        public List<RawFile> files { get; set; }

        public Episode()
        {

        }

        internal Episode GenerateFromAnimeEpisodeID(int anime_episode_id, int uid, int level)
        {
            Episode ep = new Episode();

            if (anime_episode_id > 0)
            {
                ep = GenerateFromAnimeEpisode(Repositories.RepoFactory.AnimeEpisode.GetByID(anime_episode_id), uid, level);
            }

            return ep;
        }

        internal Episode GenerateFromAnimeEpisode(AnimeEpisode aep, int uid, int level)
        {
            Episode ep = new Episode();
            if (aep != null)
            {
                JMMContracts.Contract_AnimeEpisode cae = aep.GetUserContract(uid);
                if (cae != null)
                {
                    ep.id = aep.AniDB_EpisodeID;
                    ep.art = new ArtCollection();
                    ep.id = aep.AnimeEpisodeID;
                    ep.type = aep.EpisodeTypeEnum.ToString();
                    ep.title = aep.PlexContract?.Title;
                    ep.summary = aep.PlexContract?.Summary;
                    ep.year = aep.PlexContract?.Year;
                    ep.air = aep.PlexContract?.AirDate.ToString();
                    ep.rating = aep.PlexContract?.Rating;
                    double rating;
                    if (double.TryParse(ep.rating, out rating))
                    {
                        ep.rating = (rating / 100).ToString().Replace(',','.');
                    }

                    ep.view = cae.IsWatched;
                    ep.epnumber = cae.EpisodeNumber;
                    ep.eptype = cae.EpisodeType;

                    // until fanart refactor this will be good for start
                    if (aep.PlexContract?.Thumb != null) { ep.art.thumb.Add(new Art() { url = APIHelper.ConstructImageLinkFromRest(aep.PlexContract?.Thumb), index = 0 }); }
                    if (aep.PlexContract?.Art != null) { ep.art.fanart.Add(new Art() { url = APIHelper.ConstructImageLinkFromRest(aep.PlexContract?.Art), index = 0 }); }

                    if (level != 1)
                    {
                        List<VideoLocal> vls = aep.GetVideoLocals();
                        if (vls.Count > 0)
                        {
                            ep.files = new List<RawFile>();
                            foreach (VideoLocal vl in vls)
                            {
                                ep.files.Add(new RawFile(vl, (level-1), uid));
                            }
                        }
                    }
                }
            }

            return ep;
        }
    }
}

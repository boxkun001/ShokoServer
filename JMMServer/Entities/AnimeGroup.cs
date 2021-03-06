﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AniDBAPI;
using JMMContracts;
using JMMContracts.PlexAndKodi;
using JMMServer.Collections;
using JMMServer.Databases;
using JMMServer.LZ4;
using JMMServer.Repositories;
using JMMServer.Repositories.NHibernate;
using NLog;

namespace JMMServer.Entities
{
    public class AnimeGroup
    {
        #region DB Columns

        public int AnimeGroupID { get; private set; }
        public int? AnimeGroupParentID { get; set; }
        public string GroupName { get; set; }
        public string Description { get; set; }
        public int IsManuallyNamed { get; set; }
        public DateTime DateTimeUpdated { get; set; }
        public DateTime DateTimeCreated { get; set; }
        public string SortName { get; set; }
        public DateTime? EpisodeAddedDate { get; set; }
        public DateTime? LatestEpisodeAirDate { get; set; }
        public int MissingEpisodeCount { get; set; }
        public int MissingEpisodeCountGroups { get; set; }
        public int OverrideDescription { get; set; }
        public int? DefaultAnimeSeriesID { get; set; }

        public int ContractVersion { get; set; }
        public byte[] ContractBlob { get; set; }
        public int ContractSize { get; set; }

        #endregion

        public const int CONTRACT_VERSION = 4;


        private static Logger logger = LogManager.GetCurrentClassLogger();


        internal Contract_AnimeGroup _contract = null;

        public virtual Contract_AnimeGroup Contract
        {
            get
            {
                if ((_contract == null) && (ContractBlob != null) && (ContractBlob.Length > 0) && (ContractSize > 0))
                    _contract = CompressionHelper.DeserializeObject<Contract_AnimeGroup>(ContractBlob, ContractSize);
                return _contract;
            }
            set
            {
                _contract = value;
                int outsize;
                ContractBlob = CompressionHelper.SerializeObject(value, out outsize);
                ContractSize = outsize;
                ContractVersion = CONTRACT_VERSION;
            }
        }

        public void CollectContractMemory()
        {
            _contract = null;
        }


        public string GetPosterPathNoBlanks()
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                return GetPosterPathNoBlanks(session.Wrap());
            }
        }

        public string GetPosterPathNoBlanks(ISessionWrapper session)
        {
            List<string> allPosters = GetPosterFilenames(session);
            string posterName = "";
            if (allPosters.Count > 0)
                //posterName = allPosters[fanartRandom.Next(0, allPosters.Count)];
                posterName = allPosters[0];

            if (!String.IsNullOrEmpty(posterName))
                return posterName;

            return "";
        }

        private List<string> GetPosterFilenames()
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                return GetPosterFilenames(session.Wrap());
            }
        }

        private List<string> GetPosterFilenames(ISessionWrapper session)
        {
            List<string> allPosters = new List<string>();

            // check if user has specied a fanart to always be used
            if (DefaultAnimeSeriesID.HasValue)
            {
                AnimeSeries defaultSeries = RepoFactory.AnimeSeries.GetByID(DefaultAnimeSeriesID.Value);
                if (defaultSeries != null)
                {
                    AniDB_Anime anime = defaultSeries.GetAnime();
                    string defPosterPathNoBlanks = anime.GetDefaultPosterPathNoBlanks(session);

                    if (!String.IsNullOrEmpty(defPosterPathNoBlanks) && File.Exists(defPosterPathNoBlanks))
                    {
                        allPosters.Add(defPosterPathNoBlanks);
                        return allPosters;
                    }
                }
            }

            foreach (AnimeSeries ser in GetAllSeries())
            {
                AniDB_Anime anime = ser.GetAnime();
                string defPosterPathNoBlanks = anime.GetDefaultPosterPathNoBlanks(session);

                if (!String.IsNullOrEmpty(defPosterPathNoBlanks) && File.Exists(defPosterPathNoBlanks))
                    allPosters.Add(defPosterPathNoBlanks);
            }

            return allPosters;
        }

        public Contract_AnimeGroup GetUserContract(int userid, HashSet<GroupFilterConditionType> types = null)
        {
            if (Contract == null)
                return new Contract_AnimeGroup();
            Contract_AnimeGroup contract = (Contract_AnimeGroup) Contract.DeepCopy();
            AnimeGroup_User rr = GetUserRecord(userid);
            if (rr != null)
            {
                contract.IsFave = rr.IsFave;
                contract.UnwatchedEpisodeCount = rr.UnwatchedEpisodeCount;
                contract.WatchedEpisodeCount = rr.WatchedEpisodeCount;
                contract.WatchedDate = rr.WatchedDate;
                contract.PlayedCount = rr.PlayedCount;
                contract.WatchedCount = rr.WatchedCount;
                contract.StoppedCount = rr.StoppedCount;
            }
            else if (types != null)
            {
                if (!types.Contains(GroupFilterConditionType.HasUnwatchedEpisodes))
                    types.Add(GroupFilterConditionType.HasUnwatchedEpisodes);
                if (!types.Contains(GroupFilterConditionType.Favourite))
                    types.Add(GroupFilterConditionType.Favourite);
                if (!types.Contains(GroupFilterConditionType.EpisodeWatchedDate))
                    types.Add(GroupFilterConditionType.EpisodeWatchedDate);
                if (!types.Contains(GroupFilterConditionType.HasWatchedEpisodes))
                    types.Add(GroupFilterConditionType.HasWatchedEpisodes);
            }
            return contract;
        }

        public Video GetPlexContract(int userid)
        {
            return GetOrCreateUserRecord(userid).PlexContract;
        }

        private AnimeGroup_User GetOrCreateUserRecord(int userid)
        {
            AnimeGroup_User rr = GetUserRecord(userid);
            if (rr != null)
                return rr;
            rr = new AnimeGroup_User(userid, this.AnimeGroupID);
            rr.WatchedCount = 0;
            rr.UnwatchedEpisodeCount = 0;
            rr.PlayedCount = 0;
            rr.StoppedCount = 0;
            rr.WatchedEpisodeCount = 0;
            rr.WatchedDate = null;
            RepoFactory.AnimeGroup_User.Save(rr);
            return rr;
        }

        public static bool IsRelationTypeInExclusions(string type)
        {
            string[] list = ServerSettings.AutoGroupSeriesRelationExclusions.Split('|');
            foreach (string a in list)
            {
                if (a.ToLowerInvariant().Equals(type.ToLowerInvariant())) return true;
            }
            return false;
        }

        public static List<AnimeGroup> GetRelatedGroupsFromAnimeID(int animeid, bool forceRecursive = false)
        {
            using (var session = DatabaseFactory.SessionFactory.OpenSession())
            {
                return GetRelatedGroupsFromAnimeID(session.Wrap(), animeid, forceRecursive);
            }
        }

        public static List<AnimeGroup> GetRelatedGroupsFromAnimeID(ISessionWrapper session, int animeid,
            bool forceRecursive = false)
        {
            
            List<AnimeGroup> grps = new List<AnimeGroup>();

            AniDB_Anime anime = RepoFactory.AniDB_Anime.GetByAnimeID(session, animeid);
            if (anime == null) return grps;

            // first check for groups which are directly related
            List<AniDB_Anime_Relation> relations = anime.GetRelatedAnime(session);
            //TODO REMOVE sort, missing RelationCompare relations.Sort(RelationCompare);
            foreach (AniDB_Anime_Relation rel in relations)
            {
                string relationtype = rel.RelationType.ToLower();
                if (IsRelationTypeInExclusions(relationtype))
                {
                    //Filter these relations these will fix messes, like Gundam , Clamp, etc.
                    continue;
                }
                AniDB_Anime relAnime = RepoFactory.AniDB_Anime.GetByAnimeID(rel.RelatedAnimeID);
                if (relAnime != null)
                {
                    // Don't include movies or OVA's if excluded
                    if (AnimeGroup.IsRelationTypeInExclusions(relAnime.AnimeTypeDescription.ToLower())) continue;
                }

                // we actually need to get the series, because it might have been added to another group already
                AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(rel.RelatedAnimeID);
                if (ser != null)
                {
                    AnimeGroup grp = RepoFactory.AnimeGroup.GetByID(ser.AnimeGroupID);
                    if (grp != null) grps.Add(grp);
                }
            }
            if (!forceRecursive && grps.Count > 0) return grps;

            // if nothing found check by all related anime
            List<AniDB_Anime> relatedAnime = anime.GetAllRelatedAnime(session);
            foreach (AniDB_Anime rel in relatedAnime)
            {
                // we actually need to get the series, because it might have been added to another group already
                AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(rel.AnimeID);
                if (ser != null)
                {
                    AnimeGroup grp = RepoFactory.AnimeGroup.GetByID(ser.AnimeGroupID);
                    if (grp != null)
                    {
                        if (!grps.Contains(grp)) grps.Add(grp);
                    }
                }
            }

            return grps;
        }

        public AnimeGroup_User GetUserRecord(int userID)
        {
            return RepoFactory.AnimeGroup_User.GetByUserAndGroupID(userID, this.AnimeGroupID);
        }

        public void Populate(AnimeSeries series)
        {
             Populate(series, DateTime.Now);
        }

        public void Populate(AnimeSeries series, DateTime now)
        {
            AniDB_Anime anime = series.GetAnime();

            Populate(anime, now);
        }

        public void Populate(AniDB_Anime anime, DateTime now)
        {
            Description = anime.Description;
            GroupName = anime.PreferredTitle;
            SortName = anime.PreferredTitle;
            DateTimeUpdated = now;
            DateTimeCreated = now;
        }

        public bool HasMissingEpisodesAny
        {
            get { return MissingEpisodeCount > 0 || MissingEpisodeCountGroups > 0; }
        }

        public bool HasMissingEpisodesGroups
        {
            get { return MissingEpisodeCountGroups > 0; }
        }

        public bool HasMissingEpisodes
        {
            get { return MissingEpisodeCountGroups > 0; }
        }

        /*
		public List<string> AnimeTypesList
		{
			get
			{
				List<string> atypeList = new List<string>();
				foreach (AnimeSeries series in GetAllSeries())
				{
					string atype = series.GetAnime().AnimeTypeDescription;
					if (!atypeList.Contains(atype)) atypeList.Add(atype);
				}
				return atypeList;
			}
		}
        */

        /// <summary>
        /// Renames all Anime groups based on the user's language preferences
        /// </summary>
        public static void RenameAllGroups()
        {
            foreach (AnimeGroup grp in RepoFactory.AnimeGroup.GetAll().ToList())
            {
                List<AnimeSeries> list = grp.GetSeries();

                // only rename the group if it has one direct child Anime Series
                if (list.Count == 1)
                {
                    RepoFactory.AnimeSeries.Save(list[0], false);
					list[0].UpdateStats(true, true, false);
					string newTitle = list[0].GetSeriesName();
                    grp.GroupName = newTitle;
                    grp.SortName = newTitle;
                    RepoFactory.AnimeGroup.Save(grp, true, true);
                }
                else if (list.Count > 1)
                {
                    #region Naming

                    AnimeSeries series = null;
                    bool hasCustomName = true;
                    if (grp.DefaultAnimeSeriesID.HasValue)
                    {
                        series = RepoFactory.AnimeSeries.GetByID(grp.DefaultAnimeSeriesID.Value);
                        if (series == null)
                        {
                            grp.DefaultAnimeSeriesID = null;
                        }
                        else
                        {
                            hasCustomName = false;
                        }
                    }

                    if (!grp.DefaultAnimeSeriesID.HasValue)
                    {
                        foreach (AnimeSeries ser in list)
                        {
                            if (ser == null) continue;
                                // Check all titles for custom naming, in case user changed language preferences
                            if (ser.SeriesNameOverride.Equals(grp.GroupName))
                            {
                                hasCustomName = false;
                            }
                            else
                            {
                                foreach (AniDB_Anime_Title title in ser.GetAnime().GetTitles())
                                {
                                    if (title.Title.Equals(grp.GroupName))
                                    {
                                        hasCustomName = false;
                                        break;
                                    }
                                }
								#region tvdb names
								List<TvDB_Series> tvdbs = ser.GetTvDBSeries();
								if(tvdbs != null && tvdbs.Count != 0)
								{
									foreach (TvDB_Series tvdbser in tvdbs)
									{
										if (tvdbser.SeriesName.Equals(grp.GroupName))
										{
											hasCustomName = false;
											break;
										}
									}
								}
                                #endregion
                                RepoFactory.AnimeSeries.Save(ser, false);
								ser.UpdateStats(true, true, false);
								if (series == null)
								{
									series = ser;
									continue;
								}
								if (ser.AirDate < series.AirDate) series = ser;
							}
                        }
                    }
                    if (series != null)
                    {
                        string newTitle = series.GetSeriesName();
						if (grp.DefaultAnimeSeriesID.HasValue &&
							grp.DefaultAnimeSeriesID.Value != series.AnimeSeriesID)
							newTitle = RepoFactory.AnimeSeries.GetByID(grp.DefaultAnimeSeriesID.Value).GetSeriesName();
                        if (hasCustomName) newTitle = grp.GroupName;
                        // reset tags, description, etc to new series
                        grp.Populate(series);
                        grp.GroupName = newTitle;
                        grp.SortName = newTitle;
                        RepoFactory.AnimeGroup.Save(grp, true, true);
						grp.TopLevelAnimeGroup.UpdateStatsFromTopLevel(true, true, false);
                    }

                    #endregion
                }
            }
        }


        public List<AniDB_Anime> Anime
        {
            get
            {
                List<AniDB_Anime> relAnime = new List<AniDB_Anime>();
                foreach (AnimeSeries serie in GetSeries())
                {
                    AniDB_Anime anime = serie.GetAnime();
                    if (anime != null) relAnime.Add(anime);
                }
                return relAnime;
            }
        }

        public decimal AniDBRating
        {
            get
            {
                try
                {
                    decimal totalRating = 0;
                    int totalVotes = 0;

                    foreach (AniDB_Anime anime in Anime)
                    {
                        totalRating += (decimal) anime.AniDBTotalRating;
                        totalVotes += anime.AniDBTotalVotes;
                    }

                    if (totalVotes == 0)
                        return 0;
                    else
                        return totalRating/(decimal) totalVotes;
                }
                catch (Exception ex)
                {
                    logger.Error("Error in  AniDBRating: {0}", ex.ToString());
                    return 0;
                }
            }
        }


        /*		[XmlIgnore]
		 public List<AnimeGroup> ChildGroups
		{
			get
			{
				AnimeGroupRepository repGroups = new AnimeGroupRepository();
				return repGroups.GetByParentID(this.AnimeGroupID);
			}
		}*/

        public List<AnimeGroup> GetChildGroups()
        {
            return RepoFactory.AnimeGroup.GetByParentID(AnimeGroupID);
        }


        /*[XmlIgnore]
		public List<AnimeGroup> AllChildGroups
		{
			get
			{
				List<AnimeGroup> grpList = new List<AnimeGroup>();
				AnimeGroup.GetAnimeGroupsRecursive(this.AnimeGroupID, ref grpList);
				return grpList;
			}
		}*/

        public List<AnimeGroup> GetAllChildGroups()
        {
            List<AnimeGroup> grpList = new List<AnimeGroup>();
            AnimeGroup.GetAnimeGroupsRecursive(this.AnimeGroupID, ref grpList);
            return grpList;
        }

        /*[XmlIgnore]
		public List<AnimeSeries> Series
		{
			get
			{
				AnimeSeriesRepository repSeries = new AnimeSeriesRepository();
				List<AnimeSeries> seriesList = repSeries.GetByGroupID(this.AnimeGroupID);

				return seriesList;
			}
		}*/

        public List<AnimeSeries> GetSeries()
        {
            List<AnimeSeries> seriesList = RepoFactory.AnimeSeries.GetByGroupID(this.AnimeGroupID);
            // Make everything that relies on GetSeries[0] have the proper result
            seriesList.OrderBy(a => a.AirDate);
            if (DefaultAnimeSeriesID.HasValue)
            {
                AnimeSeries series = RepoFactory.AnimeSeries.GetByID(DefaultAnimeSeriesID.Value);
                if (series != null)
                {
                    seriesList.Remove(series);
                    seriesList.Insert(0, series);
                }
            }
            return seriesList;
        }

		/*[XmlIgnore]
		public List<AnimeSeries> AllSeries
		{
			get
			{
				List<AnimeSeries> seriesList = new List<AnimeSeries>();
				AnimeGroup.GetAnimeSeriesRecursive(this.AnimeGroupID, ref seriesList);

				return seriesList;
			}
		}*/

        public List<AnimeSeries> GetAllSeries(bool skipSorting = false)
        {
            List<AnimeSeries> seriesList = new List<AnimeSeries>();
            AnimeGroup.GetAnimeSeriesRecursive(this.AnimeGroupID, ref seriesList);
			if (skipSorting) return seriesList;

            return seriesList.OrderBy(a => a.AirDate).ToList();
        }

        public static Dictionary<int, GroupVotes> BatchGetVotes(ISessionWrapper session, IReadOnlyCollection<AnimeGroup> animeGroups)
        {
            if (animeGroups == null)
                throw new ArgumentNullException(nameof(animeGroups));

            var votesByGroup = new Dictionary<int, GroupVotes>();

            if (animeGroups.Count == 0)
            {
                return votesByGroup;
            }

            var seriesByGroup = animeGroups.ToDictionary(g => g.AnimeGroupID, g => g.GetAllSeries());
            var allAnimeIds = seriesByGroup.Values.SelectMany(serLst => serLst.Select(series => series.AniDB_ID)).ToArray();
            var votesByAnime = RepoFactory.AniDB_Vote.GetByAnimeIDs(session, allAnimeIds);

            foreach (AnimeGroup animeGroup in animeGroups)
            {
                decimal allVoteTotal = 0m;
                decimal permVoteTotal = 0m;
                decimal tempVoteTotal = 0m;
                int allVoteCount = 0;
                int permVoteCount = 0;
                int tempVoteCount = 0;
                var groupSeries = seriesByGroup[animeGroup.AnimeGroupID];

                foreach (AnimeSeries series in groupSeries)
                {
                    AniDB_Vote vote;

                    if (votesByAnime.TryGetValue(series.AniDB_ID, out vote))
                    {
                        allVoteCount++;
                        allVoteTotal += vote.VoteValue;

                        if (vote.VoteType == (int)AniDBVoteType.Anime)
                        {
                            permVoteCount++;
                            permVoteTotal += vote.VoteValue;
                        }
                        else if (vote.VoteType == (int)AniDBVoteType.AnimeTemp)
                        {
                            tempVoteCount++;
                            tempVoteTotal += vote.VoteValue;
                        }
                    }
                }

                var groupVotes = new GroupVotes(allVoteCount == 0 ? (decimal?)null : allVoteTotal / allVoteCount / 100m,
                    permVoteCount == 0 ? (decimal?)null : permVoteTotal / permVoteCount / 100m,
                    tempVoteCount == 0 ? (decimal?)null : tempVoteTotal / tempVoteCount / 100m);

                votesByGroup[animeGroup.AnimeGroupID] = groupVotes;
            }

            return votesByGroup;
        }

        public GroupVotes GetVotes(ISessionWrapper session)
        {
            var votesByGroup = BatchGetVotes(session, new [] { this });
            GroupVotes votes;

            votesByGroup.TryGetValue(AnimeGroupID, out votes);

            return votes ?? GroupVotes.Null;
        }

        /*
		public string TagsString
		{
			get
			{
				string temp = "";
                foreach (AniDB_Tag tag in Tags)
                    temp += tag.TagName + "|";
				if (temp.Length > 2)
					temp = temp.Substring(0, temp.Length - 2);

				return temp;
			}
		}
		*/

        public List<AniDB_Tag> Tags
        {
            get
            {
                List<AniDB_Tag> tags = new List<AniDB_Tag>();
                List<int> animeTagIDs = new List<int>();
                List<AniDB_Anime_Tag> animeTags = new List<AniDB_Anime_Tag>();

                using (var session = DatabaseFactory.SessionFactory.OpenSession())
                {
                    // get a list of all the unique tags for this all the series in this group
                    foreach (AnimeSeries ser in GetAllSeries())
                    {
                        foreach (AniDB_Anime_Tag aac in ser.GetAnime().GetAnimeTags())
                        {
                            if (!animeTagIDs.Contains(aac.AniDB_Anime_TagID))
                            {
                                animeTagIDs.Add(aac.AniDB_Anime_TagID);
                                animeTags.Add(aac);
                            }
                        }
                    }

                    foreach (AniDB_Anime_Tag animeTag in animeTags.OrderByDescending(a=>a.Weight))
                    {
                        AniDB_Tag tag = RepoFactory.AniDB_Tag.GetByTagID(animeTag.TagID);
                        if (tag != null) tags.Add(tag);
                    }
                }

                return tags;
            }
        }

        /*
        public string CustomTagsString
        {
            get
            {
                string temp = "";
                foreach (CustomTag tag in CustomTags)
                {
                    if (!string.IsNullOrEmpty(temp))
                        temp += "|"; 
                    temp += tag.TagName; 
                }
                    
                return temp;
            }
        }
		*/

        public List<CustomTag> CustomTags
        {
            get
            {
                List<CustomTag> tags = new List<CustomTag>();
                List<int> tagIDs = new List<int>();

                
                // get a list of all the unique custom tags for all the series in this group
                foreach (AnimeSeries ser in GetAllSeries())
                {
                    foreach (CustomTag tag in RepoFactory.CustomTag.GetByAnimeID(ser.AniDB_ID))
                    {
                        if (!tagIDs.Contains(tag.CustomTagID))
                        {
                            tagIDs.Add(tag.CustomTagID);
                            tags.Add(tag);
                        }
                    }
                }

                return tags.OrderBy(a=>a.TagName).ToList();
            }
        }

        public List<AniDB_Anime_Title> Titles
        {
            get
            {
                List<int> animeTitleIDs = new List<int>();
                List<AniDB_Anime_Title> animeTitles = new List<AniDB_Anime_Title>();


                // get a list of all the unique titles for this all the series in this group
                foreach (AnimeSeries ser in GetAllSeries())
                {
                    foreach (AniDB_Anime_Title aat in ser.GetAnime().GetTitles())
                    {
                        if (!animeTitleIDs.Contains(aat.AniDB_Anime_TitleID))
                        {
                            animeTitleIDs.Add(aat.AniDB_Anime_TitleID);
                            animeTitles.Add(aat);
                        }
                    }
                }

                return animeTitles;
            }
        }

        /*
		public string TitlesString
		{
			get
			{
				string temp = "";
				foreach (AniDB_Anime_Title title in Titles)
					temp += title.Title + ", ";
				if (temp.Length > 2)
					temp = temp.Substring(0, temp.Length - 2);

				return temp;
			}
		}
		*/

        public HashSet<string> VideoQualities
        {
            get
            {
                return RepoFactory.Adhoc.GetAllVideoQualityForGroup(this.AnimeGroupID);
            }
        }

        public override string ToString()
        {
            return String.Format("Group: {0} ({1})", GroupName, AnimeGroupID);
            //return "";
        }

        public void UpdateStatsFromTopLevel(bool watchedStats, bool missingEpsStats)
        {
            UpdateStatsFromTopLevel(false, watchedStats, missingEpsStats);
        }



        /// <summary>
        /// Update stats for all child groups and series
        /// This should only be called from the very top level group.
        /// </summary>
        public void UpdateStatsFromTopLevel(bool updateGroupStatsOnly, bool watchedStats, bool missingEpsStats)
        {
            if (this.AnimeGroupParentID.HasValue) return;

            // update the stats for all the sries first
            if (!updateGroupStatsOnly)
            {
                foreach (AnimeSeries ser in GetAllSeries())
                {
                    ser.UpdateStats(watchedStats, missingEpsStats, false);
                }
            }

            // now recursively update stats for all the child groups
            // and update the stats for the groups
            foreach (AnimeGroup grp in GetAllChildGroups())
            {
                grp.UpdateStats(watchedStats, missingEpsStats);
            }

            UpdateStats(watchedStats, missingEpsStats);
        }

        /// <summary>
        /// Update the stats for this group based on the child series
        /// Assumes that all the AnimeSeries have had their stats updated already
        /// </summary>
        public void UpdateStats(bool watchedStats, bool missingEpsStats)
        {
            List<AnimeSeries> seriesList = GetAllSeries();

            if (missingEpsStats)
            {
                UpdateMissingEpisodeStats(this, seriesList);
                RepoFactory.AnimeGroup.Save(this, true, false);
            }

            if (watchedStats)
            {
                IReadOnlyList<JMMUser> allUsers = RepoFactory.JMMUser.GetAll();

                UpdateWatchedStats(this, seriesList, allUsers, (userRecord, isNew) =>
                    {
                        // Now update the stats for the groups
                        logger.Trace("Updating stats for {0}", this.ToString());
                        RepoFactory.AnimeGroup_User.Save(userRecord);
                    });
            }
        }

        /// <summary>
        /// Batch updates watched/missing episode stats for the specified sequence of <see cref="AnimeGroup"/>s.
        /// </summary>
        /// <remarks>
        /// NOTE: This method does NOT save the changes made to the database.
        /// NOTE 2: Assumes that all the AnimeSeries have had their stats updated already.
        /// </remarks>
        /// <param name="animeGroups">The sequence of <see cref="AnimeGroup"/>s whose missing episode stats are to be updated.</param>
        /// <param name="watchedStats"><c>true</c> to update watched stats; otherwise, <c>false</c>.</param>
        /// <param name="missingEpsStats"><c>true</c> to update missing episode stats; otherwise, <c>false</c>.</param>
        /// <param name="createdGroupUsers">The <see cref="ICollection{T}"/> to add any <see cref="AnimeGroup_User"/> records
        /// that were created when updating watched stats.</param>
        /// <param name="updatedGroupUsers">The <see cref="ICollection{T}"/> to add any <see cref="AnimeGroup_User"/> records
        /// that were modified when updating watched stats.</param>
        /// <exception cref="ArgumentNullException"><paramref name="animeGroups"/> is <c>null</c>.</exception>
        public static void BatchUpdateStats(IEnumerable<AnimeGroup> animeGroups, bool watchedStats = true, bool missingEpsStats = true,
            ICollection<AnimeGroup_User> createdGroupUsers = null, ICollection<AnimeGroup_User> updatedGroupUsers = null)
        {
            if (animeGroups == null)
                throw new ArgumentNullException(nameof(animeGroups));

            if (!watchedStats && !missingEpsStats)
            {
                return; // Nothing to do
            }

            var allUsers = new Lazy<IReadOnlyList<JMMUser>>(() => RepoFactory.JMMUser.GetAll(), isThreadSafe: false);

            foreach (AnimeGroup animeGroup in animeGroups)
            {
                List<AnimeSeries> animeSeries = animeGroup.GetAllSeries();

                if (missingEpsStats)
                {
                    UpdateMissingEpisodeStats(animeGroup, animeSeries);
                }
                if (watchedStats)
                {
                    UpdateWatchedStats(animeGroup, animeSeries, allUsers.Value, (userRecord, isNew) =>
                        {
                            if (isNew)
                            {
                                createdGroupUsers?.Add(userRecord);
                            }
                            else
                            {
                                updatedGroupUsers?.Add(userRecord);
                            }
                        });
                }
            }
        }

        /// <summary>
        /// Updates the watched stats for the specified anime group.
        /// </summary>
        /// <param name="animeGroup">The <see cref="AnimeGroup"/> that is to have it's watched stats updated.</param>
        /// <param name="seriesList">The list of <see cref="AnimeSeries"/> that belong to <paramref name="animeGroup"/>.</param>
        /// <param name="allUsers">A sequence of all JMM users.</param>
        /// <param name="newAnimeGroupUsers">A methed that will be called for each processed <see cref="AnimeGroup_User"/>
        /// and whether or not the <see cref="AnimeGroup_User"/> is new.</param>
        private static void UpdateWatchedStats(AnimeGroup animeGroup, IReadOnlyCollection<AnimeSeries> seriesList,
            IEnumerable<JMMUser> allUsers, Action<AnimeGroup_User, bool> newAnimeGroupUsers)
        {
            foreach (JMMUser juser in allUsers)
            {
                AnimeGroup_User userRecord = animeGroup.GetUserRecord(juser.JMMUserID);
                bool isNewRecord = false;

                if (userRecord == null)
                {
                    userRecord = new AnimeGroup_User(juser.JMMUserID, animeGroup.AnimeGroupID);
                    isNewRecord = true;
                }

                // Reset stats
                userRecord.WatchedCount = 0;
                userRecord.UnwatchedEpisodeCount = 0;
                userRecord.PlayedCount = 0;
                userRecord.StoppedCount = 0;
                userRecord.WatchedEpisodeCount = 0;
                userRecord.WatchedDate = null;

                foreach (AnimeSeries ser in seriesList)
                {
                    AnimeSeries_User serUserRecord = ser.GetUserRecord(juser.JMMUserID);

                    if (serUserRecord != null)
                    {
                        userRecord.WatchedCount += serUserRecord.WatchedCount;
                        userRecord.UnwatchedEpisodeCount += serUserRecord.UnwatchedEpisodeCount;
                        userRecord.PlayedCount += serUserRecord.PlayedCount;
                        userRecord.StoppedCount += serUserRecord.StoppedCount;
                        userRecord.WatchedEpisodeCount += serUserRecord.WatchedEpisodeCount;

                        if (serUserRecord.WatchedDate != null
                            && (userRecord.WatchedDate == null || serUserRecord.WatchedDate > userRecord.WatchedDate))
                        {
                            userRecord.WatchedDate = serUserRecord.WatchedDate;
                        }
                    }
                }

                newAnimeGroupUsers(userRecord, isNewRecord);
            }
        }

        /// <summary>
        /// Updates the missing episode stats for the specified anime group.
        /// </summary>
        /// <remarks>
        /// NOTE: This method does NOT save the changes made to the database.
        /// NOTE 2: Assumes that all the AnimeSeries have had their stats updated already.
        /// </remarks>
        /// <param name="animeGroup">The <see cref="AnimeGroup"/> that is to have it's missing episode stats updated.</param>
        /// <param name="seriesList">The list of <see cref="AnimeSeries"/> that belong to <paramref name="animeGroup"/>.</param>
        private static void UpdateMissingEpisodeStats(AnimeGroup animeGroup, IEnumerable<AnimeSeries> seriesList)
        {
            animeGroup.MissingEpisodeCount = 0;
            animeGroup.MissingEpisodeCountGroups = 0;

            foreach (AnimeSeries series in seriesList)
            {
                animeGroup.MissingEpisodeCount += series.MissingEpisodeCount;
                animeGroup.MissingEpisodeCountGroups += series.MissingEpisodeCountGroups;

                // Now series.LatestEpisodeAirDate should never be greater than today
                if (series.LatestEpisodeAirDate.HasValue)
                {
                    if ((animeGroup.LatestEpisodeAirDate.HasValue && series.LatestEpisodeAirDate.Value > animeGroup.LatestEpisodeAirDate.Value)
                        || !animeGroup.LatestEpisodeAirDate.HasValue)
                    {
                        animeGroup.LatestEpisodeAirDate = series.LatestEpisodeAirDate;
                    }
                }
            }
        }


        public static HashSet<GroupFilterConditionType> GetConditionTypesChanged(Contract_AnimeGroup oldcontract,
            Contract_AnimeGroup newcontract)
        {
            HashSet<GroupFilterConditionType> h = new HashSet<GroupFilterConditionType>();
            if (oldcontract == null || oldcontract.Stat_IsComplete != newcontract.Stat_IsComplete)
                h.Add(GroupFilterConditionType.CompletedSeries);
            if (oldcontract == null ||
                (oldcontract.MissingEpisodeCount > 0 || oldcontract.MissingEpisodeCountGroups > 0) !=
                (newcontract.MissingEpisodeCount > 0 || newcontract.MissingEpisodeCountGroups > 0))
                h.Add(GroupFilterConditionType.MissingEpisodes);
            if (oldcontract == null || !oldcontract.Stat_AllTags.SetEquals(newcontract.Stat_AllTags))
                h.Add(GroupFilterConditionType.Tag);
            if (oldcontract == null || oldcontract.Stat_AirDate_Min != newcontract.Stat_AirDate_Min ||
                oldcontract.Stat_AirDate_Max != newcontract.Stat_AirDate_Max)
                h.Add(GroupFilterConditionType.AirDate);
            if (oldcontract == null || oldcontract.Stat_HasTvDBLink != newcontract.Stat_HasTvDBLink)
                h.Add(GroupFilterConditionType.AssignedTvDBInfo);
            if (oldcontract == null || oldcontract.Stat_HasMALLink != newcontract.Stat_HasMALLink)
                h.Add(GroupFilterConditionType.AssignedMALInfo);
            if (oldcontract == null || oldcontract.Stat_HasMovieDBLink != newcontract.Stat_HasMovieDBLink)
                h.Add(GroupFilterConditionType.AssignedMovieDBInfo);
            if (oldcontract == null || oldcontract.Stat_HasMovieDBOrTvDBLink != newcontract.Stat_HasMovieDBOrTvDBLink)
                h.Add(GroupFilterConditionType.AssignedTvDBOrMovieDBInfo);
            if (oldcontract == null || !oldcontract.Stat_AnimeTypes.SetEquals(newcontract.Stat_AnimeTypes))
                h.Add(GroupFilterConditionType.AnimeType);
            if (oldcontract == null || !oldcontract.Stat_AllVideoQuality.SetEquals(newcontract.Stat_AllVideoQuality) ||
                !oldcontract.Stat_AllVideoQuality_Episodes.SetEquals(newcontract.Stat_AllVideoQuality_Episodes))
                h.Add(GroupFilterConditionType.VideoQuality);
            if (oldcontract == null || oldcontract.AnimeGroupID != newcontract.AnimeGroupID)
                h.Add(GroupFilterConditionType.AnimeGroup);
            if (oldcontract == null || oldcontract.Stat_AniDBRating != newcontract.Stat_AniDBRating)
                h.Add(GroupFilterConditionType.AniDBRating);
            if (oldcontract == null || oldcontract.Stat_SeriesCreatedDate != newcontract.Stat_SeriesCreatedDate)
                h.Add(GroupFilterConditionType.SeriesCreatedDate);
            if (oldcontract == null || oldcontract.EpisodeAddedDate != newcontract.EpisodeAddedDate)
                h.Add(GroupFilterConditionType.EpisodeAddedDate);
            if (oldcontract == null || oldcontract.Stat_HasFinishedAiring != newcontract.Stat_HasFinishedAiring ||
                oldcontract.Stat_IsCurrentlyAiring != newcontract.Stat_IsCurrentlyAiring)
                h.Add(GroupFilterConditionType.FinishedAiring);
            if (oldcontract == null ||
                oldcontract.MissingEpisodeCountGroups > 0 != newcontract.MissingEpisodeCountGroups > 0)
                h.Add(GroupFilterConditionType.MissingEpisodesCollecting);
            if (oldcontract == null || !oldcontract.Stat_AudioLanguages.SetEquals(newcontract.Stat_AudioLanguages))
                h.Add(GroupFilterConditionType.AudioLanguage);
            if (oldcontract == null || !oldcontract.Stat_SubtitleLanguages.SetEquals(newcontract.Stat_SubtitleLanguages))
                h.Add(GroupFilterConditionType.SubtitleLanguage);
            if (oldcontract == null || oldcontract.Stat_EpisodeCount != newcontract.Stat_EpisodeCount)
                h.Add(GroupFilterConditionType.EpisodeCount);
            if (oldcontract == null || !oldcontract.Stat_AllCustomTags.SetEquals(newcontract.Stat_AllCustomTags))
                h.Add(GroupFilterConditionType.CustomTags);
            if (oldcontract == null || oldcontract.LatestEpisodeAirDate != newcontract.LatestEpisodeAirDate)
                h.Add(GroupFilterConditionType.LatestEpisodeAirDate);
            int oldyear = -1;
            int newyear = -1;
            if (oldcontract?.Stat_AirDate_Min != null)
                oldyear = oldcontract.Stat_AirDate_Min.Value.Year;
            if (newcontract?.Stat_AirDate_Min != null)
                newyear = newcontract.Stat_AirDate_Min.Value.Year;
            if (oldyear != newyear)
                h.Add(GroupFilterConditionType.Year);

            //TODO This two should be moved to AnimeGroup_User in the future...
            if (oldcontract == null || oldcontract.Stat_UserVotePermanent != newcontract.Stat_UserVotePermanent)
                h.Add(GroupFilterConditionType.UserVoted);

            if (oldcontract == null || oldcontract.Stat_UserVoteOverall != newcontract.Stat_UserVoteOverall)
            {
                h.Add(GroupFilterConditionType.UserRating);
                h.Add(GroupFilterConditionType.UserVotedAny);
            }
            return h;
        }

        public static Dictionary<int, HashSet<GroupFilterConditionType>> BatchUpdateContracts(ISessionWrapper session,
            IReadOnlyCollection<AnimeGroup> animeGroups, bool updateStats)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (animeGroups == null)
                throw new ArgumentNullException(nameof(animeGroups));

            var grpFilterCondTypesByGroup = new Dictionary<int, HashSet<GroupFilterConditionType>>();

            if (animeGroups.Count == 0)
            {
                return grpFilterCondTypesByGroup;
            }

            var seriesByGroup = animeGroups.ToDictionary(g => g.AnimeGroupID, g => g.GetAllSeries());
            var allAnimeIds = new Lazy<int[]>(
                () => seriesByGroup.Values.SelectMany(serLst => serLst.Select(series => series.AniDB_ID)).ToArray(), isThreadSafe: false);
            var allGroupIds = new Lazy<int[]>(
                () => animeGroups.Select(grp => grp.AnimeGroupID).ToArray(), isThreadSafe: false);
            var audioLangStatsByAnime = new Lazy<Dictionary<int, LanguageStat>>(
                () => RepoFactory.Adhoc.GetAudioLanguageStatsByAnime(session, allAnimeIds.Value), isThreadSafe: false);
            var subLangStatsByAnime  = new Lazy<Dictionary<int, LanguageStat>>(
                () => RepoFactory.Adhoc.GetSubtitleLanguageStatsByAnime(session, allAnimeIds.Value), isThreadSafe: false);
            var tvDbXrefByAnime = new Lazy<ILookup<int, CrossRef_AniDB_TvDBV2>>(
                () => RepoFactory.CrossRef_AniDB_TvDBV2.GetByAnimeIDs(session, allAnimeIds.Value), isThreadSafe: false);
            var allVidQualByGroup = new Lazy<ILookup<int, string>>(
                () => RepoFactory.Adhoc.GetAllVideoQualityByGroup(session, allGroupIds.Value), isThreadSafe: false);
            var movieDbXRefByAnime = new Lazy<ILookup<int, CrossRef_AniDB_Other>>(
                () => RepoFactory.CrossRef_AniDB_Other.GetByAnimeIDsAndType(session, allAnimeIds.Value, CrossRefType.MovieDB), isThreadSafe: false);
            var malXRefByAnime = new Lazy<ILookup<int, CrossRef_AniDB_MAL>>(
                () => RepoFactory.CrossRef_AniDB_MAL.GetByAnimeIDs(session, allAnimeIds.Value), isThreadSafe: false);
            var votesByGroup = BatchGetVotes(session, animeGroups);
            DateTime now = DateTime.Now;

            foreach (AnimeGroup animeGroup in animeGroups)
            {
                Contract_AnimeGroup contract = animeGroup.Contract?.DeepCopy();
                bool localUpdateStats = updateStats;

                if (contract == null)
                {
                    contract = new Contract_AnimeGroup();
                    localUpdateStats = true;
                }

                contract.AnimeGroupID = animeGroup.AnimeGroupID;
                contract.AnimeGroupParentID = animeGroup.AnimeGroupParentID;
                contract.DefaultAnimeSeriesID = animeGroup.DefaultAnimeSeriesID;
                contract.GroupName = animeGroup.GroupName;
                contract.Description = animeGroup.Description;
                contract.LatestEpisodeAirDate = animeGroup.LatestEpisodeAirDate;
                contract.SortName = animeGroup.SortName;
                contract.EpisodeAddedDate = animeGroup.EpisodeAddedDate;
                contract.OverrideDescription = animeGroup.OverrideDescription;
                contract.DateTimeUpdated = animeGroup.DateTimeUpdated;
                contract.IsFave = 0;
                contract.UnwatchedEpisodeCount = 0;
                contract.WatchedEpisodeCount = 0;
                contract.WatchedDate = null;
                contract.PlayedCount = 0;
                contract.WatchedCount = 0;
                contract.StoppedCount = 0;
                contract.MissingEpisodeCount = animeGroup.MissingEpisodeCount;
                contract.MissingEpisodeCountGroups = animeGroup.MissingEpisodeCountGroups;

                List<AnimeSeries> allSeriesForGroup = seriesByGroup[animeGroup.AnimeGroupID];

                if (localUpdateStats)
                {
                    DateTime? airDateMin = null;
                    DateTime? airDateMax = null;
                    DateTime? groupEndDate = new DateTime(1980, 1, 1);
                    DateTime? seriesCreatedDate = null;
                    bool isComplete = false;
                    bool hasFinishedAiring = false;
                    bool isCurrentlyAiring = false;
                    HashSet<string> videoQualityEpisodes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    HashSet<string> audioLanguages = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    HashSet<string> subtitleLanguages = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    bool hasTvDB = true;
                    bool hasMAL = true;
                    bool hasMovieDB = true;
                    bool hasMovieDBOrTvDB = true;
                    int seriesCount = 0;
                    int epCount = 0;

                    foreach (AnimeSeries series in allSeriesForGroup)
                    {
                        seriesCount++;

                        List<VideoLocal> vidsTemp = RepoFactory.VideoLocal.GetByAniDBAnimeID(series.AniDB_ID);
                        List<CrossRef_File_Episode> crossRefs = RepoFactory.CrossRef_File_Episode.GetByAnimeID(series.AniDB_ID);
                        ILookup<int, CrossRef_File_Episode> crossRefsLookup = crossRefs.ToLookup(cr => cr.EpisodeID);
                        var dictVids = new Dictionary<string, VideoLocal>();

                        foreach (VideoLocal vid in vidsTemp)
                        {
                            //Hashes may be repeated from multiple locations but we don't care
                            dictVids[vid.Hash] = vid;
                        }

                        // All Video Quality Episodes
                        // Try to determine if this anime has all the episodes available at a certain video quality
                        // e.g.  the series has all episodes in blu-ray
                        // Also look at languages
                        Dictionary<string, int> vidQualEpCounts = new Dictionary<string, int>();
                        // video quality, count of episodes
                        AniDB_Anime anime = series.GetAnime();

                        foreach (AnimeEpisode ep in series.GetAnimeEpisodes())
                        {
                            if (ep.EpisodeTypeEnum != enEpisodeType.Episode)
                            {
                                continue;
                            }

                            var epVids = new List<VideoLocal>();

                            foreach (CrossRef_File_Episode xref in crossRefsLookup[ep.AniDB_EpisodeID])
                            {
                                if (xref.EpisodeID != ep.AniDB_EpisodeID)
                                {
                                    continue;
                                }

                                VideoLocal video;

                                if (dictVids.TryGetValue(xref.Hash, out video))
                                {
                                    epVids.Add(video);
                                }
                            }

                            var qualityAddedSoFar = new HashSet<string>();

                            // Handle mutliple files of the same quality for one episode
                            foreach (VideoLocal vid in epVids)
                            {
                                AniDB_File anifile = vid.GetAniDBFile();

                                if (anifile == null)
                                {
                                    continue;
                                }

                                if (!qualityAddedSoFar.Contains(anifile.File_Source))
                                {
                                    int srcCount;

                                    vidQualEpCounts.TryGetValue(anifile.File_Source, out srcCount);
                                    vidQualEpCounts[anifile.File_Source] = srcCount + 1; // If the file source wasn't originally in the dictionary, then it will be set to 1

                                    qualityAddedSoFar.Add(anifile.File_Source);
                                }
                            }
                        }

                        epCount += anime.EpisodeCountNormal;

                        // Add all video qualities that span all of the normal episodes
                        videoQualityEpisodes.UnionWith(
                            vidQualEpCounts
                                .Where(vqec => anime.EpisodeCountNormal == vqec.Value)
                                .Select(vqec => vqec.Key));

                        LanguageStat langStats;

                        // Audio languages
                        if (audioLangStatsByAnime.Value.TryGetValue(anime.AnimeID, out langStats))
                        {
                            audioLanguages.UnionWith(langStats.LanguageNames);
                        }

                        // Subtitle languages
                        if (subLangStatsByAnime.Value.TryGetValue(anime.AnimeID, out langStats))
                        {
                            subtitleLanguages.UnionWith(langStats.LanguageNames);
                        }

                        // Calculate Air Date 
                        DateTime seriesAirDate = series.AirDate;

                        if (seriesAirDate != DateTime.MinValue)
                        {
                            if (airDateMin == null || seriesAirDate < airDateMin.Value)
                            {
                                airDateMin = seriesAirDate;
                            }

                            if (airDateMax == null || seriesAirDate > airDateMax.Value)
                            {
                                airDateMax = seriesAirDate;
                            }
                        }

                        // Calculate end date
                        // If the end date is NULL it actually means it is ongoing, so this is the max possible value
                        DateTime? seriesEndDate = series.EndDate;

                        if (seriesEndDate == null || groupEndDate == null)
                        {
                            groupEndDate = null;
                        }
                        else if (seriesEndDate.Value > groupEndDate.Value)
                        {
                            groupEndDate = seriesEndDate;
                        }

                         // Note - only one series has to be finished airing to qualify
                        if (series.EndDate != null && series.EndDate.Value < now)
                        {
                            hasFinishedAiring = true;
                        }
                        // Note - only one series has to be finished airing to qualify
                        if (series.EndDate == null || series.EndDate.Value > now)
                        {
                            isCurrentlyAiring = true;
                        }

                        // We evaluate IsComplete as true if
                        // 1. series has finished airing
                        // 2. user has all episodes locally
                        // Note - only one series has to be complete for the group to be considered complete
                        if (series.EndDate != null && series.EndDate.Value < now
                            && series.MissingEpisodeCount == 0 && series.MissingEpisodeCountGroups == 0)
                        {
                            isComplete = true;
                        }

                        // Calculate Series Created Date 
                        DateTime createdDate = series.DateTimeCreated;

                        if (seriesCreatedDate == null || createdDate < seriesCreatedDate.Value)
                        {
                            seriesCreatedDate = createdDate;
                        }

                        // For the group, if any of the series don't have a tvdb link
                        // we will consider the group as not having a tvdb link
                        if (!tvDbXrefByAnime.Value[anime.AnimeID].Any())
                        {
	                        if(anime.AnimeType != (int) enAnimeType.Movie && !(anime.Restricted > 0))
	                        	hasTvDB = false;
                        }
                        if (!movieDbXRefByAnime.Value[anime.AnimeID].Any())
                        {
	                        if(anime.AnimeType == (int) enAnimeType.Movie && !(anime.Restricted > 0))
		                        hasMovieDB = false;
                        }
                        if (!malXRefByAnime.Value[anime.AnimeID].Any())
                        {
                            hasMAL = false;
                        }

                        hasMovieDBOrTvDB = hasTvDB || hasMovieDB;
                    }

                    contract.Stat_AllTags = animeGroup.Tags
                        .Select(a => a.TagName)
                        .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
                    contract.Stat_AllCustomTags = animeGroup.CustomTags
                        .Select(a => a.TagName)
                        .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
                    contract.Stat_AllTitles = animeGroup.Titles
                        .Select(a => a.Title)
                        .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
                    contract.Stat_AnimeTypes = allSeriesForGroup
                        .Select(a => a.Contract?.AniDBAnime?.AniDBAnime)
                        .Where(a => a != null)
                        .Select(a => a.AnimeType.ToString())
                        .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
                    contract.Stat_AllVideoQuality = allVidQualByGroup.Value[animeGroup.AnimeGroupID]
                        .ToHashSet(StringComparer.InvariantCultureIgnoreCase);
                    contract.Stat_IsComplete = isComplete;
                    contract.Stat_HasFinishedAiring = hasFinishedAiring;
                    contract.Stat_IsCurrentlyAiring = isCurrentlyAiring;
                    contract.Stat_HasTvDBLink = hasTvDB;
                    contract.Stat_HasMALLink = hasMAL;
                    contract.Stat_HasMovieDBLink = hasMovieDB;
                    contract.Stat_HasMovieDBOrTvDBLink = hasMovieDBOrTvDB;
                    contract.Stat_SeriesCount = seriesCount;
                    contract.Stat_EpisodeCount = epCount;
                    contract.Stat_AllVideoQuality_Episodes = videoQualityEpisodes;
                    contract.Stat_AirDate_Min = airDateMin;
                    contract.Stat_AirDate_Max = airDateMax;
                    contract.Stat_EndDate = groupEndDate;
                    contract.Stat_SeriesCreatedDate = seriesCreatedDate;
                    contract.Stat_AniDBRating = animeGroup.AniDBRating;
                    contract.Stat_AudioLanguages = audioLanguages;
                    contract.Stat_SubtitleLanguages = subtitleLanguages;
                    contract.LatestEpisodeAirDate = animeGroup.LatestEpisodeAirDate;

                    GroupVotes votes;

                    votesByGroup.TryGetValue(animeGroup.AnimeGroupID, out votes);
                    contract.Stat_UserVoteOverall = votes?.AllVotes;
                    contract.Stat_UserVotePermanent = votes?.PermanentVotes;
                    contract.Stat_UserVoteTemporary = votes?.TemporaryVotes;
                }

                grpFilterCondTypesByGroup[animeGroup.AnimeGroupID] = GetConditionTypesChanged(animeGroup.Contract, contract);
                animeGroup.Contract = contract;
            }

            return grpFilterCondTypesByGroup;
        }

        public HashSet<GroupFilterConditionType> UpdateContract(ISessionWrapper session, bool updatestats)
        {
            var grpFilterCondTypesByGroup = BatchUpdateContracts(session, new[] { this }, updatestats);

            return grpFilterCondTypesByGroup[AnimeGroupID];
        }

        public void DeleteFromFilters()
        {
            foreach (GroupFilter gf in RepoFactory.GroupFilter.GetAll())
            {
                bool change = false;
                foreach (int k in gf.GroupsIds.Keys)
                {
                    if (gf.GroupsIds[k].Contains(AnimeGroupID))
                    {
                        gf.GroupsIds[k].Remove(AnimeGroupID);
                        change = true;
                    }
                }
                if (change)
                    RepoFactory.GroupFilter.Save(gf);
            }
        }

        public void UpdateGroupFilters(HashSet<GroupFilterConditionType> types, JMMUser user = null)
        {
            IReadOnlyList<JMMUser> users = new List<JMMUser> {user};
            if (user == null)
                users = RepoFactory.JMMUser.GetAll();
            List<GroupFilter> tosave = new List<GroupFilter>();

            foreach (JMMUser u in users)
            {
                HashSet<GroupFilterConditionType> n = new HashSet<GroupFilterConditionType>(types);
                Contract_AnimeGroup cgrp = GetUserContract(u.JMMUserID, n);
                foreach (GroupFilter gf in RepoFactory.GroupFilter.GetWithConditionTypesAndAll(n))
                {
                    if (gf.CalculateGroupFilterGroups(cgrp, u.Contract, u.JMMUserID))
                    {
                        if (!tosave.Contains(gf))
                            tosave.Add(gf);
                    }
                }
            }
            foreach (GroupFilter gf in tosave)
            {
                RepoFactory.GroupFilter.Save(gf);
            }
        }


        public static void GetAnimeGroupsRecursive(int animeGroupID, ref List<AnimeGroup> groupList)
        {
            AnimeGroup grp = RepoFactory.AnimeGroup.GetByID(animeGroupID);
            if (grp == null) return;

            // get the child groups for this group
            groupList.AddRange(grp.GetChildGroups());

            foreach (AnimeGroup childGroup in grp.GetChildGroups())
            {
                GetAnimeGroupsRecursive(childGroup.AnimeGroupID, ref groupList);
            }
        }

        public static void GetAnimeSeriesRecursive(int animeGroupID, ref List<AnimeSeries> seriesList)
        {
            AnimeGroup grp = RepoFactory.AnimeGroup.GetByID(animeGroupID);
            if (grp == null) return;

            // get the series for this group
            List<AnimeSeries> thisSeries = grp.GetSeries();
            seriesList.AddRange(thisSeries);

            foreach (AnimeGroup childGroup in grp.GetChildGroups())
            {
                GetAnimeSeriesRecursive(childGroup.AnimeGroupID, ref seriesList);
            }
        }

        public AnimeGroup TopLevelAnimeGroup
        {
            get
            {
                if (!AnimeGroupParentID.HasValue) return this;
                AnimeGroup parentGroup = RepoFactory.AnimeGroup.GetByID(this.AnimeGroupParentID.Value);
                while (parentGroup != null && parentGroup.AnimeGroupParentID.HasValue)
                {
                    parentGroup = RepoFactory.AnimeGroup.GetByID(parentGroup.AnimeGroupParentID.Value);
                }
                return parentGroup;
            }
        }
    }

    public class GroupVotes
    {
        public static readonly GroupVotes Null = new GroupVotes();

        public GroupVotes(decimal? allVotes = null, decimal? permanentVotes = null, decimal? temporaryVotes = null)
        {
            AllVotes       = allVotes;
            PermanentVotes = permanentVotes;
            TemporaryVotes = temporaryVotes;
        }

        public decimal? AllVotes { get; }

        public decimal? PermanentVotes { get; }

        public decimal? TemporaryVotes { get; }
    }
}
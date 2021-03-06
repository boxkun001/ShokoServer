﻿using FluentNHibernate.Mapping;
using JMMServer.Entities;

namespace JMMServer.Mappings
{
    public class JMMUserMap : ClassMap<JMMUser>
    {
        public JMMUserMap()
        {
            Not.LazyLoad();
            Id(x => x.JMMUserID);

            Map(x => x.HideCategories);
            Map(x => x.IsAniDBUser).Not.Nullable();
            Map(x => x.IsTraktUser).Not.Nullable();
            Map(x => x.IsAdmin).Not.Nullable();
            Map(x => x.Password);
            Map(x => x.Username);
            Map(x => x.CanEditServerSettings);
            Map(x => x.PlexUsers);
        }
    }
}
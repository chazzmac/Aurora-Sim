/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aurora.Framework;
using Aurora.DataManager;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using Nini.Config;
using OpenSim.Services.Interfaces;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using RegionFlags = Aurora.Framework.RegionFlags;

namespace Aurora.Services.DataService
{
    public class LocalFriendsConnector : IFriendsData
    {
        private IGenericData GD = null;
        private string m_realm = "friends";

        public void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore simBase, string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("FriendsConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;

                string connectionString = defaultConnectionString;
                if (source.Configs[Name] != null)
                    connectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                GD.ConnectToDatabase(connectionString, "Friends", source.Configs["AuroraConnectors"].GetBoolean("ValidateTables", true));

                DataManager.DataManager.RegisterPlugin(Name, this);
            }
        }

        public string Name
        {
            get { return "IFriendsData"; }
        }

        public void Dispose()
        {
        }

        #region IFriendsData Members

        public bool Store(UUID PrincipalID, string Friend, int Flags, int Offered)
        {
            GD.Delete (m_realm, new string[2] { "PrincipalID", "Friend" }, new object[2] { PrincipalID, Friend });
            return GD.Insert(m_realm, new string[] { "PrincipalID", "Friend", "Flags", "Offered" },
                new object[] { PrincipalID, Friend.MySqlEscape(), Flags, Offered });
        }

        public bool Delete(UUID ownerID, string friend)
        {
            return GD.Delete(m_realm, new string[] { "PrincipalID", "Friend" },
                new object[] { ownerID, friend.MySqlEscape() });
        }

        public FriendInfo[] GetFriends(UUID principalID)
        {
            List<FriendInfo> infos = new List<FriendInfo>();
            List<string> query = GD.Query("PrincipalID", principalID, m_realm, "Friend,Flags");

            //These are used to get the other flags below
            List<string> keys = new List<string>();
            List<object> values = new List<object>();

            for (int i = 0; i < query.Count; i += 2)
            {
                FriendInfo info = new FriendInfo();
                info.PrincipalID = principalID;
                info.Friend = query[i];
                info.MyFlags = int.Parse(query[i + 1]);
                infos.Add(info);

                keys.Add("PrincipalID");
                keys.Add("Friend");
                values.Add(info.Friend);
                values.Add(info.PrincipalID);

                List<string> query2 = GD.Query(keys.ToArray(), values.ToArray(), m_realm, "Flags");
                if (query2.Count >= 1) infos[infos.Count - 1].TheirFlags = int.Parse(query2[0]);

                keys = new List<string>();
                values = new List<object>();
            }
            return infos.ToArray();
        }

        #endregion
    }
}

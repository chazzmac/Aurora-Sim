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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using log4net;
using Nini.Config;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Services.Interfaces;
using Aurora.Framework;
using Aurora.DataManager;
using ChatSessionMember = OpenSim.Framework.ChatSessionMember;

namespace OpenSim.Region.OptionalModules.Avatar.XmlRpcGroups
{
    public class AuroraDataGroupsServicesConnectorModule : ISharedRegionModule, IGroupsServicesConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const GroupPowers m_DefaultEveryonePowers = GroupPowers.AllowSetHome |
            GroupPowers.Accountable |
            GroupPowers.JoinChat |
            GroupPowers.AllowVoiceChat |
            GroupPowers.ReceiveNotices |
            GroupPowers.StartProposal |
            GroupPowers.VoteOnProposal;

        private bool m_connectorEnabled = false;
        private bool m_notConnectedBecauseOfMissing = false;

        private IUserAccountService m_accountService = null;
        private IGroupsServiceConnector GroupsConnector;

        #region IRegionModuleBase Members

        public string Name
        {
            get { return "AuroraDataGroupsServicesConnectorModule"; }
        }

        // this module is not intended to be replaced, but there should only be 1 of them.
        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource config)
        {
            IConfig groupsConfig = config.Configs["Groups"];

            if (groupsConfig == null)
            {
                // Do not run this module by default.
                return;
            }
            else
            {
                // if groups aren't enabled, we're not needed.
                // if we're not specified as the connector to use, then we're not wanted
                if ((groupsConfig.GetBoolean("Enabled", false) == false)
                    || (groupsConfig.GetString("ServicesConnectorModule", "Default") != Name))
                {
                    m_connectorEnabled = false;
                    return;
                }

                //m_log.InfoFormat("[AURORA-GROUPS-CONNECTOR]: Initializing {0}", this.Name);

                m_connectorEnabled = true;
            }
        }

        public void Close()
        {
            m_log.InfoFormat("[AURORA-GROUPS-CONNECTOR]: Closing {0}", this.Name);
        }

        public void AddRegion(IScene scene)
        {
            GroupsConnector = Aurora.DataManager.DataManager.RequestPlugin<IGroupsServiceConnector>();
            if (GroupsConnector == null)
            {
                m_log.Warn("[AURORA-GROUPS-CONNECTOR]: GroupsConnector is null");
                m_connectorEnabled = false;
                m_notConnectedBecauseOfMissing = true;
            }
            if (m_connectorEnabled)
            {
                if (m_accountService == null)
                {
                    m_accountService = scene.UserAccountService;
                }
                scene.RegisterModuleInterface<IGroupsServicesConnector>(this);
            }
        }

        public void RemoveRegion(IScene scene)
        {
            if (scene.RequestModuleInterface<IGroupsServicesConnector>() == this)
            {
                scene.UnregisterModuleInterface<IGroupsServicesConnector>(this);
            }
        }

        public void RegionLoaded(IScene scene)
        {
        }

        #endregion

        #region ISharedRegionModule Members

        public void PostInitialise()
        {
            // NoOp
        }

        #endregion

        #region IGroupsServicesConnector Members

        /// <summary>
        /// Create a Group, including Everyone and Owners Role, place FounderID in both groups, select Owner as selected role, and newly created group as agent's active role.
        /// </summary>
        public UUID CreateGroup(UUID requestingAgentID, string name, string charter, bool showInList, UUID insigniaID,
                                int membershipFee, bool openEnrollment, bool allowPublish,
                                bool maturePublish, UUID founderID)
        {
            UUID GroupID = UUID.Random();
            UUID OwnerRoleID = UUID.Random();

            // Would this be cleaner as (GroupPowers)ulong.MaxValue;
            GroupPowers OwnerPowers = GroupPowers.Accountable
                                    | GroupPowers.AllowEditLand
                                    | GroupPowers.AllowFly
                                    | GroupPowers.AllowLandmark
                                    | GroupPowers.AllowRez
                                    | GroupPowers.AllowSetHome
                                    | GroupPowers.AllowVoiceChat
                                    | GroupPowers.AssignMember
                                    | GroupPowers.AssignMemberLimited
                                    | GroupPowers.ChangeActions
                                    | GroupPowers.ChangeIdentity
                                    | GroupPowers.ChangeMedia
                                    | GroupPowers.ChangeOptions
                                    | GroupPowers.CreateRole
                                    | GroupPowers.DeedObject
                                    | GroupPowers.DeleteRole
                                    | GroupPowers.Eject
                                    | GroupPowers.FindPlaces
                                    | GroupPowers.Invite
                                    | GroupPowers.JoinChat
                                    | GroupPowers.LandChangeIdentity
                                    | GroupPowers.LandDeed
                                    | GroupPowers.LandDivideJoin
                                    | GroupPowers.LandEdit
                                    | GroupPowers.LandEjectAndFreeze
                                    | GroupPowers.LandGardening
                                    | GroupPowers.LandManageAllowed
                                    | GroupPowers.LandManageBanned
                                    | GroupPowers.LandManagePasses
                                    | GroupPowers.LandOptions
                                    | GroupPowers.LandRelease
                                    | GroupPowers.LandSetSale
                                    | GroupPowers.ModerateChat
                                    | GroupPowers.ObjectManipulate
                                    | GroupPowers.ObjectSetForSale
                                    | GroupPowers.ReceiveNotices
                                    | GroupPowers.RemoveMember
                                    | GroupPowers.ReturnGroupOwned
                                    | GroupPowers.ReturnGroupSet
                                    | GroupPowers.ReturnNonGroup
                                    | GroupPowers.RoleProperties
                                    | GroupPowers.SendNotices
                                    | GroupPowers.SetLandingPoint
                                    | GroupPowers.StartProposal
                                    | GroupPowers.VoteOnProposal;

            GroupsConnector.CreateGroup(GroupID, name, charter, showInList,
                insigniaID, 0, openEnrollment, allowPublish, maturePublish, founderID,
                ((ulong)m_DefaultEveryonePowers), OwnerRoleID, ((ulong)OwnerPowers));

            return GroupID;
        }

        public void UpdateGroup(UUID requestingAgentID, UUID groupID, string charter, bool showInList,
                                UUID insigniaID, int membershipFee, bool openEnrollment,
                                bool allowPublish, bool maturePublish)
        {
            GroupsConnector.UpdateGroup(requestingAgentID, groupID, charter,
                showInList == true ? 1 : 0, insigniaID, membershipFee,
                openEnrollment == true ? 1 : 0, allowPublish == true ? 1 : 0,
                maturePublish == true ? 1 : 0);
        }

        public void AddGroupRole(UUID requestingAgentID, UUID groupID, UUID roleID, string name, string description,
                                 string title, ulong powers)
        {
            GroupsConnector.AddRoleToGroup(requestingAgentID, groupID, roleID, name, description, title, powers);
        }

        public void RemoveGroupRole(UUID requestingAgentID, UUID groupID, UUID roleID)
        {
            GroupsConnector.RemoveRoleFromGroup(requestingAgentID, roleID, groupID);
        }

        public void UpdateGroupRole(UUID requestingAgentID, UUID groupID, UUID roleID, string name, string description,
                                    string title, ulong powers)
        {
            GroupsConnector.UpdateRole(requestingAgentID, groupID, roleID, name, description, title, powers);
        }

        public GroupRecord GetGroupRecord(UUID requestingAgentID, UUID GroupID, string GroupName)
        {
            return GroupsConnector.GetGroupRecord(requestingAgentID, GroupID, GroupName);
        }

        public GroupProfileData GetMemberGroupProfile(UUID requestingAgentID, UUID GroupID, UUID AgentID)
        {
            GroupMembershipData MemberInfo = GroupsConnector.GetGroupMembershipData(requestingAgentID, GroupID, AgentID);
            GroupProfileData MemberGroupProfile = GroupsConnector.GetMemberGroupProfile(requestingAgentID, GroupID, AgentID);

            MemberGroupProfile.MemberTitle = MemberInfo.GroupTitle;
            MemberGroupProfile.PowersMask = MemberInfo.GroupPowers;

            return MemberGroupProfile;
        }

        public void SetAgentActiveGroup(UUID requestingAgentID, UUID AgentID, UUID GroupID)
        {
            GroupsConnector.SetAgentActiveGroup(AgentID, GroupID);
        }

        public void SetAgentActiveGroupRole(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            GroupsConnector.SetAgentGroupSelectedRole(AgentID, GroupID, RoleID);
        }

        public void SetAgentGroupInfo(UUID requestingAgentID, UUID AgentID, UUID GroupID, bool AcceptNotices, bool ListInProfile)
        {
            GroupsConnector.SetAgentGroupInfo(requestingAgentID, AgentID, GroupID, AcceptNotices ? 1 : 0, ListInProfile ? 1 : 0);
        }

        public void AddAgentToGroupInvite(UUID requestingAgentID, UUID inviteID, UUID groupID, UUID roleID, UUID agentID, string FromAgentName)
        {
            GroupsConnector.AddAgentGroupInvite(requestingAgentID, inviteID, groupID, roleID, agentID, FromAgentName);
        }

        public GroupInviteInfo GetAgentToGroupInvite(UUID requestingAgentID, UUID inviteID)
        {
            return GroupsConnector.GetAgentToGroupInvite(requestingAgentID, inviteID);
        }

        public void RemoveAgentToGroupInvite(UUID requestingAgentID, UUID inviteID)
        {
            GroupsConnector.RemoveAgentInvite(requestingAgentID, inviteID);
        }

        public void AddAgentToGroup(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            GroupsConnector.AddAgentToGroup(requestingAgentID, AgentID, GroupID, RoleID);
        }

        public bool RemoveAgentFromGroup(UUID requestingAgentID, UUID AgentID, UUID GroupID)
        {
            return GroupsConnector.RemoveAgentFromGroup(requestingAgentID, AgentID, GroupID);
        }

        public void AddAgentToGroupRole(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            GroupsConnector.AddAgentToRole(requestingAgentID, AgentID, GroupID, RoleID);
        }

        public void RemoveAgentFromGroupRole(UUID requestingAgentID, UUID AgentID, UUID GroupID, UUID RoleID)
        {
            GroupsConnector.RemoveAgentFromRole(requestingAgentID, AgentID, GroupID, RoleID);
        }

        public List<DirGroupsReplyData> FindGroups(UUID requestingAgentID, string search, int queryStart, uint queryflags)
        {
            //TODO: Fix this.. should be in the search module
            return GroupsConnector.FindGroups(requestingAgentID, search, queryStart, queryflags);
        }

        public GroupMembershipData GetAgentGroupMembership(UUID requestingAgentID, UUID GroupID, UUID AgentID)
        {
            return GroupsConnector.GetGroupMembershipData(requestingAgentID, GroupID, AgentID);
        }

        public GroupMembershipData GetAgentActiveMembership(UUID requestingAgentID, UUID AgentID)
        {
            return GroupsConnector.GetGroupMembershipData(requestingAgentID, GroupsConnector.GetAgentActiveGroup(requestingAgentID, AgentID), AgentID);
        }

        public List<GroupMembershipData> GetAgentGroupMemberships(UUID requestingAgentID, UUID AgentID)
        {
            return GroupsConnector.GetAgentGroupMemberships(requestingAgentID, AgentID);
        }

        public List<GroupRolesData> GetAgentGroupRoles(UUID requestingAgentID, UUID AgentID, UUID GroupID)
        {
            return GroupsConnector.GetAgentGroupRoles(requestingAgentID, AgentID, GroupID);
        }

        public List<GroupRolesData> GetGroupRoles(UUID requestingAgentID, UUID GroupID)
        {
            return GroupsConnector.GetGroupRoles(requestingAgentID, GroupID);
        }

        public List<GroupMembersData> GetGroupMembers(UUID requestingAgentID, UUID GroupID)
        {
            return GroupsConnector.GetGroupMembers(requestingAgentID, GroupID);
        }

        public List<GroupRoleMembersData> GetGroupRoleMembers(UUID requestingAgentID, UUID GroupID)
        {
            return GroupsConnector.GetGroupRoleMembers(requestingAgentID, GroupID);
        }

        public List<GroupNoticeData> GetGroupNotices(UUID requestingAgentID, UUID GroupID)
        {
            return GroupsConnector.GetGroupNotices(requestingAgentID, GroupID);
        }

        public GroupNoticeInfo GetGroupNotice(UUID requestingAgentID, UUID noticeID)
        {
            return GroupsConnector.GetGroupNotice(requestingAgentID, noticeID);
        }

        public void AddGroupNotice(UUID requestingAgentID, UUID groupID, UUID noticeID, string fromName, string subject, string message, UUID ItemID, int AssetType, string ItemName)
        {
            GroupsConnector.AddGroupNotice(requestingAgentID, groupID, noticeID, fromName, subject, message, ItemID, AssetType, ItemName);
        }

        public void AddGroupProposal(UUID agentID, GroupProposalInfo info)
        {
            GroupsConnector.AddGroupProposal(agentID, info);
        }

        #endregion

        public List<GroupInviteInfo> GetGroupInvites(UUID requestingAgentID)
        {
            return GroupsConnector.GetGroupInvites(requestingAgentID);
        }

        private Dictionary<UUID, ChatSession> ChatSessions = new Dictionary<UUID, ChatSession>();
        /// <summary>
        /// Add this member to the friend conference
        /// </summary>
        /// <param name="member"></param>
        /// <param name="SessionID"></param>
        public void AddMemberToGroup(ChatSessionMember member, UUID SessionID)
        {
            ChatSession session;
            ChatSessions.TryGetValue(SessionID, out session);
            ChatSessionMember oldMember = FindMember(SessionID, member.AvatarKey);
            if ((oldMember == null) || (oldMember.AvatarKey == UUID.Zero))
                session.Members.Add(member);
            else
                oldMember.HasBeenAdded = true;//Reset this
        }

        /// <summary>
        /// Create a new friend conference session
        /// </summary>
        /// <param name="session"></param>
        public bool CreateSession(ChatSession session)
        {
            ChatSession oldSession = null;
            if (ChatSessions.TryGetValue(session.SessionID, out oldSession))
                if (GetMemeberCount(session) == 0)
                    RemoveSession(session.SessionID);
                else
                    return false;//Already have one
            ChatSessions.Add(session.SessionID, session);
            return true;
        }

        private int GetMemeberCount(ChatSession session)
        {
            int count = 0;
            foreach (ChatSessionMember member in session.Members)
            {
                if (member.HasBeenAdded)
                    count++;
            }
            return count;
        }

        public void RemoveSession(UUID sessionid)
        {
            ChatSessions.Remove(sessionid);
        }

        /// <summary>
        /// Get a session by a user's sessionID
        /// </summary>
        /// <param name="SessionID"></param>
        /// <returns></returns>
        public ChatSession GetSession(UUID SessionID)
        {
            ChatSession session;
            ChatSessions.TryGetValue(SessionID, out session);
            return session;
        }

        /// <summary>
        /// Add the agent to the in-memory session lists and give them the default permissions
        /// </summary>
        /// <param name="AgentID"></param>
        /// <param name="SessionID"></param>
        private void AddDefaultPermsMemberToSession(UUID AgentID, UUID SessionID)
        {
            ChatSession session;
            ChatSessions.TryGetValue(SessionID, out session);
            ChatSessionMember member = new ChatSessionMember()
            {
                AvatarKey = AgentID,
                CanVoiceChat = true,
                IsModerator = false,
                MuteText = false,
                MuteVoice = false,
                HasBeenAdded = false
            };
            session.Members.Add(member);
        }

        /// <summary>
        /// Find the member from X sessionID 
        /// </summary>
        /// <param name="sessionid"></param>
        /// <param name="Agent"></param>
        /// <returns></returns>
        public ChatSessionMember FindMember(UUID sessionid, UUID Agent)
        {
            ChatSession session;
            ChatSessions.TryGetValue(sessionid, out session);
            if (session == null)
                return null;
            ChatSessionMember thismember = new ChatSessionMember() { AvatarKey = UUID.Zero };
            foreach (ChatSessionMember testmember in session.Members)
            {
                if (testmember.AvatarKey == Agent)
                    thismember = testmember;
            }
            return thismember;
        }
    }
}

/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
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
using System.Net;
using System.Reflection;

using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Services.Interfaces;
using Aurora.Simulation.Base;
using OpenSim.Framework.Servers.HttpServer;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;

using Nini.Config;
using log4net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Services.LLLoginService
{
    public class LoginResponseEnum
    {
        public static string PasswordIncorrect = "key"; //Password is wrong
        public static string InternalError = "Internal Error"; //Something inside went wrong
        public static string MessagePopup = "critical"; //Makes a message pop up in the viewer
        public static string ToSNeedsSent = "tos"; //Pops up the ToS acceptance box
        public static string Update = "update"; //Informs the client that they must update the viewer to login
        public static string OptionalUpdate = "optional"; //Informs the client that they have an optional update
        public static string PresenceIssue = "presence"; //Used by opensim to tell the viewer that the agent is already logged in
        public static string OK = "true"; //Login went fine
        public static string Indeterminant = "indeterminate"; //Unknown exactly what this does
        public static string Redirect = "redirect"; //Redirect! TBA!
    }

    public class LLFailedLoginResponse : LoginResponse
    {
        protected string m_key;
        protected string m_value;
        protected bool m_login;

        public static LLFailedLoginResponse AuthenticationProblem;
        public static LLFailedLoginResponse AccountProblem;
        public static LLFailedLoginResponse GridProblem;
        public static LLFailedLoginResponse InventoryProblem;
        public static LLFailedLoginResponse DeadRegionProblem;
        public static LLFailedLoginResponse LoginBlockedProblem;
        public static LLFailedLoginResponse AlreadyLoggedInProblem;
        public static LLFailedLoginResponse InternalError;
        public static LLFailedLoginResponse PermanentBannedProblem;

        static LLFailedLoginResponse()
        {
            AuthenticationProblem = new LLFailedLoginResponse(LoginResponseEnum.PasswordIncorrect,
                "Could not authenticate your avatar. Please check your username and password, and check the grid if problems persist.",
                false);
            AccountProblem = new LLFailedLoginResponse(LoginResponseEnum.PasswordIncorrect,
                "Could not find an account for your avatar. Please check that your username is correct or make a new account.",
                false);
            PermanentBannedProblem = new LLFailedLoginResponse(LoginResponseEnum.PasswordIncorrect,
                "You have been blocked from using this service.",
                false);
            GridProblem = new LLFailedLoginResponse(LoginResponseEnum.InternalError,
                "Error connecting to the desired location. Try connecting to another region.",
                false);
            InventoryProblem = new LLFailedLoginResponse(LoginResponseEnum.InternalError,
                "The inventory service is not responding.  Please notify your login region operator.",
                false);
            DeadRegionProblem = new LLFailedLoginResponse(LoginResponseEnum.InternalError,
                "The region you are attempting to log into is not responding. Please select another region and try again.",
                false);
            LoginBlockedProblem = new LLFailedLoginResponse(LoginResponseEnum.InternalError,
                "Logins are currently restricted. Please try again later.",
                false);
            AlreadyLoggedInProblem = new LLFailedLoginResponse(LoginResponseEnum.PresenceIssue,
                "You appear to be already logged in. " +
                "If this is not the case please wait for your session to timeout. " +
                "If this takes longer than a few minutes please contact the grid owner. " +
                "Please wait 5 minutes if you are going to connect to a region nearby to the region you were at previously.",
                false);
            InternalError = new LLFailedLoginResponse(LoginResponseEnum.InternalError, "Error generating Login Response", false);
        }

        public LLFailedLoginResponse(string key, string value, bool login)
        {
            m_key = key;
            m_value = value;
            m_login = login;
        }

        public override Hashtable ToHashtable()
        {
            Hashtable loginError = new Hashtable();
            loginError["reason"] = m_key;
            loginError["message"] = m_value;
            loginError["login"] = m_login.ToString().ToLower();
            return loginError;
        }

        public override OSD ToOSDMap()
        {
            OSDMap map = new OSDMap();

            map["reason"] = OSD.FromString(m_key);
            map["message"] = OSD.FromString(m_value);
            map["login"] = OSD.FromString(m_login.ToString().ToLower());

            return map;
        }
    }

    /// <summary>
    /// A class to handle LL login response.
    /// </summary>
    public class LLLoginResponse : OpenSim.Services.Interfaces.LoginResponse
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Hashtable loginFlagsHash;
        private Hashtable uiConfigHash;

        private ArrayList loginFlags;
        private ArrayList eventCategories;
        private ArrayList uiConfig;
        private ArrayList classifiedCategories;
        private ArrayList inventoryRoot;
        private ArrayList initialOutfit;
        private ArrayList agentInventory;
        private ArrayList activeGestures;
        private ArrayList tutorial = new ArrayList();

        private UUID agentID;
        private UUID sessionID;
        private UUID secureSessionID;

        // Login Flags
        private string dst;
        private string stipendSinceLogin;
        private string gendered;
        private string everLoggedIn;
        private string login;
        private uint simPort;
        private uint simHttpPort;
        private string simAddress;
        private string agentAccess;
        private string agentAccessMax;
        private Int32 circuitCode;
        private uint regionX;
        private uint regionY;
        private int regionSizeX;
        private int regionSizeY;

        // Login
        private string firstname;
        private string lastname;
        private string displayName;

        // Error Flags
        private string errorReason;
        private string errorMessage;
        private string startLocation;
        private string home;
        private string seedCapability;
        private string lookAt;
        private string udpBlackList;
        private IConfigSource m_source;

        private BuddyList m_buddyList = null;
        private IRegistryCore m_registry;

        public LLLoginResponse()
        {
            login = "true";
            ErrorMessage = "";
            ErrorReason = LoginResponseEnum.OK;
            loginFlags = new ArrayList();
            eventCategories = new ArrayList();
            uiConfig = new ArrayList();
            classifiedCategories = new ArrayList();

            uiConfigHash = new Hashtable();

            inventoryRoot = new ArrayList();
            initialOutfit = new ArrayList();
            agentInventory = new ArrayList();
            activeGestures = new ArrayList();

            SetDefaultValues();
        }

        public LLLoginResponse(UserAccount account, AgentCircuitData aCircuit, OpenSim.Services.Interfaces.UserInfo pinfo,
            GridRegion destination, List<InventoryFolderBase> invSkel, FriendInfo[] friendsList, IInventoryService invService, ILibraryService libService,
            string where, string startlocation, Vector3 position, Vector3 lookAt, List<InventoryItemBase> gestures,
            GridRegion home, IPEndPoint clientIP, string AdultMax, string AdultRating,
            ArrayList eventValues, ArrayList classifiedValues, string seedCap, IConfigSource source, string DisplayName, IRegistryCore registry)
            : this()
        {
            m_registry = registry;
            m_source = source;
            SeedCapability = seedCap;

            FillOutInventoryData(invSkel, libService, invService);

            FillOutActiveGestures(gestures);

            CircuitCode = (int)aCircuit.circuitcode;
            Lastname = account.LastName;
            Firstname = account.FirstName;
            this.DisplayName = DisplayName;
            AgentID = account.PrincipalID;
            SessionID = aCircuit.SessionID;
            SecureSessionID = aCircuit.SecureSessionID;
            BuddList = ConvertFriendListItem(friendsList);
            StartLocation = where;
            AgentAccessMax = AdultMax;
            AgentAccess = AdultRating;
            eventCategories = eventValues;
            classifiedCategories = classifiedValues;

            FillOutHomeData(pinfo, home);
            LookAt = String.Format("[r{0},r{1},r{2}]", lookAt.X, lookAt.Y, lookAt.Z);

            FillOutRegionData (aCircuit, destination);
            login = "true";
            ErrorMessage = "";
            ErrorReason = LoginResponseEnum.OK;
        }

        #region FillOutData

        private void FillOutInventoryData(List<InventoryFolderBase> invSkel, ILibraryService libService, IInventoryService invService)
        {
            InventoryData inventData = null;

            try
            {
                inventData = GetInventorySkeleton(libService, invService, invSkel);
            }
            catch (Exception e)
            {
                m_log.WarnFormat(
                    "[LLLOGIN SERVICE]: Error processing inventory skeleton of agent {0} - {1}",
                    agentID, e);

                // ignore and continue
            }

            if (inventData != null)
            {
                ArrayList AgentInventoryArray = inventData.InventoryArray;

                Hashtable InventoryRootHash = new Hashtable();
                InventoryRootHash["folder_id"] = inventData.RootFolderID.ToString();
                InventoryRoot = new ArrayList();
                InventoryRoot.Add(InventoryRootHash);
                InventorySkeleton = AgentInventoryArray;
            }

            // Inventory Library Section
            if (libService != null && (InventoryLibraryOwner == null || InventoryLibRoot == null || InventoryLibRoot == null))
            {
                InventoryLibrary = new ArrayList ();
                InventoryLibraryOwner = new ArrayList ();
                InventoryLibRoot = new ArrayList ();

                InventoryLibraryOwner = GetLibraryOwner(libService);
                InventoryLibrary = GetInventoryLibrary(libService, invService);
                Hashtable InventoryLibRootHash = new Hashtable();
                InventoryLibRootHash["folder_id"] = "00000112-000f-0000-0000-000100bba000"; ;
                InventoryLibRoot.Add(InventoryLibRootHash);
            }
        }

        private void FillOutActiveGestures(List<InventoryItemBase> gestures)
        {
            ArrayList list = new ArrayList();
            if (gestures != null)
            {
                foreach (InventoryItemBase gesture in gestures)
                {
                    Hashtable item = new Hashtable();
                    item["item_id"] = gesture.ID.ToString();
                    item["asset_id"] = gesture.AssetID.ToString();
                    list.Add(item);
                }
            }
            ActiveGestures = list;
        }

        private void FillOutHomeData(OpenSim.Services.Interfaces.UserInfo pinfo, GridRegion home)
        {
            int x = 1000 * (int)Constants.RegionSize, y = 1000 * (int)Constants.RegionSize;
            if (home != null)
            {
                x = home.RegionLocX;
                y = home.RegionLocY;
            }

            Home = string.Format(
                        "{{'region_handle':[r{0},r{1}], 'position':[r{2},r{3},r{4}], 'look_at':[r{5},r{6},r{7}]}}",
                        x,
                        y,
                        pinfo.HomePosition.X, pinfo.HomePosition.Y, pinfo.HomePosition.Z,
                        pinfo.HomeLookAt.X, pinfo.HomeLookAt.Y, pinfo.HomeLookAt.Z);

        }

        private void FillOutRegionData(AgentCircuitData circuitData, GridRegion destination)
        {
            IPEndPoint endPoint = destination.ExternalEndPoint;
            //We don't need this anymore, we set this from what we get from the region
            //endPoint = Util.ResolveAddressForClient (endPoint, circuitData.ClientIPEndPoint);
            SimAddress = endPoint.Address.ToString();
            SimPort = (uint)circuitData.RegionUDPPort;
            RegionX = (uint)destination.RegionLocX;
            RegionY = (uint)destination.RegionLocY;
            RegionSizeX = destination.RegionSizeX;
            RegionSizeY = destination.RegionSizeY;
        }

        private void SetDefaultValues()
        {
            DST = TimeZone.CurrentTimeZone.IsDaylightSavingTime(DateTime.Now) ? "Y" : "N";
            StipendSinceLogin = "N";
            Gendered = "Y";
            EverLoggedIn = "Y";
            login = "false";
            firstname = "Test";
            lastname = "User";
            agentAccess = "M";
            agentAccessMax = "A";
            startLocation = "last";
            udpBlackList = "EnableSimulator,TeleportFinish,CrossedRegion,OpenCircuit";

            ErrorMessage = "You have entered an invalid name/password combination.  Check Caps/lock.";
            ErrorReason = LoginResponseEnum.PasswordIncorrect;
            SessionID = UUID.Random();
            SecureSessionID = UUID.Random();
            AgentID = UUID.Random();

            Hashtable InitialOutfitHash = new Hashtable();
            InitialOutfitHash["folder_name"] = "Nightclub Female";
            InitialOutfitHash["gender"] = "female";
            initialOutfit.Add(InitialOutfitHash);

            Hashtable TutorialHash = new Hashtable();
            TutorialHash["tutorial_url"] = TutorialURL;

            if (TutorialURL != "")
                TutorialHash["use_tutorial"] = "Y";
            else
                TutorialHash["use_tutorial"] = "";
            tutorial.Add(TutorialHash);
        }

        #endregion

        #region To***

        public override Hashtable ToHashtable()
        {
            try
            {
                Hashtable responseData = new Hashtable();

                loginFlagsHash = new Hashtable();
                loginFlagsHash["daylight_savings"] = DST;
                loginFlagsHash["stipend_since_login"] = StipendSinceLogin;
                loginFlagsHash["gendered"] = Gendered;
                loginFlagsHash["ever_logged_in"] = EverLoggedIn;
                loginFlags.Add(loginFlagsHash);

                responseData["first_name"] = Firstname;
                responseData["last_name"] = Lastname;
                responseData["display_name"] = DisplayName;
                responseData["agent_access"] = agentAccess;
                responseData["agent_access_max"] = agentAccessMax;
                responseData["udp_blacklist"] = udpBlackList;

                if (AllowFirstLife != null)
                    uiConfigHash["allow_first_life"] = AllowFirstLife;
                uiConfig.Add(uiConfigHash);

                responseData["sim_port"] = (Int32)SimPort;
                responseData["sim_ip"] = SimAddress;
                responseData["http_port"] = (Int32)SimHttpPort;

                responseData["agent_id"] = AgentID.ToString();
                responseData["session_id"] = SessionID.ToString();
                responseData["secure_session_id"] = SecureSessionID.ToString();
                responseData["circuit_code"] = CircuitCode;
                responseData["seconds_since_epoch"] = (Int32)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                responseData["login-flags"] = loginFlags;
                responseData["seed_capability"] = seedCapability;

                responseData["event_categories"] = eventCategories;
                responseData["event_notifications"] = new ArrayList(); // TODO: What is this?
                responseData["classified_categories"] = classifiedCategories;
                responseData["ui-config"] = uiConfig;
                responseData["export"] = AllowExportPermission ? "flag" : "";

                if (agentInventory != null)
                {
                    responseData["inventory-skeleton"] = agentInventory;
                    responseData["inventory-root"] = inventoryRoot;
                }
                responseData["inventory-skel-lib"] = InventoryLibrary;
                responseData["inventory-lib-root"] = InventoryLibRoot;
                responseData["gestures"] = activeGestures;
                responseData["inventory-lib-owner"] = InventoryLibraryOwner;
                responseData["initial-outfit"] = initialOutfit;
                responseData["tutorial_setting"] = tutorial;
                responseData["start_location"] = startLocation;
                responseData["home"] = home;
                responseData["look_at"] = lookAt;
                responseData["message"] = Message;
                responseData["region_x"] = (Int32)(RegionX);
                responseData["region_y"] = (Int32)(RegionY);
                responseData["region_size_x"] = (Int32)(RegionSizeX);
                responseData["region_size_y"] = (Int32)(RegionSizeY);

                #region Global Textures

                ArrayList globalTextures = new ArrayList();
                Hashtable globalTexturesHash = new Hashtable();
                globalTexturesHash["sun_texture_id"] = SunTexture;
                globalTexturesHash["cloud_texture_id"] = CloudTexture;
                globalTexturesHash["moon_texture_id"] = MoonTexture;
                globalTextures.Add(globalTexturesHash);
                responseData["global-textures"] = globalTextures;

                #endregion Global Textures

                if (SearchURL != String.Empty)
                    responseData["search"] = SearchURL;

                IMapService mapService = m_registry.RequestModuleInterface<IMapService> ();
                if (mapService != null)
                    responseData["map-server-url"] = mapService.GetURLOfMap ();
                else if (MapTileURL != String.Empty)
                    responseData["map-server-url"] = MapTileURL;

                if (WebProfileURL != String.Empty)
                    responseData["web_profile_url"] = WebProfileURL;

                if (HelpURL != String.Empty)
                    responseData["help_url_format"] = HelpURL;

                if (SnapshotConfigURL != String.Empty)
                    responseData["snapshot_config_url"] = SnapshotConfigURL;

                if (HelpURL != String.Empty)
                    responseData["help_url_format"] = HelpURL;

                if (OpenIDURL != String.Empty)
                    responseData["openid_url"] = OpenIDURL;

                if (MaxAgentGroups != 0)
                    responseData["max-agent-groups"] = MaxAgentGroups;
                else
                    responseData["max-agent-groups"] = 100;

                //Makes viewers crash...
                if (VoiceServerType != String.Empty)
                {
                    Hashtable voice_config = new Hashtable();
                    voice_config["VoiceServerType"] = VoiceServerType;
                    ArrayList list = new ArrayList();
                    list.Add(voice_config);
                    responseData["voice-config"] = list;
                }

                if (m_buddyList != null)
                {
                    responseData["buddy-list"] = m_buddyList.ToArray();
                }
                if (m_source != null)
                {
                    // we're mapping GridInfoService keys to 
                    // the ones expected by known viewers.
                    // hippo, imprudence, phoenix are known to work
                    IConfig gridInfo = m_source.Configs["GridInfoService"];
                    if (gridInfo.GetBoolean("SendGridInfoToViewerOnLogin", false))
                    {
                        string tmp;
                        tmp = gridInfo.GetString("gridname", String.Empty);
                        if (tmp != String.Empty) responseData["gridname"] = tmp;
                        tmp = gridInfo.GetString("login", String.Empty);
                        if (tmp != String.Empty) responseData["loginuri"] = tmp;

                        // alternate keys of the same thing. (note careful not to overwrite responsdata["welcome"]
                        tmp = gridInfo.GetString("loginpage", String.Empty);
                        if (tmp != String.Empty) responseData["loginpage"] = tmp;
                        tmp = gridInfo.GetString("welcome", String.Empty);
                        if (tmp != String.Empty) responseData["loginpage"] = tmp;

                        // alternate keys of the same thing.
                        tmp = gridInfo.GetString("economy", String.Empty);
                        if (tmp != String.Empty) responseData["economy"] = tmp;
                        tmp = gridInfo.GetString("helperuri", String.Empty);
                        if (tmp != String.Empty) responseData["helperuri"] = tmp;

                        tmp = gridInfo.GetString("about", String.Empty);
                        if (tmp != String.Empty) responseData["about"] = tmp;
                        tmp = gridInfo.GetString("help", String.Empty);
                        if (tmp != String.Empty) responseData["help"] = tmp;
                        tmp = gridInfo.GetString("register", String.Empty);
                        if (tmp != String.Empty) responseData["register"] = tmp;
                        tmp = gridInfo.GetString("password", String.Empty);
                        if (tmp != String.Empty) responseData["password"] = tmp;
                        tmp = gridInfo.GetString("CurrencySymbol", String.Empty);
                        if (tmp != String.Empty) responseData["currency"] = tmp;
                        tmp = gridInfo.GetString("RealCurrencySymbol", String.Empty);
                        if (tmp != String.Empty) responseData["real_currency"] = tmp;
                        tmp = gridInfo.GetString("DirectoryFee", String.Empty);
                        if (tmp != String.Empty) responseData["directory_fee"] = tmp;
                        tmp = gridInfo.GetString("MaxGroups", String.Empty);
                        if (tmp != String.Empty) responseData["max_groups"] = tmp;
                    }
                }

                responseData["login"] = "true";

                return responseData;
            }
            catch (Exception e)
            {
                m_log.Warn("[CLIENT]: LoginResponse: Error creating Hashtable Response: " + e.ToString());

                return LLFailedLoginResponse.InternalError.ToHashtable();
            }
        }

        public override OSD ToOSDMap()
        {
            try
            {
                OSDMap map = new OSDMap();

                map["first_name"] = OSD.FromString(Firstname);
                map["last_name"] = OSD.FromString(Lastname);
                map["display_name"] = OSD.FromString(DisplayName);
                map["agent_access"] = OSD.FromString(agentAccess);
                map["agent_access_max"] = OSD.FromString(agentAccessMax);

                map["sim_port"] = OSD.FromInteger(SimPort);
                map["sim_ip"] = OSD.FromString(SimAddress);

                map["agent_id"] = OSD.FromUUID(AgentID);
                map["session_id"] = OSD.FromUUID(SessionID);
                map["secure_session_id"] = OSD.FromUUID(SecureSessionID);
                map["circuit_code"] = OSD.FromInteger(CircuitCode);
                map["seconds_since_epoch"] = OSD.FromInteger((int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);

                #region Login Flags

                OSDMap loginFlagsLLSD = new OSDMap();
                loginFlagsLLSD["daylight_savings"] = OSD.FromString(DST);
                loginFlagsLLSD["stipend_since_login"] = OSD.FromString(StipendSinceLogin);
                loginFlagsLLSD["gendered"] = OSD.FromString(Gendered);
                loginFlagsLLSD["ever_logged_in"] = OSD.FromString(EverLoggedIn);
                map["login-flags"] = WrapOSDMap(loginFlagsLLSD);

                #endregion Login Flags

                #region Global Textures

                OSDMap globalTexturesLLSD = new OSDMap();
                globalTexturesLLSD["sun_texture_id"] = OSD.FromString(SunTexture);
                globalTexturesLLSD["cloud_texture_id"] = OSD.FromString(CloudTexture);
                globalTexturesLLSD["moon_texture_id"] = OSD.FromString(MoonTexture);

                map["global-textures"] = WrapOSDMap(globalTexturesLLSD);

                #endregion Global Textures

                map["seed_capability"] = OSD.FromString(seedCapability);

                map["event_categories"] = ArrayListToOSDArray(eventCategories);
                //map["event_notifications"] = new OSDArray(); // todo
                map["classified_categories"] = ArrayListToOSDArray(classifiedCategories);

                #region UI Config

                OSDMap uiConfigLLSD = new OSDMap();
                if (AllowFirstLife != null)
                    uiConfigLLSD["allow_first_life"] = OSD.FromString(AllowFirstLife);
                map["ui-config"] = WrapOSDMap(uiConfigLLSD);

                #endregion UI Config

                #region Inventory

                map["inventory-skeleton"] = ArrayListToOSDArray(agentInventory);

                map["inventory-skel-lib"] = ArrayListToOSDArray (InventoryLibrary);
                map["inventory-root"] = ArrayListToOSDArray(inventoryRoot); ;
                map["inventory-lib-root"] = ArrayListToOSDArray (InventoryLibRoot);
                map["inventory-lib-owner"] = ArrayListToOSDArray (InventoryLibraryOwner);

                #endregion Inventory

                map["gestures"] = ArrayListToOSDArray(activeGestures);

                map["initial-outfit"] = ArrayListToOSDArray(initialOutfit);
                map["tutorial_setting"] = ArrayListToOSDArray(tutorial);
                map["start_location"] = OSD.FromString(startLocation);
                map["udp_blacklist"] = OSD.FromString(udpBlackList);

                map["seed_capability"] = OSD.FromString(seedCapability);
                map["home"] = OSD.FromString(home);
                map["look_at"] = OSD.FromString(lookAt);
                map["message"] = OSD.FromString(Message);
                map["region_x"] = OSD.FromInteger(RegionX);
                map["region_y"] = OSD.FromInteger(RegionY);

                if (MapTileURL != String.Empty)
                    map["map-server-url"] = OSD.FromString(MapTileURL);

                if (WebProfileURL != String.Empty)
                    map["web_profile_url"] = WebProfileURL;

                if (HelpURL != String.Empty)
                    map["help_url_format"] = HelpURL;

                if (SnapshotConfigURL != String.Empty)
                    map["snapshot_config_url"] = SnapshotConfigURL;

                if (HelpURL != String.Empty)
                    map["help_url_format"] = HelpURL;

                if (OpenIDURL != String.Empty)
                    map["openid_url"] = OpenIDURL;

                if (MaxAgentGroups != 0)
                    map["max-agent-groups"] = MaxAgentGroups;
                else
                    map["max-agent-groups"] = 100;

                if (VoiceServerType != String.Empty)
                {
                    OSDMap voice_config = new OSDMap();
                    voice_config["VoiceServerType"] = OSD.FromString(VoiceServerType);
                    map["voice-config"] = voice_config;
                }

                if (SearchURL != String.Empty)
                    map["search"] = OSD.FromString(SearchURL);

                if (m_buddyList != null)
                {
                    map["buddy-list"] = ArrayListToOSDArray(m_buddyList.ToArray());
                }

                map["login"] = OSD.FromString("true");

                return map;
            }
            catch (Exception e)
            {
                m_log.Warn("[CLIENT]: LoginResponse: Error creating LLSD Response: " + e.Message);

                return LLFailedLoginResponse.InternalError.ToOSDMap();
            }
        }

        public OSDArray ArrayListToOSDArray(ArrayList arrlst)
        {
            OSDArray llsdBack = new OSDArray();
            foreach (Hashtable ht in arrlst)
            {
                OSDMap mp = new OSDMap();
                foreach (DictionaryEntry deHt in ht)
                {
                    mp.Add((string)deHt.Key, OSDString.FromObject(deHt.Value));
                }
                llsdBack.Add(mp);
            }
            return llsdBack;
        }

        private static OSDArray WrapOSDMap(OSDMap wrapMe)
        {
            OSDArray array = new OSDArray();
            array.Add(wrapMe);
            return array;
        }

        public void AddToUIConfig(string itemName, string item)
        {
            uiConfigHash[itemName] = item;
        }

        private static LLLoginResponse.BuddyList ConvertFriendListItem(FriendInfo[] friendsList)
        {
            LLLoginResponse.BuddyList buddylistreturn = new LLLoginResponse.BuddyList();
            foreach (FriendInfo finfo in friendsList)
            {
                if (finfo.TheirFlags == -1)
                    continue;
                LLLoginResponse.BuddyList.BuddyInfo buddyitem = new LLLoginResponse.BuddyList.BuddyInfo(finfo.Friend);
                buddyitem.BuddyID = finfo.Friend;
                buddyitem.BuddyRightsHave = (int)finfo.TheirFlags;
                buddyitem.BuddyRightsGiven = (int)finfo.MyFlags;
                buddylistreturn.AddNewBuddy(buddyitem);
            }
            return buddylistreturn;
        }

        private InventoryData GetInventorySkeleton(ILibraryService library, IInventoryService inventoryService, List<InventoryFolderBase> folders)
        {
            UUID rootID = UUID.Zero;
            ArrayList AgentInventoryArray = new ArrayList();
            Hashtable TempHash;
            foreach (InventoryFolderBase InvFolder in folders)
            {
                if (InvFolder.ParentID == UUID.Zero && InvFolder.Name == "My Inventory")
                    rootID = InvFolder.ID;
                TempHash = new Hashtable();
                TempHash["name"] = InvFolder.Name;
                TempHash["parent_id"] = InvFolder.ParentID.ToString();
                TempHash["version"] = (Int32)InvFolder.Version;
                TempHash["type_default"] = (Int32)InvFolder.Type;
                TempHash["folder_id"] = InvFolder.ID.ToString();
                AgentInventoryArray.Add(TempHash);
            }
            return new InventoryData(AgentInventoryArray, rootID);

        }

        /// <summary>
        /// Converts the inventory library skeleton into the form required by the rpc request.
        /// </summary>
        /// <returns></returns>
        protected virtual ArrayList GetInventoryLibrary(ILibraryService library, IInventoryService inventoryService)
        {
            ArrayList AgentInventoryArray = new ArrayList();
            List<InventoryFolderBase> rootFolders = inventoryService.GetRootFolders(library.LibraryOwner);
            Hashtable RootHash = new Hashtable();
            RootHash["name"] = library.LibraryName;
            RootHash["parent_id"] = UUID.Zero.ToString();
            RootHash["version"] = (Int32)1;
            RootHash["type_default"] = (Int32)9;
            RootHash["folder_id"] = "00000112-000f-0000-0000-000100bba000";
            AgentInventoryArray.Add(RootHash);

            List<UUID> rootFolderUUIDs = new List<UUID>();
            foreach (InventoryFolderBase rootFolder in rootFolders)
            {
                if (rootFolder.Name != "My Inventory")
                    rootFolderUUIDs.Add(rootFolder.ID);
            }

            if (rootFolderUUIDs.Count != 0)
            {
                foreach (UUID rootfolderID in rootFolderUUIDs)
                {
                    TraverseFolder(library.LibraryOwner, rootfolderID, inventoryService, true, ref AgentInventoryArray);
                }
            }
            return AgentInventoryArray;
        }

        private void TraverseFolder(UUID agentID, UUID folderID, IInventoryService invService, bool rootFolder, ref ArrayList table)
        {
            List<InventoryFolderBase> folders = invService.GetFolderFolders(agentID, folderID);
            foreach (InventoryFolderBase folder in folders)
            {
                Hashtable TempHash = new Hashtable();
                TempHash["name"] = folder.Name;
                if (rootFolder)
                    TempHash["parent_id"] = "00000112-000f-0000-0000-000100bba000";
                else
                    TempHash["parent_id"] = folder.ParentID.ToString();
                TempHash["version"] = (Int32)folder.Version;
                TempHash["type_default"] = (Int32)folder.Type;
                TempHash["folder_id"] = folder.ID.ToString();
                table.Add(TempHash);
                TraverseFolder(agentID, folder.ID, invService, false, ref table);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        protected virtual ArrayList GetLibraryOwner(ILibraryService libService)
        {
            //for now create random inventory library owner
            Hashtable TempHash = new Hashtable();
            TempHash["agent_id"] = libService.LibraryOwner.ToString(); // libFolder.Owner
            ArrayList inventoryLibOwner = new ArrayList();
            inventoryLibOwner.Add(TempHash);
            return inventoryLibOwner;
        }

        public class InventoryData
        {
            public ArrayList InventoryArray = null;
            public UUID RootFolderID = UUID.Zero;

            public InventoryData(ArrayList invList, UUID rootID)
            {
                InventoryArray = invList;
                RootFolderID = rootID;
            }
        }

        #endregion

        #region Properties

        public string Login
        {
            get { return login; }
            set { login = value; }
        }

        public string DST
        {
            get { return dst; }
            set { dst = value; }
        }

        public string StipendSinceLogin
        {
            get { return stipendSinceLogin; }
            set { stipendSinceLogin = value; }
        }

        public string Gendered
        {
            get { return gendered; }
            set { gendered = value; }
        }

        public string EverLoggedIn
        {
            get { return everLoggedIn; }
            set { everLoggedIn = value; }
        }

        public uint SimPort
        {
            get { return simPort; }
            set { simPort = value; }
        }

        public uint SimHttpPort
        {
            get { return simHttpPort; }
            set { simHttpPort = value; }
        }

        public string SimAddress
        {
            get { return simAddress; }
            set { simAddress = value; }
        }

        public UUID AgentID
        {
            get { return agentID; }
            set { agentID = value; }
        }

        public UUID SessionID
        {
            get { return sessionID; }
            set { sessionID = value; }
        }

        public UUID SecureSessionID
        {
            get { return secureSessionID; }
            set { secureSessionID = value; }
        }

        public Int32 CircuitCode
        {
            get { return circuitCode; }
            set { circuitCode = value; }
        }

        public uint RegionX
        {
            get { return regionX; }
            set { regionX = value; }
        }

        public uint RegionY
        {
            get { return regionY; }
            set { regionY = value; }
        }

        public int RegionSizeX
        {
            get { return regionSizeX; }
            set { regionSizeX = value; }
        }

        public int RegionSizeY
        {
            get { return regionSizeY; }
            set { regionSizeY = value; }
        }

        public string Firstname
        {
            get { return firstname; }
            set { firstname = value; }
        }

        public string DisplayName
        {
            get { return displayName; }
            set { displayName = value; }
        }

        public string Lastname
        {
            get { return lastname; }
            set { lastname = value; }
        }

        public string AgentAccess
        {
            get { return agentAccess; }
            set { agentAccess = value; }
        }

        public string AgentAccessMax
        {
            get { return agentAccessMax; }
            set { agentAccessMax = value; }
        }

        public string StartLocation
        {
            get { return startLocation; }
            set { startLocation = value; }
        }

        public string LookAt
        {
            get { return lookAt; }
            set { lookAt = value; }
        }

        public string SeedCapability
        {
            get { return seedCapability; }
            set { seedCapability = value; }
        }

        public string ErrorReason
        {
            get { return errorReason; }
            set { errorReason = value; }
        }

        public string ErrorMessage
        {
            get { return errorMessage; }
            set { errorMessage = value; }
        }

        public ArrayList InventoryRoot
        {
            get { return inventoryRoot; }
            set { inventoryRoot = value; }
        }

        public ArrayList InventorySkeleton
        {
            get { return agentInventory; }
            set { agentInventory = value; }
        }

        public static ArrayList InventoryLibrary;

        public static ArrayList InventoryLibraryOwner;

        public static ArrayList InventoryLibRoot;

        public ArrayList ActiveGestures
        {
            get { return activeGestures; }
            set { activeGestures = value; }
        }

        public string Home
        {
            get { return home; }
            set { home = value; }
        }

        public string SunTexture
        {
            get { return (string)LLLoginResponseRegister.GetValue("SunTexture"); }
        }

        public string CloudTexture
        {
            get { return (string)LLLoginResponseRegister.GetValue("CloudTexture"); }
        }

        public string MoonTexture
        {
            get { return (string)LLLoginResponseRegister.GetValue("MoonTexture"); }
        }

        public string AllowFirstLife
        {
            get { return (string)LLLoginResponseRegister.GetValue("AllowFirstLife"); }
        }

        public bool AllowExportPermission
        {
            get { return (bool)LLLoginResponseRegister.GetValue("AllowExportPermission"); }
        }

        public string OpenIDURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("OpenIDURL"); }
        }

        public string SnapshotConfigURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("SnapshotConfigURL"); }
        }

        public string HelpURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("HelpURL"); }
        }

        public int MaxAgentGroups
        {
            get { return (int)LLLoginResponseRegister.GetValue("MaxAgentGroups"); }
        }

        public string VoiceServerType
        {
            get { return (string)LLLoginResponseRegister.GetValue("VoiceServerType"); }
        }

        public string TutorialURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("TutorialURL"); }
        }

        public string MapTileURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("MapTileURL"); }
        }

        public string SearchURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("SearchURL"); }
        }

        public string WebProfileURL
        {
            get { return (string)LLLoginResponseRegister.GetValue("WebProfileURL"); }
        }

        public string Message
        {
            get
            {
                string retVal = (string)LLLoginResponseRegister.GetValue("Message");
                if (retVal.Contains("<USERNAME>"))
                    retVal = retVal.Replace("<USERNAME>", firstname + " " + lastname);
                return retVal;
            }
        }

        public BuddyList BuddList
        {
            get { return m_buddyList; }
            set { m_buddyList = value; }
        }

        #endregion

        public class UserInfo
        {
            public string firstname;
            public string lastname;
            public ulong homeregionhandle;
            public Vector3 homepos;
            public Vector3 homelookat;
        }

        public class BuddyList
        {
            public List<BuddyInfo> Buddies = new List<BuddyInfo>();

            public void AddNewBuddy(BuddyInfo buddy)
            {
                if (!Buddies.Contains(buddy))
                {
                    Buddies.Add(buddy);
                }
            }

            public ArrayList ToArray()
            {
                ArrayList buddyArray = new ArrayList();
                foreach (BuddyInfo buddy in Buddies)
                {
                    buddyArray.Add(buddy.ToHashTable());
                }
                return buddyArray;
            }

            public class BuddyInfo
            {
                public int BuddyRightsHave = 1;
                public int BuddyRightsGiven = 1;
                public string BuddyID;

                public BuddyInfo(string buddyID)
                {
                    BuddyID = buddyID;
                }

                public BuddyInfo(UUID buddyID)
                {
                    BuddyID = buddyID.ToString();
                }

                public Hashtable ToHashTable()
                {
                    Hashtable hTable = new Hashtable();
                    hTable["buddy_rights_has"] = BuddyRightsHave;
                    hTable["buddy_rights_given"] = BuddyRightsGiven;
                    hTable["buddy_id"] = BuddyID;
                    return hTable;
                }
            }
        }
    }

    /// <summary>
    /// A generic kvp register so that we can store values for multiple LLLoginResponses
    /// </summary>
    public class LLLoginResponseRegister
    {
        private static Dictionary<string, object> m_values = new Dictionary<string, object>();

        public static void RegisterValue(string key, object value)
        {
            m_values[key] = value;
        }

        public static object GetValue(string key)
        {
            if (m_values.ContainsKey(key))
                return m_values[key];
            return null;
        }
    }
}

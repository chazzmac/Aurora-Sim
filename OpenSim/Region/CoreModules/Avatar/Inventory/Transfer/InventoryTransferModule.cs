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
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.CoreModules.Avatar.Inventory.Transfer
{
    public class InventoryTransferModule : ISharedRegionModule
    {
        private static readonly ILog m_log
            = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        private List<IScene> m_Scenelist = new List<IScene> ();

        private IMessageTransferModule m_TransferModule = null;
        private bool m_Enabled = true;

        #region IRegionModule Members

        public void Initialise(IConfigSource config)
        {
            if (config.Configs["Messaging"] != null)
            {
                // Allow disabling this module in config
                //
                if (config.Configs["Messaging"].GetString(
                        "InventoryTransferModule", "InventoryTransferModule") !=
                        "InventoryTransferModule")
                {
                    m_Enabled = false;
                    return;
                }
            }
        }

        public void AddRegion (IScene scene)
        {
            if (!m_Enabled)
                return;

            m_Scenelist.Add(scene);

            scene.RegisterModuleInterface<InventoryTransferModule>(this);

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
            scene.EventManager.OnIncomingInstantMessage += OnGridInstantMessage;
        }

        public void RegionLoaded (IScene scene)
        {
            if (m_TransferModule == null)
            {
                m_TransferModule = m_Scenelist[0].RequestModuleInterface<IMessageTransferModule>();
                if (m_TransferModule == null)
                {
                    m_log.Error("[INVENTORY TRANSFER]: No Message transfer module found, transfers will be local only");
                    m_Enabled = false;

                    m_Scenelist.Clear();
                    scene.EventManager.OnNewClient -= OnNewClient;
                    scene.EventManager.OnClosingClient -= OnClosingClient;
                    scene.EventManager.OnIncomingInstantMessage -= OnGridInstantMessage;
                }
            }
        }

        public void RemoveRegion (IScene scene)
        {
            m_Scenelist.Remove (scene);
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
            scene.EventManager.OnIncomingInstantMessage -= OnGridInstantMessage;
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "InventoryModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        private void OnNewClient(IClientAPI client)
        {
            // Inventory giving is conducted via instant message
            client.OnInstantMessage += OnInstantMessage;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnInstantMessage -= OnInstantMessage;
        }

        private IScene FindClientScene(UUID agentId)
        {
            lock (m_Scenelist)
            {
                foreach (IScene scene in m_Scenelist)
                {
                    IScenePresence presence = scene.GetScenePresence (agentId);
                    if (presence != null)
                        return scene;
                }
            }
            return null;
        }

        private void OnInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            //m_log.InfoFormat("[INVENTORY TRANSFER]: OnInstantMessage {0}", im.dialog);
            
            IScene scene = FindClientScene(client.AgentId);

            if (scene == null) // Something seriously wrong here.
                return;

            if (im.dialog == (byte) InstantMessageDialog.InventoryOffered)
            {
                //m_log.DebugFormat("Asset type {0}", ((AssetType)im.binaryBucket[0]));

                if (im.binaryBucket.Length < 17) // Invalid
                    return;
            
                UUID receipientID = im.toAgentID;
                IScenePresence user = scene.GetScenePresence (receipientID);
                UUID copyID;

                // Send the IM to the recipient. The item is already
                // in their inventory, so it will not be lost if
                // they are offline.
                //
                if (user != null)
                {
                    // First byte is the asset type
                    AssetType assetType = (AssetType)im.binaryBucket[0];

                    if (AssetType.Folder == assetType)
                    {
                        UUID folderID = new UUID (im.binaryBucket, 1);

                        m_log.DebugFormat ("[INVENTORY TRANSFER]: Inserting original folder {0} " +
                                "into agent {1}'s inventory",
                                folderID, im.toAgentID);

                        InventoryFolderBase folderCopy = null;
                        ILLClientInventory inventoryModule = scene.RequestModuleInterface<ILLClientInventory> ();
                        if (inventoryModule != null)
                            folderCopy = inventoryModule.GiveInventoryFolder (receipientID, client.AgentId, folderID, UUID.Zero);

                        if (folderCopy == null)
                        {
                            client.SendAgentAlertMessage ("Can't find folder to give. Nothing given.", false);
                            return;
                        }

                        // The outgoing binary bucket should contain only the byte which signals an asset folder is
                        // being copied and the following bytes for the copied folder's UUID
                        copyID = folderCopy.ID;
                        byte[] copyIDBytes = copyID.GetBytes ();
                        im.binaryBucket = new byte[1 + copyIDBytes.Length];
                        im.binaryBucket[0] = (byte)AssetType.Folder;
                        Array.Copy (copyIDBytes, 0, im.binaryBucket, 1, copyIDBytes.Length);

                        if (user != null)
                        {
                            user.ControllingClient.SendBulkUpdateInventory (folderCopy);
                        }

                        im.imSessionID = folderID;
                    }
                    else
                    {
                        // First byte of the array is probably the item type
                        // Next 16 bytes are the UUID

                        UUID itemID = new UUID (im.binaryBucket, 1);

                        m_log.DebugFormat ("[INVENTORY TRANSFER]: (giving) Inserting item {0} " +
                                "into agent {1}'s inventory",
                                itemID, im.toAgentID);

                        InventoryItemBase itemCopy = null;
                        ILLClientInventory inventoryModule = scene.RequestModuleInterface<ILLClientInventory> ();
                        if (inventoryModule != null)
                            itemCopy = inventoryModule.GiveInventoryItem (
                                im.toAgentID,
                                im.fromAgentID, itemID, UUID.Zero);

                        if (itemCopy == null)
                        {
                            client.SendAgentAlertMessage ("Can't find item to give. Nothing given.", false);
                            return;
                        }

                        copyID = itemCopy.ID;
                        Array.Copy (copyID.GetBytes (), 0, im.binaryBucket, 1, 16);

                        if (user != null)
                        {
                            user.ControllingClient.SendBulkUpdateInventory (itemCopy);
                        }

                        im.imSessionID = itemID;
                    }

                    user.ControllingClient.SendInstantMessage (im);
                    return;
                }
                else
                {
                    if (m_TransferModule != null)
                        m_TransferModule.SendInstantMessage (im);
                }
            }
            else if (im.dialog == (byte) InstantMessageDialog.InventoryAccepted)
            {
                IScenePresence user = scene.GetScenePresence (im.toAgentID);

                if (user != null) // Local
                {
                    user.ControllingClient.SendInstantMessage(im);
                }
                else
                {
                    if (m_TransferModule != null)
                        m_TransferModule.SendInstantMessage(im);
                }
            }
            else if (im.dialog == (byte) InstantMessageDialog.InventoryDeclined)
            {
                // Here, the recipient is local and we can assume that the
                // inventory is loaded. Courtesy of the above bulk update,
                // It will have been pushed to the client, too
                //
                IInventoryService invService = scene.InventoryService;

                InventoryFolderBase trashFolder =
                    invService.GetFolderForType (client.AgentId, InventoryType.Unknown, AssetType.TrashFolder);

                UUID inventoryID = im.imSessionID; // The inventory item/folder, back from it's trip

                InventoryItemBase item = new InventoryItemBase(inventoryID, client.AgentId);
                item = invService.GetItem(item);
                InventoryFolderBase folder = null;
                
                if (item != null && trashFolder != null)
                {
                    item.Folder = trashFolder.ID;

                    // Diva comment: can't we just update this item???
                    List<UUID> uuids = new List<UUID>();
                    uuids.Add(item.ID);
                    invService.DeleteItems(item.Owner, uuids);
                    ILLClientInventory inventory = client.Scene.RequestModuleInterface<ILLClientInventory>();
                    if (inventory != null)
                        inventory.AddInventoryItem(client, item);
                }
                else
                {
                    folder = new InventoryFolderBase(inventoryID, client.AgentId);
                    folder = invService.GetFolder(folder);
                    
                    if (folder != null & trashFolder != null)
                    {
                        folder.ParentID = trashFolder.ID;
                        invService.MoveFolder(folder);
                    }
                }
                
                if ((null == item && null == folder) | null == trashFolder)
                {
                    string reason = String.Empty;
                    
                    if (trashFolder == null)
                        reason += " Trash folder not found.";
                    if (item == null)
                        reason += " Item not found.";
                    if (folder == null)
                        reason += " Folder not found.";
                    
                    client.SendAgentAlertMessage("Unable to delete "+
                            "received inventory" + reason, false);
                }

                IScenePresence user = scene.GetScenePresence (im.toAgentID);

                if (user != null) // Local
                {
                    user.ControllingClient.SendInstantMessage(im);
                }
                else
                {
                    if (m_TransferModule != null)
                        m_TransferModule.SendInstantMessage(im);
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="msg"></param>
        private void OnGridInstantMessage(GridInstantMessage msg)
        {
            // Check if this is ours to handle
            //
            IScene scene = FindClientScene(msg.toAgentID);

            if (scene == null)
                return;

            // Find agent to deliver to
            //
            IScenePresence user = scene.GetScenePresence (msg.toAgentID);

            // Just forward to local handling
            OnInstantMessage(user.ControllingClient, msg);

        }
    }
}

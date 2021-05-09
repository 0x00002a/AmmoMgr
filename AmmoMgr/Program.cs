﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        internal struct AmmoItemData
        {
            public IMyInventory Parent;
            public MyInventoryItem Item;
        }
        internal struct InventoryData
        {
            public bool Requester;
            public IMyInventory Inventory;
        }


        #region Constants
        internal const string AMMO_TYPE_NAME = "MyObjectBuilder_AmmoMagazine";
        internal const string VERSION = "0.1.1";
        #endregion

        #region Fields
        internal HashSet<MyDefinitionId> wc_weapons_ = new HashSet<MyDefinitionId>();
        internal Dictionary<IMyInventory, HashSet<MyItemType>> inv_allowlist_cache_ = new Dictionary<IMyInventory, HashSet<MyItemType>>();
        internal List<HashSet<InventoryData>> partitioned_invs_ = new List<HashSet<InventoryData>>();
        internal Dictionary<string, List<AmmoItemData>> avaliability_lookup_ = new Dictionary<string, List<AmmoItemData>>();
        internal List<string> actions_log_ = new List<string>();

        internal ulong ticks_10 = 0;

        internal Console console = new Console
        {
            ClearOnPrint = true,
            Header = $"== AmmoMgr v{VERSION} ==",
        };
        #endregion

        #region Util 

        internal static void SortItems(IMyInventory parent, List<MyInventoryItem> items, Dictionary<string, List<AmmoItemData>> readin)
        {
            foreach(var item in items)
            {
                if (item.Type.TypeId.ToString() == AMMO_TYPE_NAME)
                {
                    List<AmmoItemData> target_set;
                    var key = item.Type.SubtypeId.ToString();

                    var found = true;
                    if (!readin.TryGetValue(key, out target_set))
                    {
                        target_set = new List<AmmoItemData>();
                        found = false;
                    }

                    target_set.Add(new AmmoItemData { Item = item, Parent = parent });
                    if (!found)
                    {
                        readin.Add(key, target_set);
                    }
                }
            }
        }
        internal static void ClearLists<T, V>(Dictionary<T, List<V>> dict)
        {
            foreach (var val in dict.Values)
            {
                val.Clear();
            }
        }
       
        internal bool IsWeapon(IMyTerminalBlock entity)
        {
            if (entity is IMyUserControllableGun)
            {
                return true;
            } else
            {
                var id = new MyDefinitionId(entity.BlockDefinition.TypeId, entity.BlockDefinition.SubtypeId);

                return wc_weapons_.Contains(id);
            }
        }
        internal bool CanContainItem(IMyInventory inv, MyItemType ammo)
        {
            HashSet<MyItemType> allowed;
            if (!inv_allowlist_cache_.TryGetValue(inv, out allowed))
            {
                allowed = new HashSet<MyItemType>();
                inv.GetAcceptedItems(null, t => { allowed.Add(t); return false; });
                inv_allowlist_cache_.Add(inv, allowed);
            }

            return allowed.Contains(ammo);
        }
        #endregion

        #region Init

        internal static void ScanForWeapons(WcPbApi wc, ICollection<MyDefinitionId> readin)
        {
            wc.GetAllCoreStaticLaunchers(readin);
            wc.GetAllCoreTurrets(readin);
            wc.GetAllCoreWeapons(readin);

        }


        #endregion

        #region Running
        internal void WriteStatsToStdout()
        {
            console.Stdout.WriteLn($"Inventories: {partitioned_invs_.Sum(pre =>  pre.Count )}");

            foreach(var act in actions_log_)
            {
                console.Stdout.WriteLn(act);
            }

        }

        internal InventoryData CreateInvData(IMyInventory inv)
        {
            var is_requester = IsWeapon((IMyTerminalBlock)inv.Owner);

            var item = new InventoryData { Inventory = inv, Requester = is_requester };
            return item;

        }

        internal static string OwnerName(IMyInventory inv)
        {
            var owner = inv?.Owner as IMyTerminalBlock;
            return owner == null ? "" : owner.CustomName;
        }

        internal void RefreshInventories(List<HashSet<InventoryData>> readin)
        {
            var block_cache = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(block_cache);


            var flat_inventories = block_cache.Where(b => b.HasInventory).Select(b => b.GetInventory()).ToList();
            foreach(var inv in flat_inventories)
            {
                if (inv.Owner is IMyTerminalBlock)
                {
                    var item = CreateInvData(inv);
                    var parition = readin.FirstOrDefault(set => set.Contains(item));
                    if (parition == null)
                    {
                        parition = new HashSet<InventoryData>();
                        readin.Add(parition);
                    }
                    foreach(var peer in flat_inventories)
                    {
                        if (inv.IsConnectedTo(peer))
                        {
                            if (peer.Owner is IMyTerminalBlock)
                            {
                                parition.Add(CreateInvData(peer));
                            }
                        }
                    }
                }
            }
            
        }
        internal void AllotItems(double per_inv, List<AmmoItemData> avaliable, IEnumerable<IMyInventory> requesters)
        {
            
            var aval_head = 0;
            foreach(var inv in requesters)
            {
                var needed = per_inv;
                for (var i = aval_head; i < avaliable.Count; ++i)
                {
                    var target_item = avaliable[i];
                    if (target_item.Parent.IsConnectedTo(inv) && target_item.Parent != inv)
                    {
                        var aval = Math.Min(needed, (double)target_item.Item.Amount);

                        inv.TransferItemFrom(target_item.Parent, target_item.Item, (MyFixedPoint)aval);
                        actions_log_.Add($"{OwnerName(inv)} ({target_item.Item.Type.SubtypeId}) ({aval}) -> {OwnerName(target_item.Parent)}");

                        needed -= aval;
                        if (needed > 0)
                        {
                            ++aval_head;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

            }
        }

        internal IEnumerable<IMyInventory> FilterByCanContain(List<IMyInventory> src, MyItemType type)
        {
            return src.Where(i => CanContainItem(i, type));
        }

        internal void RebalanceInventories(List<HashSet<InventoryData>> requesters, Dictionary<string, List<AmmoItemData>> avaliable)
        {
            foreach (var ammo in avaliable)
            {
                if (ammo.Value.Count != 0)
                {

                    foreach (var inv_system in requesters)
                    {
                        var ammo_t = ammo.Value[0].Item.Type;
                        var eligable_invs = inv_system.Where(i => CanContainItem(i.Inventory, ammo_t));
                        var nb_req = eligable_invs.Count(i => i.Requester);
                        var total = eligable_invs.Select(i => (double)i.Inventory.GetItemAmount(ammo_t)).Sum();
                        var per_inv = total / nb_req;

                        var eligible_req = eligable_invs.Where(i => i.Requester).Select(i => i.Inventory);
                        AllotItems(per_inv, ammo.Value, eligible_req);

                    }
                }

            }


        }

        internal static void ScanInventories(List<HashSet<InventoryData>> inventories, Dictionary<string, List<AmmoItemData>> readin)
        {
            var items_tmp = new List<MyInventoryItem>();
            foreach(var part in inventories)
            {
                foreach (var inv in part)
                {
                    items_tmp.Clear();
                    inv.Inventory.GetItems(items_tmp);

                    SortItems(inv.Inventory, items_tmp, readin);
                }

            }
        }

        #endregion
        #region Cleanup

        #endregion

        #region Entry points
        public Program()
        {
            var wc = new WcPbApi();
            if (!wc.Activate(Me))
            {
                console.Persistout.WriteLn($"Failed to initalise WeaponCore, falling back to vanilla only");
            } else
            {
                ScanForWeapons(wc, wc_weapons_);
                console.Persistout.WriteLn($"Using WeaponCore weapons as well as vanilla");
            }
            

            Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Once;

        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if ((updateSource & UpdateType.Update10) == UpdateType.Update10)
            {
                ++ticks_10;

            }

            var is_oneshot = (updateSource & UpdateType.Once) == UpdateType.Once;
            if (is_oneshot || ticks_10 % 12 == 0) // Do a rescan every 2 minutes 
            {
                RefreshInventories(partitioned_invs_);
                
            } else if (ticks_10 % 3 == 0)
            {
                actions_log_.Clear();
                ClearLists(avaliability_lookup_);
                ScanInventories(partitioned_invs_, avaliability_lookup_);
                RebalanceInventories(partitioned_invs_, avaliability_lookup_);
            }

            WriteStatsToStdout();
            console.PrintOutput(this);


        }
        #endregion
    }
}

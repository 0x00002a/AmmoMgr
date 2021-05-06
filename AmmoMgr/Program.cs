using Sandbox.Game.EntityComponents;
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
        internal const string AMMO_TYPE_NAME = "MyObjectBuilder_AmmoMagazineDefinition";
        internal const string VERSION = "0.1.0";
        #endregion

        #region Fields
        internal List<IMyInventory> cached_inventories_ = new List<IMyInventory>();
        internal List<IMyInventory> requesters_ = new List<IMyInventory>();
        internal HashSet<MyDefinitionId> wc_weapons_ = new HashSet<MyDefinitionId>();
        internal Dictionary<IMyInventory, HashSet<MyItemType>> inv_allowlist_cache_ = new Dictionary<IMyInventory, HashSet<MyItemType>>();
        internal List<IMyInventory> connected_invs_cache_ = new List<IMyInventory>();
        internal List<HashSet<InventoryData>> partitioned_invs_ = new List<HashSet<InventoryData>>();

        internal Dictionary<MyInventoryItem, double> claimed_items_cache_ = new Dictionary<MyInventoryItem, double>();

        internal Dictionary<string, List<AmmoItemData>> avaliability_lookup_ = new Dictionary<string, List<AmmoItemData>>();

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
        internal InventoryData CreateInvData(IMyInventory inv)
        {
            var is_requester = IsWeapon((IMyTerminalBlock)inv.Owner);

            var item = new InventoryData { Inventory = inv, Requester = is_requester };
            return item;

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
                    if (target_item.Parent.IsConnectedTo(inv))
                    {
                        var aval = Math.Min(needed, (double)target_item.Item.Amount);

                        inv.TransferItemFrom(target_item.Parent, target_item.Item, (MyFixedPoint)aval);

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

        internal static void ScanInventories(List<IMyInventory> inventories, Dictionary<string, List<AmmoItemData>> readin)
        {
            var items_tmp = new List<MyInventoryItem>();
            foreach(var inv in inventories)
            {
                items_tmp.Clear();
                inv.GetItems(items_tmp);

                SortItems(inv, items_tmp, readin);

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
            }

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
                ClearLists(avaliability_lookup_);
                ScanInventories(cached_inventories_, avaliability_lookup_);
                RebalanceInventories(partitioned_invs_, avaliability_lookup_);
            }


        }
        #endregion
    }
}

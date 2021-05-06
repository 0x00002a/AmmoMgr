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


        #region Constants
        internal const string AMMO_TYPE_NAME = "MyObjectBuilder_AmmoMagazineDefinition";
        #endregion

        #region Fields
        internal List<IMyInventory> cached_inventories_ = new List<IMyInventory>();
        internal List<IMyInventory> requesters_ = new List<IMyInventory>();

        internal Dictionary<string, List<AmmoItemData>> avaliability_lookup_ = new Dictionary<string, List<AmmoItemData>>();

        internal ulong ticks_10 = 0;
        #endregion

        #region Init
        internal void RefreshInventories(List<IMyInventory> inventories)
        {
            var block_cache = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(block_cache);

            inventories.Clear();
            inventories.AddRange(
                    block_cache
                    .Where(b => b.HasInventory)
                    .Select(b => b.GetInventory()));
        }

        #endregion

        #region Running

        internal void AllotItems(double per_inv, List<AmmoItemData> avaliable, IEnumerable<IMyInventory> requesters)
        {
            /*if (per_inv * requesters.Count > avaliable.Count) // Sanity check
            {
                throw new InvalidOperationException("Not enough avaliable for all requesters, AllotItems called with invalid arguments!");
            }*/

            var aval_head = 0;
            foreach(var inv in requesters)
            {
                var needed = per_inv;
                for (var i = aval_head; i < avaliable.Count; ++i)
                {
                    var target_item = avaliable[i];

                    var aval = Math.Min(needed, (double)target_item.Item.Amount);

                    inv.TransferItemFrom(target_item.Parent, target_item.Item, (MyFixedPoint)aval);

                    needed -= aval;
                    if (needed > 0)
                    {
                        ++aval_head;
                    } else
                    {
                        break;
                    }
                }

            }
        }

        internal void RebalanceInventories(IEnumerable<IMyInventory> requesters, Dictionary<string, List<AmmoItemData>> avaliable)
        {

            var total_reqs = requesters_.Count;

            foreach(var ammo in avaliable)
            {
                var total_aval = ammo.Value.Select(a => (double)a.Item.Amount).Sum();
                var per_inv = Math.Floor(total_aval / total_reqs);
                AllotItems(per_inv, ammo.Value, requesters);
            }

        }

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

        internal static void ClearLists<T, V>(Dictionary<T, List<V>> dict)
        {
            foreach (var val in dict.Values)
            {
                val.Clear();
            }
        }
        #endregion

        #region Entry points
        public Program()
        {

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
                RefreshInventories(cached_inventories_);
                
            } else if (ticks_10 % 3 == 0)
            {
                ClearLists(avaliability_lookup_);
                ScanInventories(cached_inventories_, avaliability_lookup_);
                RebalanceInventories(requesters_, avaliability_lookup_);
            }


        }
        #endregion
    }
}

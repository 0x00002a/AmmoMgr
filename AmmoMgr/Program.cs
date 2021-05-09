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
        
        internal enum StatusType
        {
            //TotalAmmoSummary,
            WeaponsSummary,
            Invalid,
        }


        #region Constants
        internal const string AMMO_TYPE_NAME = "MyObjectBuilder_AmmoMagazine";
        internal const string VERSION = "0.3.1";
        #endregion

        #region Fields
        internal HashSet<MyDefinitionId> wc_weapons_ = new HashSet<MyDefinitionId>();
        internal Dictionary<IMyInventory, HashSet<MyItemType>> inv_allowlist_cache_ = new Dictionary<IMyInventory, HashSet<MyItemType>>();
        internal List<HashSet<IMyInventory>> partitioned_invs_ = new List<HashSet<IMyInventory>>();
        internal Dictionary<IMyInventory, bool> requester_cache_ = new Dictionary<IMyInventory, bool>();
        internal Dictionary<string, List<AmmoItemData>> avaliability_lookup_ = new Dictionary<string, List<AmmoItemData>>();
        internal List<string> actions_log_ = new List<string>();
        internal Dictionary<StatusType, List<IMyTextSurface>> status_lcds_ = new Dictionary<StatusType, List<IMyTextSurface>>();
        internal StringBuilder lcd_data_cache_ = new StringBuilder();
        internal string LCD_STATUS_PREFIX = "AmmoMgrLCD";
        internal MyIni status_lcd_parser_ = new MyIni();
        internal WcPbApi wc_;

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

        internal bool IsRequester(IMyInventory inv)
        {
            bool result;
            if (!requester_cache_.TryGetValue(inv, out result))
            {
                result = false;
            }
            return result;
        }
        #endregion

        #region Init

        internal static void ScanForWeapons(WcPbApi wc, ICollection<MyDefinitionId> readin)
        {
            wc.GetAllCoreStaticLaunchers(readin);
            wc.GetAllCoreTurrets(readin);
            wc.GetAllCoreWeapons(readin);

        }
        internal void ScanForLCDs(Dictionary<StatusType, List<IMyTextSurface>> readin)
        {
            var search_str = LCD_STATUS_PREFIX;

            var blocks_tmp = new List<IMyTextSurfaceProvider>();
            GridTerminalSystem.GetBlocksOfType(blocks_tmp);

            foreach (var prov in blocks_tmp)
            {
                var block = prov as IMyTerminalBlock;
                if (block != null)
                {
                    var name = block.CustomName;
                    if (name.Contains(search_str))
                    {
                        ParseStatusLCDData(block, prov, readin);
                    }
                }
            }
        }
        internal string BuildStatusLCDSectionName(int index)
        {
            return $"AmmoMgr {index}";
        }
        internal bool TryParseStatus(string input, out StatusType output)
        {
            switch(input)
            {
                case "WeaponsSummary":
                case "WepSummary":
                    output = StatusType.WeaponsSummary;
                    return true;
                default:
                    output = StatusType.Invalid;
                    return false;
            }
        }
        internal void ParseStatusLCDData(IMyTerminalBlock block, IMyTextSurfaceProvider prov, Dictionary<StatusType, List<IMyTextSurface>> readin)
        {
            status_lcd_parser_.Clear();
            if (block.CustomData.Length != 0 && !status_lcd_parser_.TryParse(content: block.CustomData))
            {
                console.Persistout.WriteLn($"{block.CustomName} has invalid custom data");
                return;
            }
            for(var i = 0; i != prov.SurfaceCount; ++i)
            {
                var sect = BuildStatusLCDSectionName(i);

                if (status_lcd_parser_.ContainsSection(sect))
                {
                    StatusType type;
                    TryParseStatus(status_lcd_parser_.Get(sect, "type").ToString(), out type);
                    List<IMyTextSurface> surfaces;
                    if (!readin.TryGetValue(type, out surfaces))
                    {
                        surfaces = new List<IMyTextSurface>();
                        readin.Add(type, surfaces);
                    }
                    surfaces.Add(prov.GetSurface(i));
                } else
                {
                    status_lcd_parser_.AddSection(sect);
                }
            }
            block.CustomData = status_lcd_parser_.ToString();

            


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

        internal IMyInventory CreateInvData(IMyInventory inv)
        {
            var is_requester = IsWeapon((IMyTerminalBlock)inv.Owner);

            requester_cache_[inv] = is_requester;
            return inv;

        }

        internal static string OwnerName(IMyInventory inv)
        {
            var owner = inv?.Owner as IMyTerminalBlock;
            return owner == null ? "" : owner.CustomName;
        }

        internal void RefreshInventories(List<HashSet<IMyInventory>> readin)
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
                        parition = new HashSet<IMyInventory>();
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

        internal void RebalanceInventories(List<HashSet<IMyInventory>> requesters, Dictionary<string, List<AmmoItemData>> avaliable)
        {
            foreach (var ammo in avaliable)
            {
                if (ammo.Value.Count != 0)
                {

                    foreach (var inv_system in requesters)
                    {
                        var ammo_t = ammo.Value[0].Item.Type;
                        var eligable_invs = inv_system.Where(i => CanContainItem(i, ammo_t));
                        var nb_req = eligable_invs.Count(i => IsRequester(i));
                        var total = eligable_invs.Select(i => (double)i.GetItemAmount(ammo_t)).Sum();
                        var per_inv = total / nb_req;

                        var eligible_req = eligable_invs.Where(i => IsRequester(i));
                        AllotItems(per_inv, ammo.Value, eligible_req);

                    }
                }

            }


        }

        internal static void ScanInventories(List<HashSet<IMyInventory>> inventories, Dictionary<string, List<AmmoItemData>> readin)
        {
            var items_tmp = new List<MyInventoryItem>();
            foreach(var part in inventories)
            {
                foreach (var inv in part)
                {
                    items_tmp.Clear();
                    inv.GetItems(items_tmp);

                    SortItems(inv, items_tmp, readin);
                }

            }
        }

        internal void RefreshTargetingStatus()
        {
            if (wc_ == null)
            {
                return;
            }

            foreach(var parition in partitioned_invs_)
            {
                foreach(var inv in parition)
                {
                    var inv_parent = inv.Owner as IMyTerminalBlock;
                    if (inv_parent != null && (IsRequester(inv) || IsWeapon(inv_parent)))
                    {
                        var curr_target = wc_.GetWeaponTarget(inv_parent);

                        requester_cache_[inv] = !wc_.IsWeaponReadyToFire(inv_parent) 
                                            || !(curr_target == null 
                                            || curr_target.Value.EntityId == 0
                                            || wc_.CanShootTarget(inv_parent, ((MyDetectedEntityInfo)curr_target).EntityId, 0));
                    }

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
                wc_ = wc;
                console.Persistout.WriteLn($"Using WeaponCore weapons as well as vanilla");
            }
            ScanForLCDs(status_lcds_);
            

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
            if (argument == "refresh" && (updateSource & UpdateType.Terminal) == 0)
            {
                foreach(var surf in status_lcds_)
                {
                    surf.Value.Clear();
                }
                ScanForLCDs(status_lcds_);
            }

            if ((updateSource & UpdateType.Update10) == UpdateType.Update10)
            {
                ++ticks_10;

                DrawStatus();
                RefreshTargetingStatus();
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

        #region LCD Drawing 
        internal void DrawStatus()
        {
            foreach (var kh in status_lcds_)
            {
                console.Stdout.WriteLn($"{kh.Key} => {kh.Value}");
                DrawStatusFor(kh.Key, kh.Value);
            }
        }

        internal void DrawStatusFor(StatusType type, List<IMyTextSurface> surfaces)
        {
            lcd_data_cache_.Clear();
            AppendTxtFor(type, lcd_data_cache_);

            foreach (var surface in surfaces)
            {
                surface.ContentType = ContentType.TEXT_AND_IMAGE;
                surface.WriteText(lcd_data_cache_.ToString());
            }

            lcd_data_cache_.Clear();
        }
        internal static void DrawProgressBar(StringBuilder to, int steps, double curr, double total)
        {
            to.Append("(");
            var seg_size = total / steps;
            for(var s = 0; s < steps; ++s)
            {
                var seg = seg_size * s;
                if (seg > curr || curr == 0)
                {
                    to.Append("-");
                } else
                {
                    to.Append("=");
                }

            }
            to.Append(")");
        }
        internal void AppendForWepSummary(StringBuilder to)
        {
            foreach (var wep_group in partitioned_invs_)
            {
                foreach (var wep in wep_group)
                {
                    var owner_block = wep.Owner as IMyTerminalBlock;
                    if (owner_block != null && IsWeapon(owner_block))
                    {
                        to.Append($"[ {owner_block.CustomName} ]\n");
                        var aval = wep.MaxVolume;

                        to.Append("  ");
                        DrawProgressBar(to, 5, (double)wep.CurrentVolume, (double)aval);
                        to.Append("\n");

                        HashSet<MyItemType> accepted;
                        if (inv_allowlist_cache_.TryGetValue(wep, out accepted))
                        {
                            foreach (var accept in accepted)
                            {
                                var qty = wep.GetItemAmount(accept);
                                if (qty > 0)
                                {
                                    to.Append($"    > {accept.SubtypeId}: {qty}\n");
                                }
                            }
                        } else
                        {
                            to.Append("    > DRY");
                        }
                    }
                }
            }
        }
        internal void AppendTxtFor(StatusType data, StringBuilder to)
        {
            switch(data)
            {
                case StatusType.WeaponsSummary:
                    AppendForWepSummary(to);
                    break;
                case StatusType.Invalid:
                    to.Append("Invalid custom data");
                    break;
            }
        }

        #endregion
    }
}

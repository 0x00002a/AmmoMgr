/*
    AmmoMgr Space Engineers Script.
    Copyright (C) 2021 Natasha England-Elbro

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/


using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
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
using CoreSystems.Api;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        internal struct AmmoItemData
        {
            public IMyInventory Parent;
            public MyInventoryItem Item;
        }
        internal enum Priority
        {
            Unknown = -1,
            Container = 0,
            InactiveWeapon = 1,
            ActiveWeapon = 2,
        };
        
        internal enum StatusType
        {
            //TotalAmmoSummary,
            WeaponsSummary,
            ContainerSummary,
            FullSummary,
            EngagedSummary,
            Invalid,
        }
        internal class StatusLCDData
        {
            public StatusType Type;
            public string Group;
            public Vector2 OriginOffset;
            public Vector2 ScrollOffset;
            public float Scale = 1f;
            public bool ScrollingUp = false;
            public bool Scroll;
            public bool ShortMode;
            public bool HideZeroEntries;
        }

        internal enum ExecutionPoint
        {
            Rebalancing, 
            Scanning,

        }


        #region Constants
        internal const string AMMO_TYPE_NAME = "MyObjectBuilder_AmmoMagazine";
        internal const string VERSION = "0.5.1";

        internal const int MAX_REBALANCE_TICKS = 60; // Increase this to slowdown the script and maybe improve perf with _lots_ of inventories
        internal const int TICKS_PER_COMP_UPDATE = 30;
        internal int max_comp_since_ticks_ = 0;
        #endregion

        #region Fields
        internal HashSet<MyDefinitionId> wc_weapons_ = new HashSet<MyDefinitionId>();
        internal Dictionary<IMyInventory, HashSet<MyItemType>> inv_allowlist_cache_ = new Dictionary<IMyInventory, HashSet<MyItemType>>();
        internal List<HashSet<IMyInventory>> partitioned_invs_ = new List<HashSet<IMyInventory>>();
        internal Dictionary<IMyInventory, Priority> requester_cache_ = new Dictionary<IMyInventory, Priority>();
        internal Dictionary<string, List<AmmoItemData>> avaliability_lookup_ = new Dictionary<string, List<AmmoItemData>>();
        internal List<string> actions_log_ = new List<string>();
        internal Dictionary<StatusLCDData, List<IMyTextSurface>> status_lcds_ = new Dictionary<StatusLCDData, List<IMyTextSurface>>();
        internal Dictionary<string, HashSet<IMyTerminalBlock>> block_groups_cache_ = new Dictionary<string, HashSet<IMyTerminalBlock>>();
        internal Dictionary<IMyInventory, bool> cache_outdated_lookup_ = new Dictionary<IMyInventory, bool>();
        internal List<IMyInventory> outdated_inv_store_ = new List<IMyInventory>();
        internal List<MySprite> sprite_cache_ = new List<MySprite>();
        internal List<IMyInventory> flat_inv_cache_ = new List<IMyInventory>();
        internal StringBuilder lcd_data_cache_ = new StringBuilder();
        internal MyIni status_lcd_parser_ = new MyIni();
        internal WcPbApi wc_;
        internal SpriteBuilder sbuilder_ = new SpriteBuilder();
        internal string lcd_tag_;
        internal uint ticks_per_inv_refresh_ = 1200; // Every 2 minutes
        internal ulong tick_ = 0;

        #region Ini Keys 
        internal const string INI_SECT_NAME = "AmmoMgr";
        internal static MyIniKey LCD_TAG_KEY = new MyIniKey(INI_SECT_NAME, "lcd tag");

        #endregion



        Exception fatal_error_ = null;

        internal ulong ticks_10 = 0;

        internal Console console = new Console
        {
            ClearOnPrint = true,
            Header = $"== AmmoMgr v{VERSION} ==",
        };
        #endregion

        #region Util 
        internal static bool IsAmmo(MyItemType type)
        {
            return type.TypeId == AMMO_TYPE_NAME;
        }

        internal static void SortItems(IMyInventory parent, List<MyInventoryItem> items, Dictionary<string, List<AmmoItemData>> readin)
        {
            
            foreach(var item in items)
            {
                if (IsAmmo(item.Type))
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
        internal HashSet<MyItemType> AcceptedItems(IMyInventory inv)
        {
            HashSet<MyItemType> allowed;
            if (!inv_allowlist_cache_.TryGetValue(inv, out allowed))
            {
                allowed = new HashSet<MyItemType>();
                inv.GetAcceptedItems(null, t => {
                    if (IsAmmo(t))
                    {
                        allowed.Add(t);
                    }
                    return false; 
                });
                inv_allowlist_cache_.Add(inv, allowed);
            }
            return allowed;

        }
        internal bool CanContainItem(IMyInventory inv, MyItemType ammo)
        {
            return AcceptedItems(inv).Contains(ammo);
        }
       
        internal bool CanContainAmmo(IMyInventory inv)
        {
            return AcceptedItems(inv).Any(i => IsAmmo(i));
        }
        
        internal bool IsRequester(IMyInventory inv)
        {
            Priority priority;
            if (!requester_cache_.TryGetValue(inv, out priority))
            {
                return false;
            } else
            {
                return priority > 0;
            }
        }

        internal bool IsSameOrHigherPriority(IMyInventory target, IMyInventory than)
        {
            return PriorityFor(target) >= PriorityFor(than);
        }

        internal Priority PriorityFor(IMyInventory inv)
        {
            Priority p;
            if (!requester_cache_.TryGetValue(inv, out p))
            {
                return Priority.Unknown;
            }
            else
            {
                return p;
            }
        }
        
        #endregion

        #region Init

        internal static void ScanForWeapons(WcPbApi wc, ICollection<MyDefinitionId> readin)
        {
            wc.GetAllCoreStaticLaunchers(readin);
            wc.GetAllCoreTurrets(readin);
            wc.GetAllCoreWeapons(readin);
        }
        internal void ScanForLCDs(Dictionary<StatusLCDData, List<IMyTextSurface>> readin)
        {
            var search_str = lcd_tag_;

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
                case "":
                case "Weapons":
                    output = StatusType.WeaponsSummary;
                    return true;
                case "Full":
                    output = StatusType.FullSummary;
                    return true;
                case "Engaged":
                    output = StatusType.EngagedSummary;
                    return true;
                case "Containers":
                    output = StatusType.ContainerSummary;
                    return true;
                default:
                    output = StatusType.Invalid;
                    return false;
            }
        }
        internal void ParseStatusLCDData(IMyTerminalBlock block, IMyTextSurfaceProvider prov, Dictionary<StatusLCDData, List<IMyTextSurface>> readin)
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
                    var group = status_lcd_parser_.Get(sect, "group").ToString(null);
                    var offset_x = status_lcd_parser_.Get(sect, "offset x").ToInt32(0);
                    var offset_y = status_lcd_parser_.Get(sect, "offset y").ToInt32(0);
                    var origin_offset = new Vector2(offset_x, offset_y);
                    var scale = status_lcd_parser_.Get(sect, "scale").ToDouble(1);
                    var scroll = status_lcd_parser_.Get(sect, "scroll").ToBoolean(true);
                    var short_m = status_lcd_parser_.Get(sect, "oneline").ToBoolean(false);

                    var data = new StatusLCDData { 
                        Group = group, 
                        Type = type, 
                        OriginOffset = 
                        origin_offset, 
                        Scale = (float)scale, 
                        Scroll = scroll,
                        ShortMode = short_m,
                        HideZeroEntries = status_lcd_parser_.Get(sect, "hide empty").ToBoolean(true),
                    };

                    List<IMyTextSurface> surfaces;
                    if (!readin.TryGetValue(data, out surfaces))
                    {
                        surfaces = new List<IMyTextSurface>();
                        readin.Add(data, surfaces);
                    }
                    surfaces.Add(prov.GetSurface(i));
                } else
                {
                    status_lcd_parser_.AddSection(sect);
                }
            }
            block.CustomData = status_lcd_parser_.ToString();

            


        }

        internal void ScanGroups()
        {
            block_groups_cache_.Clear();
            GridTerminalSystem.GetBlockGroups(null, g => {
                var set = new HashSet<IMyTerminalBlock>();
                g.GetBlocks(null, b => { set.Add(b); return false; });
                block_groups_cache_.Add(g.Name, set); 
                
                
                return false; });
        }

        internal void ParseConfig()
        {
            status_lcd_parser_.Clear();
            if (!status_lcd_parser_.TryParse(Me.CustomData))
            {
                console.Persistout.WriteLn("[WARN]: PB has invalid custom data, fix it then recompile to have config applied");
                return;
            }

            lcd_tag_ = status_lcd_parser_.Get(LCD_TAG_KEY).ToString("AmmoMgrLCD");

            status_lcd_parser_.Clear();
        }

        #endregion

        #region Running
        internal void WriteStatsToStdout()
        {
            var complexity = Runtime.CurrentInstructionCount;
          
            if (max_comp_since_ticks_ < complexity)
            {
                max_comp_since_ticks_ = complexity;
            }
            console.Stdout.WriteLn($"Complexity: {max_comp_since_ticks_} / {Runtime.MaxInstructionCount}");
            foreach(var act in actions_log_)
            {
                console.Stdout.WriteLn(act);
            }
            if (tick_ % TICKS_PER_COMP_UPDATE == 0)
            {
                max_comp_since_ticks_ = 0;
            }

        }


        internal static string OwnerName(IMyInventory inv)
        {
            var owner = inv?.Owner as IMyTerminalBlock;
            return owner?.CustomName ?? "";
        }
        internal bool IsValidInventory(IMyInventory inv)
        {
            var parent = inv.Owner as IMyTerminalBlock;
            return parent != null && CanContainAmmo(inv) && Me.IsSameConstructAs(parent);
        }
        HashSet<IMyInventory> checked_cache_ = new HashSet<IMyInventory>();
        internal void AddInventory(IMyInventory inv, List<IMyInventory> all, List<HashSet<IMyInventory>> readin)
        {
            if (!checked_cache_.Contains(inv))
            {
                checked_cache_.Add(inv);
                var partition = readin.FirstOrDefault(set => set.Contains(inv));

                if (partition == null)
                {
                    partition = new HashSet<IMyInventory>();
                    readin.Add(partition);
                }

                partition.Add(inv);
                cache_outdated_lookup_[inv] = false;

                foreach (var peer in all)
                {
                    if (!checked_cache_.Contains(peer) && !partition.Contains(peer) && inv.IsConnectedTo(peer))
                    {
                        partition.Add(peer);
                        checked_cache_.Add(peer);
                    }
                }
            }
        }

        internal IEnumerator<bool> RefreshInventories(List<HashSet<IMyInventory>> readin)
        {
            RemoveOutdatedInvs();
            flat_inv_cache_.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, b => {
                if (b.HasInventory && IsValidInventory(b.GetInventory()))
                {
                    flat_inv_cache_.Add(b.GetInventory());
                }
                return false;
            });

           

            checked_cache_.Clear();
            foreach(var inv in flat_inv_cache_)
            {
                AddInventory(inv, flat_inv_cache_, readin);
                yield return false; // Uncapped execution ticks, because IsConnectedTo is expensive apparently 
            }

            yield break;
            
        }
        internal void AllotItems(double per_inv, List<AmmoItemData> avaliable, HashSet<IMyInventory> requesters)
        {

            if (avaliable.Count == 0)
            {
                return;
            }

            var ammo_t = avaliable[0].Item.Type;

            var aval_head = 0;
            foreach (var inv in requesters)
            {
                var to_block = inv.Owner as IMyTerminalBlock;
                var needed = per_inv;
                if (
                   (to_block != null && to_block.IsWorking)
                && ((double)inv.GetItemAmount(ammo_t) < per_inv)
                && CanContainItem(inv, ammo_t)
                )
                {
                    for (var i = aval_head; i < avaliable.Count; ++i)
                    {

                        var target_item = avaliable[i];
                        if (ammo_t != target_item.Item.Type)
                        {
                            throw new Exception("All avaliable must have same ammo type");
                        }

                        var from_inv = target_item.Parent;
                        if (
                            IsSameOrHigherPriority(inv, from_inv)
                            && (PriorityFor(from_inv) < PriorityFor(inv) || (double)from_inv.GetItemAmount(target_item.Item.Type) > per_inv)
                            && from_inv.IsConnectedTo(inv)
                            )
                        {
                            var aval = Math.Min(needed, (double)target_item.Item.Amount);

                            inv.TransferItemFrom(target_item.Parent, target_item.Item, (MyFixedPoint)aval);
                            actions_log_.Add($"{OwnerName(target_item.Parent)} -> {OwnerName(inv)} ({target_item.Item.Type.SubtypeId}) ({aval}) ");

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
        }

        internal IEnumerable<IMyInventory> FilterByCanContain(List<IMyInventory> src, MyItemType type)
        {
            return src.Where(i => CanContainItem(i, type));
        }

        private double CalcPerInv(MyItemType ammo_t, HashSet<IMyInventory> inv_system)
        {
            var nb_req = 0;
            var total_qty = 0.0;
            foreach (var inv in inv_system)
            {
                if (CanContainItem(inv, ammo_t))
                {
                    if (IsRequester(inv))
                    {
                        ++nb_req;
                    }
                    total_qty += (double)inv.GetItemAmount(ammo_t);
                }
            }

            var per_inv = Math.Floor(Math.Round(total_qty / nb_req, 1));

            return per_inv;

        }

        internal IEnumerator<bool> RebalanceInventories(List<HashSet<IMyInventory>> requesters, Dictionary<string, List<AmmoItemData>> avaliable)
        {
            var per_yield = MAX_REBALANCE_TICKS / (double)requesters.Count;
            var since_yield = 0.0;
            foreach (var ammo in avaliable)
            {
                if (ammo.Value.Count != 0)
                {

                    foreach (var inv_system in requesters)
                    {
                        if (since_yield >= 1.0)
                        {
                            since_yield = 0;
                            yield return false;
                        }
                        since_yield += per_yield;
                        var per_inv = CalcPerInv(ammo.Value[0].Item.Type, inv_system);
                        AllotItems(per_inv, ammo.Value, inv_system);
                    }
                }

            }


        }

        readonly List<MyInventoryItem> items_tmp_ = new List<MyInventoryItem>();
        internal void ScanInventories(List<HashSet<IMyInventory>> inventories, Dictionary<string, List<AmmoItemData>> readin)
        {
            foreach(var part in inventories)
            {
                foreach (var inv in part)
                {
                    cache_outdated_lookup_[inv] = true;
                    items_tmp_.Clear();
                    inv.GetItems(items_tmp_);

                    SortItems(inv, items_tmp_, readin);
                }

            }
        }

        internal void RefreshTargetingStatus()
        {
            

            foreach(var parition in partitioned_invs_)
            {
                foreach(var inv in parition)
                {
                    var inv_parent = inv.Owner as IMyTerminalBlock;
                    if (inv_parent != null && IsWeapon(inv_parent))
                    {
                        var priority = Priority.InactiveWeapon;
                        if (wc_ != null && wc_weapons_.Contains(inv_parent.BlockDefinition))
                        {
                            var curr_target = wc_.GetWeaponTarget(inv_parent);


                            if (!(curr_target == null || curr_target.Value.EntityId == 0 || !wc_.CanShootTarget(inv_parent, ((MyDetectedEntityInfo)curr_target).EntityId, 0)))
                            {
                                priority = Priority.ActiveWeapon;
                            }
                        } else if ((inv_parent as IMyUserControllableGun)?.IsShooting ?? false)
                        {
                            priority = Priority.ActiveWeapon;
                        }

                        requester_cache_[inv] = priority;
                    }

                }

            }

        }
        


        #endregion
        #region Cleanup

        internal void RemoveOutdatedInvs()
        {
            outdated_inv_store_.Clear();
            foreach(var kh in cache_outdated_lookup_)
            {
                if (kh.Value)
                {
                    outdated_inv_store_.Add(kh.Key);
                }
            }
            foreach(var outdated in outdated_inv_store_)
            {
                cache_outdated_lookup_.Remove(outdated);
                partitioned_invs_.FirstOrDefault(p => p.Contains(outdated))?.Remove(outdated);
            }
            outdated_inv_store_.Clear();
        }

        #endregion

        #region Entry points
        public Program()
        {
            ParseConfig();
            var wc = new WcPbApi();
            bool has_wc = false;
            try
            {
                wc.Activate(Me);
                has_wc = true;
            } catch(Exception)
            {
                console.Persistout.WriteLn($"Failed to initalise WeaponCore, falling back to vanilla only");
            }
            if (has_wc)
            {
                ScanForWeapons(wc, wc_weapons_);
                wc_ = wc;
                console.Persistout.WriteLn($"Using WeaponCore weapons as well as vanilla");
            }
            ScanForLCDs(status_lcds_);
            ScanGroups();

            sm_instructions_.Enqueue(RefreshInventories(partitioned_invs_));

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
        readonly Queue<IEnumerator<bool>> sm_instructions_ = new Queue<IEnumerator<bool>>();

        private IEnumerator<bool> Tickover()
        {
            actions_log_.Clear();
            ClearLists(avaliability_lookup_);
            ScanInventories(partitioned_invs_, avaliability_lookup_);

            return RebalanceInventories(partitioned_invs_, avaliability_lookup_);

        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (fatal_error_ != null)
            {
                console.Stderr.WriteLn($"== Fatal Error ==\nReason: {fatal_error_.Message}\n\n-- Stack Trace --\n{fatal_error_.StackTrace}");
            }
            else
            {

                try
                {
                    ++tick_;
                    if ((updateSource & UpdateType.Update10) == UpdateType.Update10)
                    {

                        DrawStatus();
                        RefreshTargetingStatus();
                    }

                    var is_oneshot = (updateSource & UpdateType.Once) == UpdateType.Once;
                    if (is_oneshot && sm_instructions_.Count != 0)
                    {

                        var curr = sm_instructions_.Peek();
                        if (curr.MoveNext())
                        {
                        } else
                        {
                            sm_instructions_.Dequeue().Dispose();
                        }
                        Runtime.UpdateFrequency |= UpdateFrequency.Once;

                    }
                    if (sm_instructions_.Count == 0)
                    {
                        sm_instructions_.Enqueue(Tickover());
                        Runtime.UpdateFrequency |= UpdateFrequency.Once;
                    }

                    if (tick_ % ticks_per_inv_refresh_ == 0 && sm_instructions_.Count <= 1)
                    {
                        sm_instructions_.Enqueue(RefreshInventories(partitioned_invs_));
                        Runtime.UpdateFrequency |= UpdateFrequency.Once;
                    }

                    WriteStatsToStdout();
                }
                catch (Exception e)
                {
                    fatal_error_ = e;
                    Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update100;
                }
            }

            console.PrintOutput(this);

        }

        #endregion

        #region LCD Drawing 
        internal void DrawStatus()
        {
            foreach (var kh in status_lcds_)
            {
                DrawStatusFor(kh.Key, kh.Value);
            }
        }

        internal void DrawStatusFor(StatusLCDData data, List<IMyTextSurface> surfaces)
        {
            lcd_data_cache_.Clear();

            foreach (var surface in surfaces)
            {
                surface.ContentType = ContentType.SCRIPT;
                surface.Script = string.Empty;
                var viewport = new RectangleF(new Vector2(0, (surface.TextureSize.Y - surface.SurfaceSize.Y) / 2f) + data.OriginOffset, surface.SurfaceSize);
                var pos = viewport.Position + data.ScrollOffset;

                var frame = surface.DrawFrame();
                sbuilder_.CurrPos = pos;
                sbuilder_.Scale = data.Scale;
                sbuilder_.Viewport = viewport;
                sbuilder_.Surface = surface;

                var end_pos = AppendTxtFor(data, ref frame);

                if (data.Scroll && !viewport.Contains(end_pos) || data.ScrollOffset.LengthSquared() > 0)
                {
                    if (!data.ScrollingUp && viewport.Position.Y + viewport.Size.Y < end_pos.Y)
                    {
                        data.ScrollOffset -= new Vector2(0, 10);
                    }
                    else if (data.ScrollingUp && viewport.Position.Y + 5 < pos.Y)
                    {
                        data.ScrollingUp = false;
                    }
                    else
                    {
                        data.ScrollOffset += new Vector2(0, 50);
                        data.ScrollingUp = true;
                    }
                }


                frame.Dispose();

            }

            lcd_data_cache_.Clear();
        }
        internal static Color ColourForProg(int prog)
        {
            return prog > 80 ? Color.Green : prog > 50 ? Color.Orange : prog > 30 ? Color.OrangeRed : prog > 10 ? Color.Red : Color.DarkRed;
        }
        
        internal Vector2 AppendForWepSummary(ref MySpriteDrawFrame frame, StatusLCDData data, Func<IMyTerminalBlock, bool> accept_filt)
        {
            HashSet<IMyTerminalBlock> filter_group = null;
            var filter_group_name = data.Group;
            if (filter_group_name != null)
            {
                block_groups_cache_.TryGetValue(filter_group_name, out filter_group);
            }

            sprite_cache_.Clear();
            var to = sprite_cache_;
            foreach (var wep_group in partitioned_invs_)
            {
                var box_border = new SpriteBuilder.BoxedProxy(sbuilder_);
                foreach (var wep in wep_group)
                {
                    var owner_block = wep.Owner as IMyTerminalBlock;
                    if (accept_filt(owner_block) && (filter_group == null || filter_group.Contains(owner_block)))
                    {
                        var title_txt = $"[ {owner_block.CustomName} ]";
                        to.Add(sbuilder_.MakeText(title_txt));


                        SpriteBuilder.IndentProxy? maybe_indent = null;
                        if (data.ShortMode)
                        {
                            maybe_indent = sbuilder_.WithIndent((int)(sbuilder_.TextSizePx(title_txt).X + sbuilder_.NewlineHeight));
                        }
                        else
                        {
                            sbuilder_.AddNewline();
                        }
                        var aval = wep.MaxVolume;

                        sbuilder_.MakeProgressBar(
                            to: to,
                            size:  new Vector2(sbuilder_.Viewport.Size.X / 6, 2 * (SpriteBuilder.NEWLINE_HEIGHT_BASE / 3)),
                            bg: Color.White,
                            fg: ColourForProg((int)((double)wep.CurrentVolume / (double)aval * 100)),
                            curr: (double)wep.CurrentVolume, total: (double)aval
                            );
                        maybe_indent?.Dispose();


                        sbuilder_.AddNewline();
                        if (!data.ShortMode)
                        {
                            using (var idn1 = sbuilder_.WithIndent(20))
                            {
                                var accepted = AcceptedItems(wep);
                                foreach (var accept in accepted)
                                {
                                    var qty = wep.GetItemAmount(accept);
                                    if (!data.HideZeroEntries || qty > 0)
                                    {
                                        to.Add(sbuilder_.MakeBulletPt());
                                        to.Add(sbuilder_.MakeText($"{accept.SubtypeId}: {(double)qty:00}", offset: new Vector2(sbuilder_.NewlineHeight / 2, 0)));
                                        sbuilder_.AddNewline();
                                    }

                                }
                                sbuilder_.AddNewline();
                            }
                        }
                    }
                }
                if (sprite_cache_.Count != 0)
                {
                    box_border.Make(ref frame, (int)sbuilder_.Viewport.Size.X, 10);

                    frame.AddRange(sprite_cache_);

                    sprite_cache_.Clear();

                    sbuilder_.AddNewline();
                    sbuilder_.AddNewline();
                } 
            }
            return sbuilder_.CurrPos;
        }
        internal Vector2 AppendTxtFor(StatusLCDData data, ref MySpriteDrawFrame to)
        {
            Func<IMyTerminalBlock, bool> filter_act = null;
            var status = data.Type;
            switch (status)
            {
                case StatusType.WeaponsSummary:
                    filter_act = b => b != null && IsWeapon(b);
                    break;
                case StatusType.ContainerSummary:
                    filter_act = b => b != null && !IsWeapon(b);
                    break;
                case StatusType.FullSummary:
                    filter_act = b => b != null;
                    break;
                case StatusType.EngagedSummary:
                    filter_act = b => b != null && PriorityFor(b.GetInventory()) >= Priority.ActiveWeapon;
                    break;

            }

            if (filter_act != null)
            {
                return AppendForWepSummary(ref to, data, filter_act);
            }
            else
            {
                to.Add(sbuilder_.MakeText("Invalid status type in custom data", alignment: TextAlignment.CENTER, color: Color.Red));
                return Vector2.Zero;
            }
        }

        #endregion
    }
}

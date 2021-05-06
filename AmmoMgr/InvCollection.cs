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
    partial class Program
    {
        /// <summary>
        /// A collection of connected inventories
        /// <para>Its basically an undirected graph</para>
        /// </summary>
        public class InvCollection
        {
            public static double TotalAvalForFrom(IMyInventory inv, MyItemType item, List<IMyInventory> inventories)
            {
                double total = 0;
                foreach(var peer in inventories)
                {
                    if (peer == inv || inv.IsConnectedTo(peer))
                    {
                        total += (double)peer.GetItemAmount(item);
                    }
                }
                return total;
            }
            public int NBConnectedInventories(IMyInventory inv)
            {
                int total = 0;
                foreach(var peer in Inventories)
                {
                    if (peer != inv && inv.IsConnectedTo(peer))
                    {
                        ++total;
                    }
                }
                return total;
            }

            public List<IMyInventory> Inventories;
        }
    }
}

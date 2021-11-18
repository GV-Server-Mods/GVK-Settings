using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.ModAPI;
using VRage.Game.ModAPI;

// This simple script operates once every five seconds, detecting any grids further than whatever the variable "maxRange" is set to, and marking them to be deleted.
// It will display warnings to chat, counting down how much time the grids have left, and then deleting them after 30 seconds.
// You can change the distance by modifying the variable "maxRange" (it's in kilometres.) Feel free to republish the edited mod, just leave this info in for others.
// Original mod by jonn19.

namespace IndustrialAutomaton.WorldBorderEnforcement
{
	
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class WorldBorderEnforcementBase : MySessionComponentBase
    {

		public static int maxRange = 45000;
		// Above is the variable to modify, just change the value to your choice in meters.
		
		public static int maxTime = 30;
		// Similarly if you want more/less time before deletion, change this number. Must be a multiple of 5.
		
		public static Vector3D origin = new Vector3D(65536,65536,65536);
		public static Dictionary<IMyEntity, int> killList = new Dictionary<IMyEntity, int>();
		public static int runCount = 0;
		
		public override void UpdateBeforeSimulation()
		{
			if (runCount++ < 300)
				return;
			HashSet<IMyEntity> entList = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(entList, i => i is IMyCubeGrid);
			List<IMyEntity> gridList = new List<IMyEntity>();
			foreach (var ent in entList)
				if ( (int)Vector3D.Distance(ent.GetPosition(), origin) > maxRange)
					gridList.Add(ent);

			Dictionary<IMyEntity, int> tempList = new Dictionary<IMyEntity, int>();				
			foreach (var kill in killList)
				if (gridList.Contains(kill.Key))
					tempList.Add(kill.Key, kill.Value + 5);
			
			foreach (var grid in gridList)
				if (!tempList.ContainsKey(grid))
					tempList.Add(grid, 0);

			killList.Clear();
			foreach (var temp in tempList)
			{
				IMyCubeGrid grid = temp.Key as IMyCubeGrid;
				if (temp.Value == maxTime)
				{
					MyAPIGateway.Utilities.ShowMessage("Deleting ", grid.CustomName);
					temp.Key.Delete();
				} else {
					MyAPIGateway.Utilities.ShowMessage(grid.CustomName + " out of playing zone", (maxTime - temp.Value) + "s until deletion!");
					killList.Add(temp.Key, temp.Value);
				}
				runCount = 0;
			}
		}
		
	}

}
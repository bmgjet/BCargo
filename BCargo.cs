using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BCargo", "bmgjet", "1.0.4")]
    [Description("Allows inland cargoship by blocking egress while building blocked, Sets Cargo Spawn point to stop it running though islands coming in, Cargo auto heights for tides support")]
    class BCargo : RustPlugin
    {
        public Timer CargoCheck;
        private PluginConfig config;
        private Vector3 randompoint;
        private Dictionary<int, Vector3> CargoBlockPoint;
        private Coroutine _routine;
        private Dictionary<BasePlayer, Dictionary<bool, ulong>> Viewers = new Dictionary<BasePlayer, Dictionary<bool, ulong>>();
        private const string permAdmin = "BCargo.admin";

        #region Load
        private void Init()
        {
            permission.RegisterPermission(permAdmin, this);
            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                LoadDefaultConfig();
            }
            CargoBlockPoint = config.CargoBlockPoint;
        }

        private void Unload()
        {
            if (CargoCheck != null)
            {
                CargoCheck.Destroy();
                CargoCheck = null;
            }
            if (_routine != null)
            {
                try
                {
                    ServerMgr.Instance.StopCoroutine(_routine);
                    foreach (KeyValuePair<BasePlayer, Dictionary<bool, ulong>> viewer in Viewers)
                    {
                        message(viewer.Key, "View", "Stopped");
                    }
                }
                catch { }
                _routine = null;
            }
        }
        #endregion

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Override random cargo spawn point : ")] public bool SpawnAtDefined { get; set; }
            [JsonProperty(PropertyName = "Defined Spawn Point for Cargo : ")] public Vector3 CargoSpawnLocation { get; set; }
            [JsonProperty(PropertyName = "Defined Block Points for Cargo : ")] public Dictionary<int, Vector3> CargoBlockPoint { get; set; }
            [JsonProperty(PropertyName = "Distance from block point before allowing leave : ")] public float LeaveBlockDistance { get; set; }
            [JsonProperty(PropertyName = "Seconds between recheck if can leave : ")] public float ReCheckDelay { get; set; }
            [JsonProperty(PropertyName = "Enable auto leveling for tides plugin : ")] public bool Tides { get; set; }
            [JsonProperty(PropertyName = "Show debug in console : ")] public bool Debug { get; set; }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                SpawnAtDefined = false,
                CargoSpawnLocation = randompoint,
                CargoBlockPoint = new Dictionary<int, Vector3> { { 0, randompoint } },
                LeaveBlockDistance = 120,
                ReCheckDelay = 10,
                Tides = false,
                Debug = false
            };
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            randompoint = DefaultRandomPos();
            Config.WriteObject(GetDefaultConfig(), true);
            config = Config.ReadObject<PluginConfig>();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notallowed"] = "You are not authorized to do that.",
                ["reloaded"] = "Settings reloaded!",
                ["reset"] = "Settings file reset to default!",
                ["saved"] = "Settings saved!",
                ["View"] = "{0} BCargo view!",
                ["added"] = "Node {0} added!",
                ["removed"] = "Node {0} removed!",
                ["spawnstatus"] = "{0} BCargo Manaul Spawn Overide",
                ["spawnlocation"] = "Set Cargoships spawn location to {0}",
                ["help"] = "<color=orange>Help:</color>\n\n/bcargo spawn = Sets spawn location of cargo.\n/bcargo spawn on = Enables manual spawn point.\n/bcargo spawn off = Disables manual spawn point.\n\n/bcargo add = Adds a block zone.\n/bcargo remove 0 = Removes last placed block zone\n/bcargo remove x = Removes node numbered X\n/bcargo view = Shows current block points in red and default cargo path in blue.\n\n/bcargo save = Saves changes\n/bcargo reload = Reloads config file\n/bcargo debug on = Shows console debug info\n/bcargo blocked = Prints back if cargo would be blocked here",
                ["args"] = "You havnt provided the correct args type /bcargo help!"

            }, this);
        }
        private void message(BasePlayer chatplayer, string key, params object[] args)
        {
            if (chatplayer == null && !chatplayer.IsConnected) { return; }
            var message = string.Format(lang.GetMessage(key, this, chatplayer.UserIDString), args);
            chatplayer.ChatMessage(message);
        }
        #endregion

        #region Hooks
        private object OnCargoShipEgress(CargoShip cs)
        {
            if (cs != null)
            {
                bool BlockEgress = false;
                BlockEgress = BlockE(cs.transform.position);
                //Old Method
                //BlockEgress = GamePhysics.CheckSphere(cs.transform.position, config.LeaveBlockDistance, LayerMask.GetMask("Prevent Building"), QueryTriggerInteraction.Collide); //Check if in building block.

                if (BlockEgress)
                {
                    Timer CheckEgress = timer.Once(config.ReCheckDelay, () => { cs.StartEgress(); });
                    if (config.Debug) Puts("Cargo Blocked!");
                    return true;
                }
                if (config.Debug) Puts("Cargo Allowed");
                if (CargoCheck != null)
                {
                    CargoCheck.Destroy();
                }
            }
            return null;
        }

        void OnEntitySpawned(CargoShip cs)
        {
            if (cs != null)
            {
                if (config.SpawnAtDefined) { cs.transform.position = DefaultRandomPos(true); }
                if (config.Tides)   //Changes cargoship height to match tide level.
                {
                    CargoCheck = timer.Every(config.ReCheckDelay, () =>
                    {
                        if (cs != null && config.Tides)
                        {
                            Vector3 CurrentPos = cs.transform.position;
                            if (CurrentPos.y != WaterLevel(config.CargoSpawnLocation))
                            {
                                CurrentPos.y = WaterLevel(config.CargoSpawnLocation);
                                cs.transform.position = CurrentPos;
                                if (config.Debug) Puts("Adjusting Cargo Height To Match Water Level!");
                            }
                        }
                        else
                        {
                            if (CargoCheck != null)
                            {
                                CargoCheck.Destroy();
                                CargoCheck = null;
                            }
                        }
                    });
                }
            }
        }
        #endregion

        #region Bcargo
        public Vector3 DefaultRandomPos(bool NotRandom = false)
        {
            Vector3 vector = TerrainMeta.RandomPointOffshore();
            if (NotRandom) vector = config.CargoSpawnLocation;
            vector.y = WaterLevel(vector);
            return vector;
        }

        private void showonscreen(BasePlayer player, string key, Vector3 value, Color c, bool nameonly = false)
        {
            if (!nameonly)
                player.SendConsoleCommand("ddraw.sphere", 8f, c, value, config.LeaveBlockDistance);

            player.SendConsoleCommand("ddraw.text", 8f, c, value, "<size=22> Node:" + key + " </size>");
        }

        private void viewcsnodes(BasePlayer player)
        {
            //Shows map placed cargo nodes
            for (int i = 0; i < global::TerrainMeta.Path.OceanPatrolFar.Count; i++)
            {
                showonscreen(player, i.ToString(), TerrainMeta.Path.OceanPatrolFar[i], Color.blue, true);
            }
        }

        private void view(BasePlayer player)
        {
            if (CargoBlockPoint == null)
                return;

            foreach (KeyValuePair<int, Vector3> point in CargoBlockPoint)
            {
                try
                {
                    showonscreen(player, point.Key.ToString(), point.Value, Color.red);
                }
                catch { }
            }
        }

        private void CheckIfViewed()
        {
            if (Viewers.Count == 0) //If no viewers left remove routine
            {
                ServerMgr.Instance.StopCoroutine(_routine);
                _routine = null;
                Puts("BCargo View Thread Stopped!");
            }
        }

        IEnumerator BCargoView()
        {
            do //start loop
            {
                foreach (KeyValuePair<BasePlayer, Dictionary<bool, ulong>> viewer in Viewers.ToList())
                {
                    foreach (KeyValuePair<bool, ulong> viewerinfo in viewer.Value.ToList())
                    {
                        //toggle admin flag so you can show a normal user with out it auto banning them for cheating
                        if (!viewerinfo.Key)
                        {
                            viewer.Key.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                            viewer.Key.SendNetworkUpdateImmediate();
                        }
                        view(viewer.Key);
                        viewcsnodes(viewer.Key);
                        if (!viewerinfo.Key && viewer.Key.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                        {
                            viewer.Key.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                            viewer.Key.SendNetworkUpdateImmediate();
                        }
                    }
                    
                    if (!viewer.Key.IsConnected || viewer.Key.IsSleeping())
                    {
                        //Remove from viewers list
                        Viewers.Remove(viewer.Key);
                        message(viewer.Key, "View", "Stopped");
                    }
                }
                yield return CoroutineEx.waitForSeconds(8f);
            } while (Viewers.Count != 0);
            _routine = null;
            Puts("BCargo View Thread Stopped!");
        }

        public void CargoView(BasePlayer player)
        {
            if (_routine != null) //Check if already running
            {
                if (Viewers.ContainsKey(player))
                {
                    Viewers.Remove(player); //Remove player from list
                    message(player, "View", "Stopped");
                    CheckIfViewed();
                    return;
                }
            }
            if (!Viewers.ContainsKey(player))
            {
                Viewers.Add(player, new Dictionary<bool, ulong> { { player.IsAdmin, 0 } });
            }
            if (_routine == null) //Start routine
            {
                Puts("BCargo View Thread Started");
                _routine = ServerMgr.Instance.StartCoroutine(BCargoView());
            }
        }

        public void CargoPoints(BasePlayer player, Vector3 location)
        {
            int node = CargoBlockPoint.Keys.Last() + 1;
            CargoBlockPoint.Add(node, location);
            message(player, "added", node);
        }

        public bool BlockE(Vector3 currentlocation)
        {
            //checks each point to see if cargo is within area.
            foreach (KeyValuePair<int, Vector3> blockpoints in CargoBlockPoint)
            {
                if (Vector3.Distance(currentlocation, blockpoints.Value) < config.LeaveBlockDistance)
                    return true;
            }
            return false;
        }

        public float WaterLevel(Vector3 Pos)
        {
            return TerrainMeta.WaterMap.GetHeight(Pos);
        }
        #endregion

        #region ChatCommands
        [ChatCommand("bcargo")]
        private void CmdPositionCheck(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(permAdmin))
            {
                message(player, "notallowed");
                return;
            }

            if (args.Length < 1)
            {
                message(player, "args");
                return;
            }

            Vector3 NewCargoPos = player.transform.position;
            NewCargoPos.y = WaterLevel(NewCargoPos);

            switch (args[0])
            {
                case "spawn": //Sets manual spawn point or enable/disable manual spawn point
                    if (args.Length == 2)
                    {
                        if (args[1].ToLower().Contains("on"))
                        {
                            message(player, "spawnstatus", "Enabled");
                            config.SpawnAtDefined = true;
                        }
                        else if (args[1].ToLower().Contains("off"))
                        {
                            message(player, "spawnstatus", "Disabled");
                            config.SpawnAtDefined = false;
                        }
                        return;
                    }
                    //Updates node 0 to spawn point
                    if (CargoBlockPoint.ContainsKey(0))
                    {
                        CargoBlockPoint[0] = NewCargoPos;
                    }
                    else
                    {
                        CargoBlockPoint.Add(0, NewCargoPos);
                    }

                    config.CargoSpawnLocation = NewCargoPos;
                    SaveConfig();
                    message(player, "spawnlocation", config.CargoSpawnLocation.ToString());
                    return;

                case "add": //Adds a cargo leave block point
                    CargoPoints(player, NewCargoPos);
                    break;

                case "remove": //Removes a cargo leave block point
                    if (args.Length != 2)
                    {
                        message(player, "args");
                        return;
                    }

                    int node = 0;
                    try
                    {
                        node = int.Parse(args[1]); //try catch incase users try to use letters.
                    }
                    catch { }

                    if (CargoBlockPoint.ContainsKey(node) && node != 0)  //If valid node point selected remove
                    {
                        CargoBlockPoint.Remove(node);
                        message(player, "removed", node);
                        break;
                    }
                    node = CargoBlockPoint.Keys.Last(); //Other wise remove last node added.
                    CargoBlockPoint.Remove(node);
                    message(player, "removed", node);
                    break;

                case "debug": //Shows console debug
                    if (args.Length == 2)
                    {
                        if (args[1].ToLower().Contains("on"))
                        {
                            config.Debug = true;
                            Puts("Debug mode on!");
                        }
                        else
                        {
                            config.Debug = false;
                            Puts("Debug mode off!");
                        }
                    }
                    return;

                case "reload":
                    config = Config.ReadObject<PluginConfig>();
                    CargoBlockPoint = config.CargoBlockPoint;
                    message(player, "reloaded");
                    return;

                case "reset":
                    LoadDefaultConfig();
                    message(player, "reloaded");
                    return;

                case "save":
                    config.CargoBlockPoint = CargoBlockPoint;
                    SaveConfig();
                    message(player, "saved");
                    return;

                case "view":
                    CargoView(player);
                    break;

                case "blocked":
                    player.ChatMessage(BlockE(player.transform.position).ToString());
                    return;

                default:
                    message(player, "help");
                    return;
            }
            view(player); //Tempory show node changes.
        }
        #endregion
    }
}
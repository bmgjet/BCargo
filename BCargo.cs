using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BCargo", "bmgjet", "1.0.0")]
    [Description("Allows inland cargoship by blocking egress while building blocked, Sets Cargo Spawn point to stop it running though islands coming in, Cargo auto heights for tides support")]
    class BCargo : RustPlugin
    {
        public Timer CargoCheck;
        private PluginConfig config;
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
        }

        private void Unload()
        {
            if (CargoCheck != null) 
            { 
                CargoCheck.Destroy(); 
                CargoCheck = null; 
            }
        }
        #endregion

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Override random cargo spawn point : ")] public bool SpawnAtDefined { get; set; }
            [JsonProperty(PropertyName = "Defined Spawn Point for Cargo : ")] public Vector3 CargoSpawnLocation { get; set; }
            [JsonProperty(PropertyName = "Distance from prevent building before allowing leave : ")] public int LeaveBlockDistance { get; set; }
            [JsonProperty(PropertyName = "Seconds between recheck if can leave : ")] public float ReCheckDelay { get; set; }
            [JsonProperty(PropertyName = "Enable auto leveling for tides plugin : ")] public bool Tides { get; set; }
            [JsonProperty(PropertyName = "Show debug in console : ")] public bool Debug { get; set; }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                SpawnAtDefined = false,
                CargoSpawnLocation = DefaultRandomPos(),
                LeaveBlockDistance = 40,
                ReCheckDelay = 5,
                Tides = false,
                Debug = false
            };
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config.WriteObject(GetDefaultConfig(), true);
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
                ["spawnlocation"] = "Set Cargoships spawn location to {0}"
            }, this);
        }
        #endregion

        #region Hooks
        private object OnCargoShipEgress(CargoShip cs)
        {
            if (GamePhysics.CheckSphere(cs.transform.position, config.LeaveBlockDistance, 536870912, QueryTriggerInteraction.Collide)) //Check if in building block.
            {
                Timer CheckEgress = timer.Once(config.ReCheckDelay, () => {cs.StartEgress();});
                if (config.Debug) Puts("Cargo Not Allowed To Leave!");
                return true;
            }
            CargoCheck.Destroy();
            if (config.Debug) Puts("Cargo Allowed To Leave!");
            return null;
        }

        public Vector3 DefaultRandomPos(bool NotRandom = false)
        {
            Vector3 vector = TerrainMeta.RandomPointOffshore();
            if(NotRandom) vector = config.CargoSpawnLocation;
            vector.y = WaterLevel(vector);
            return vector;
        }

        public float WaterLevel(Vector3 Pos){return TerrainMeta.WaterMap.GetHeight(Pos);}

        void OnEntitySpawned(CargoShip cs)
        {
            if (cs != null)
            {
                if (config.SpawnAtDefined){cs.transform.position = DefaultRandomPos(true);}
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

        #region ChatCommands
        [ChatCommand("setbcargo")]
        private void CmdPositionCheck(BasePlayer player, string command, string[] args)
        {
            if (player.IPlayer.HasPermission(permAdmin))
            {
                Vector3 NewCargoPos = player.transform.position;
                NewCargoPos.y = WaterLevel(NewCargoPos);
                config.CargoSpawnLocation = NewCargoPos;
                SaveConfig();
                player.ChatMessage(string.Format(lang.GetMessage("spawnlocation", this, player.IPlayer.Id), config.CargoSpawnLocation.ToString()));
            }
            else{player.IPlayer.Message(lang.GetMessage("notallowed", this, player.IPlayer.Id));}
        }

        [ChatCommand("reloadbcargo")]
        private void CmdReloadSettings(BasePlayer player, string command, string[] args)
        {
            if (player.IPlayer.HasPermission(permAdmin))
            {
                  config = Config.ReadObject<PluginConfig>();
                  player.IPlayer.Message(lang.GetMessage("reloaded", this, player.IPlayer.Id));
            }
            else{player.IPlayer.Message(lang.GetMessage("notallowed", this, player.IPlayer.Id));}
        }
        #endregion
    }
}
using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace DirectionalThrustersOnly
{
    public class DirectionalThrustersOnlyConfiguration
    {
        private const string ConfigFileName = "DirectionalThrustersOnlyConfig.xml";

        private const float DefaultFalloffStartDegrees = 60.0f;
        private const float DefaultMinThrustDegrees = 10.0f;
        private const float DefaultMinThrustMultiplierConfig = 0.01f;

        /// <summary>
        /// The list of configuration items for directional thrusters
        /// </summary>
        public List<DirectionalThrustersOnlyConfigurationItem> ConfigurationItems { get; set; }

        /// <summary>
        /// Generated property to look up block block data by type
        /// </summary>
        internal Dictionary<MyDefinitionId, DirectionalThrustersOnlyConfigurationItem> blockConfigs;

        public static DirectionalThrustersOnlyConfiguration LoadSettings()
        {
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(ConfigFileName, typeof(DirectionalThrustersOnlyConfiguration)))
            {
                try
                {
                    DirectionalThrustersOnlyConfiguration loadedSettings;
                    using (var reader =
                           MyAPIGateway.Utilities.ReadFileInWorldStorage(ConfigFileName, typeof(DirectionalThrustersOnlyConfiguration)))
                    {
                        loadedSettings = MyAPIGateway.Utilities.SerializeFromXML<DirectionalThrustersOnlyConfiguration>(reader.ReadToEnd());
                    }

                    if (loadedSettings == null || !loadedSettings.Validate())
                    {
                        throw new Exception("DirectionalThrustersOnly: Invalid mod configuration");
                    }

                    SaveSettings(loadedSettings);
                    return loadedSettings;
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole($"DirectionalThrustersOnly: Failed to load mod settings: {e.Message}\n{e.StackTrace}");
                }

                MyAPIGateway.Utilities.WriteBinaryFileInWorldStorage(ConfigFileName + ".old", typeof(DirectionalThrustersOnlyConfiguration));
            }

            var settings = new DirectionalThrustersOnlyConfiguration();
            settings.SetDefaults();
            SaveSettings(settings);
            return settings;
        }

        private static void SaveSettings(DirectionalThrustersOnlyConfiguration settings)
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(DirectionalThrustersOnlyConfiguration)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
                }
                settings.UpdateCalculatedData();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"DirectionalThrustersOnly: Failed to save mod settings: {e.Message}\n{e.StackTrace}");
            }
        }

        private void UpdateCalculatedData()
        {
            blockConfigs = new Dictionary<MyDefinitionId, DirectionalThrustersOnlyConfigurationItem>();

            foreach (var item in ConfigurationItems)
            {
                foreach (var type in item.Types)
                {
                    blockConfigs.Add(new MyDefinitionId(type.TypeId, type.SubtypeId), item);
                }
            }
        }

        private bool Validate()
        {
            if (ConfigurationItems == null || ConfigurationItems.Count == 0)
            {
                return false;
            }

            foreach (var item in ConfigurationItems)
            {
                if (item.FalloffStartDegrees <= 0.0f)
                {
                    item.FalloffStartDegrees = DefaultFalloffStartDegrees;
                }
                if (item.MinThrustDegrees <= 0.0f)
                {
                    item.MinThrustDegrees = DefaultMinThrustDegrees;
                }
                if (item.MinThrustMultiplier < 0.01f)
                {
                    item.MinThrustMultiplier = DefaultMinThrustMultiplierConfig;
                }
            }
            return true;
        }

        private void SetDefaults()
        {
            ConfigurationItems = new List<DirectionalThrustersOnlyConfigurationItem>();

            var definitions = MyDefinitionManager.Static.GetDefinitionsOfType<MyThrustDefinition>();

            if (ConfigurationItems.Count == 0)
            {
                //MyLog.Default.WriteLineAndConsole($"DirectionalThrustersOnly: Adding default blockdefs {definitions.Count}");
                var newItem = new DirectionalThrustersOnlyConfigurationItem()
                {
                    FalloffStartDegrees = DefaultFalloffStartDegrees,
                    MinThrustDegrees = DefaultMinThrustDegrees,
                    MinThrustMultiplier = DefaultMinThrustMultiplierConfig,
                    Types = new List<SerializableDefinitionId>(definitions.Count)
                };

                foreach (var item in definitions)
                {
                    newItem.Types.Add(new SerializableDefinitionId()
                    {
                        TypeId = item.Id.TypeId,
                        SubtypeId = item.Id.SubtypeName,
                    });
                }
                ConfigurationItems.Add(newItem);
            }
        }

        internal DirectionalThrustersOnlyConfigurationItem GetConfigForType(MyDefinitionId id)
        {
            DirectionalThrustersOnlyConfigurationItem config;
            return blockConfigs.TryGetValue(id, out config) ? config : null;
        }
    }
}
public class DirectionalThrustersOnlyConfigurationItem
{
    public List<SerializableDefinitionId> Types { get; set; }
    public float FalloffStartDegrees { get; set; }
    public float MinThrustDegrees { get; set; }
    public float MinThrustMultiplier { get; set; }
}
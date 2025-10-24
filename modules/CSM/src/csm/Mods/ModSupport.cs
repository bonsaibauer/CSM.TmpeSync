using System;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.Plugins;
using CSM.API;
using CSM.Commands;
using CSM.Helpers;
using ICities;

namespace CSM.Mods
{
    internal class ModSupport
    {
        private static ModSupport _instance;
        public static ModSupport Instance => _instance ?? (_instance = new ModSupport());

        public List<Connection> ConnectedMods { get; } = new List<Connection>();
        private readonly List<Connection> _manualConnections = new List<Connection>();

        public List<string> RequiredModsForSync
        {
            get
            {
                return Singleton<PluginManager>.instance.GetPluginsInfo()
                        .Where(ModCompat.NeedsToBePresent).Where(plugin => plugin != null)
                        .Select(plugin => plugin.userModInstance as IUserMod)
                        .Where(mod => mod != null)
                        .Select(mod => mod.Name)
                        .Concat(AssetNames).ToList();
            }
        }

        private static IEnumerable<string> AssetNames
        {
            get
            {
                return PackageManager.FilterAssets(UserAssetType.CustomAssetMetaData)
                    .Where(asset => asset.isEnabled)
                    .Select(asset => new EntryData(asset))
                    .Select(entry => entry.entryName.Split('(')[0].Trim());
            }
        }

        public void Init()
        {
            LoadModConnections();
            Singleton<PluginManager>.instance.eventPluginsChanged += LoadModConnections;
            Singleton<PluginManager>.instance.eventPluginsStateChanged += LoadModConnections;
        }

        private void LoadModConnections()
        {
            ConnectedMods.Clear();
            IEnumerable<Type> handlers = AssemblyHelper.FindClassesInMods(typeof(Connection));

            foreach (Type handler in handlers)
            {
                if (handler.IsAbstract)
                {
                    continue;
                }

                Connection connectionInstance = (Connection)Activator.CreateInstance(handler);

                if (connectionInstance != null)
                {
                    if (connectionInstance.Enabled)
                    {
                        Log.Info($"Mod connected: {connectionInstance.Name}");
                        ConnectedMods.Add(connectionInstance);
                    }
                    else
                    {
                        Log.Debug($"Mod support for {connectionInstance.Name} found but not enabled.");
                    }
                }
                else
                {
                    Log.Warn("Mod failed to instantiate.");
                }
            }

            for (int i = _manualConnections.Count - 1; i >= 0; i--)
            {
                Connection connection = _manualConnections[i];
                if (connection == null)
                {
                    _manualConnections.RemoveAt(i);
                    continue;
                }

                if (!ContainsConnection(connection))
                {
                    ConnectedMods.Add(connection);
                }
            }

            // Refresh data model
            CommandInternal.Instance.RefreshModel();
        }

        public void OnLevelLoaded(LoadMode mode)
        {
            // TODO: Decide by mode if the function should be called
            foreach (Connection mod in ConnectedMods)
            {
                mod.RegisterHandlers();
            }
        }

        public void OnLevelUnloading()
        {
            foreach (Connection mod in ConnectedMods)
            {
                mod.UnregisterHandlers();
            }
        }

        public void DestroyConnections()
        {
            ConnectedMods.Clear();
            ConnectedMods.TrimExcess();
            _manualConnections.Clear();

            Singleton<PluginManager>.instance.eventPluginsChanged -= LoadModConnections;
            Singleton<PluginManager>.instance.eventPluginsStateChanged -= LoadModConnections;
        }

        public bool RegisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            if (!connection.Enabled)
            {
                Log.Debug($"Mod support for {connection.Name ?? connection.GetType().Name} found but not enabled.");
                return false;
            }

            if (ContainsConnection(connection))
                return false;

            _manualConnections.Add(connection);
            ConnectedMods.Add(connection);

            TryRegisterHandlers(connection);

            CommandInternal.Instance.RefreshModel();
            return true;
        }

        public bool UnregisterConnection(Connection connection)
        {
            if (connection == null)
                return false;

            bool removed = RemoveConnection(connection);
            bool removedManual = RemoveManualConnection(connection) || removed;

            if (!removedManual)
                return false;

            TryUnregisterHandlers(connection);

            CommandInternal.Instance.RefreshModel();
            return true;
        }

        public Connection[] GetRegisteredConnections()
        {
            return ConnectedMods.ToArray();
        }

        private bool ContainsConnection(Connection connection)
        {
            return ConnectedMods.Any(existing => AreConnectionsEquivalent(existing, connection));
        }

        private bool RemoveConnection(Connection connection)
        {
            for (int i = ConnectedMods.Count - 1; i >= 0; i--)
            {
                if (AreConnectionsEquivalent(ConnectedMods[i], connection))
                {
                    ConnectedMods.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private bool RemoveManualConnection(Connection connection)
        {
            for (int i = _manualConnections.Count - 1; i >= 0; i--)
            {
                if (AreConnectionsEquivalent(_manualConnections[i], connection))
                {
                    _manualConnections.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private static bool AreConnectionsEquivalent(Connection existing, Connection connection)
        {
            if (existing == null || connection == null)
                return false;

            if (ReferenceEquals(existing, connection))
                return true;

            if (existing.GetType() == connection.GetType())
                return true;

            if (existing.ModClass != null && connection.ModClass != null && existing.ModClass == connection.ModClass)
                return true;

            return false;
        }

        private void TryRegisterHandlers(Connection connection)
        {
            if (!IsLevelLoaded())
                return;

            try
            {
                connection.RegisterHandlers();
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to register handlers for {connection?.Name ?? connection?.GetType().Name}: {ex}");
            }
        }

        private void TryUnregisterHandlers(Connection connection)
        {
            if (!IsLevelLoaded())
                return;

            try
            {
                connection.UnregisterHandlers();
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to unregister handlers for {connection?.Name ?? connection?.GetType().Name}: {ex}");
            }
        }

        private static bool IsLevelLoaded()
        {
            return Singleton<LoadingManager>.exists && Singleton<LoadingManager>.instance.m_loadingComplete;
        }
    }
}

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace OneLife.src
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        //internal static ConfigEntry<bool> PreventEnemyCapture;
        //internal static ConfigEntry<bool> AllowFriendlyAIRescue;
        internal static ConfigEntry<float> CooldownBroadcastIntervalMinutes;

        internal static readonly int DeathCooldownMinutes = 25;
        //internal static readonly int AIRescueCooldownMinutes = 8;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("One Life Mod loading...");

            CooldownBroadcastIntervalMinutes = Config.Bind(
                "General",
                "Cooldown Broadcast Interval (Minutes)",
                1f,
                new ConfigDescription(
                    "How often to broadcast a chat message listing players and their death cooldowns. Nothing is sent if no one is on cooldown. You might need to restart the server if you change this.",
                    new AcceptableValueRange<float>(1f, 10f)));

            /*PreventEnemyCapture = Config.Bind(
                "General", "Prevent Enemy Capture",
                true,
                new ConfigDescription(
                    "When enabled, prevents players from getting captured by enemy AI.",
                    null));*/

            /*AllowFriendlyAIRescue = Config.Bind(
                "General", "Allow Friendly AI Rescue",
                true,
                new ConfigDescription(
                    "When enabled, allows players to get rescued by friendly AI.",
                    null));*/

            harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            // Broadcast handler functions that need to fire repeatedly.
            float intervalSeconds = CooldownBroadcastIntervalMinutes.Value * 60f;
            InvokeRepeating(nameof(BroadcastCooldowns), intervalSeconds, intervalSeconds);
            InvokeRepeating(nameof(BroadcastFinishedCooldowns), 2, 2);

            Log.LogInfo("One Life loaded.");
        }

        private void BroadcastCooldowns()
        {
            BroadcastHandler.BroadcastIfAnyOnCooldown();
        }

        private void BroadcastFinishedCooldowns()
        {
            FinishedCooldownBroadcaster.BroadcastFinishedCooldowns();
        }
        private void OnDestroy()
        {
            CancelInvoke(nameof(BroadcastCooldowns));
            CancelInvoke(nameof(BroadcastFinishedCooldowns));
            harmony?.UnpatchSelf();
        }
    }
}


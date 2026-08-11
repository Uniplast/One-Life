using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;

namespace OneLife
{
    /* This runs whenever somebody gets rescued by a friendly unit (AI or player)
       But NOT when they get captured by enemy or if they get "rescued" by a friendly airbase.
       This is used to detect when a player does a successful sortie after rescuing somebody and
       also detects if a rescuer's plane died after they rescued somebody.
    */
    [HarmonyPatch(typeof(FactionHQ), "ReportRescuePilotsAction")]
    public static class RescueHandler
    {
        public static void Postfix(FactionHQ __instance, Player player, PilotDismounted pilotDismounted)
        {
            if (!ServerDetection.IsServer) return;

            Player rescuedPlayer = pilotDismounted.Networkplayer;

            if (rescuedPlayer == null) return; //AI pilot got rescued, so who cares?

            string rescuedName = rescuedPlayer.GetDisplayName(PlayerNameContext.Other);
            string rescuerName = player != null ? player.GetDisplayName(PlayerNameContext.Other) : "somebody";

            Plugin.Log.LogInfo($"{rescuedName} was rescued by {rescuerName}.");

            Aircraft rescuerAircraft = player?.Aircraft;
            if (rescuerAircraft == null)
            {
                /* Apparently the rescuer's aircraft will become null if the pilot ejects,
                   so if that happens during a rescue, I need to handle that.
                */
                Plugin.Log.LogWarning($"It appears {rescuerName} ejected or something during a rescue. {rescuedName} remains on cooldown.");
                return;
            }

            RescueState.AwaitingSortie.Add(rescuedPlayer);

            //Tell rescuer to return to base in order to rescue player.
            NotifyRescuerToRTB(player, rescuedName);

            Plugin.Log.LogInfo($"Waiting for {rescuerName}'s aircraft to complete a successful sortie before unblocking {rescuedName}'s airframe selection.");

            // Event listener bound to the rescuer's aircraft that detects successful sorties.
            void OnSortieSuccessful(float bonus)
            {
                rescuerAircraft.onSortieSuccessful -= OnSortieSuccessful;
                rescuerAircraft.onDisableUnit -= OnRescuerAircraftDisabled;
                RescueState.AwaitingSortie.Remove(rescuedPlayer);
                Unblock(rescuedPlayer, rescuedName);
                NotifyPlayerRescued(rescuedPlayer);
            }

            // Event listener bound to rescuer's aircraft that detects if aircraft got shot down before completing the rescue.
            void OnRescuerAircraftDisabled(Unit disabledUnit)
            {
                rescuerAircraft.onSortieSuccessful -= OnSortieSuccessful;
                rescuerAircraft.onDisableUnit -= OnRescuerAircraftDisabled;
                RescueState.AwaitingSortie.Remove(rescuedPlayer);

                Plugin.Log.LogInfo($"{rescuerName}'s aircraft was destroyed before completing a sortie. {rescuedName} remains on cooldown.");
            }

            rescuerAircraft.onSortieSuccessful += OnSortieSuccessful;
            rescuerAircraft.onDisableUnit += OnRescuerAircraftDisabled;
        }

        //Tell player they need to return to a friendly airbase and do a sortie to rescue player.
        private static void NotifyRescuerToRTB(Player rescuer, string rescuedName)
        {
            if (rescuer == null || rescuer.Owner == null) return;

            ChatManager chatManager = NetworkSceneSingleton<ChatManager>.i;
            if (chatManager == null) return;

            string message = $"<color=#FF9822FF>Ok, you need to RTB, land, and exit your aircraft to successfully rescue {rescuedName}</color>.";
            chatManager.RpcTargetServerMessage(rescuer.Owner, message, true);
        }

        //Tell whole server that a player was rescued.
        private static void NotifyPlayerRescued(Player player)
        {
            if (player == null) return;

            string msg = $"<color=#FF9822FF>{player.GetDisplayName(PlayerNameContext.Other)} has been rescued!</color>";
            MissionMessages.ShowMessage(msg, true, null, true);
        }

        private static void Unblock(Player player, string name)
        {
            if (RescueState.Blocked.Remove(player))
            {
                RescueState.DeathCooldown.Remove(player);
                RescueState.StillFalling.Remove(player);

                Plugin.Log.LogInfo($"{name} has been rescued and their rescuer completed a successful sortie! Player can now respawn!");
            }
        }
    }
}

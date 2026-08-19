using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;
using System.Threading.Tasks;

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

            // Checks if there's a steam ID associated with the rescued player and saves their steam ID
            // into rescuedSteamId if they exist.
            bool grabbedRescuedSteamId = PlayerIdentityHelper.TryGetSteamId(rescuedPlayer, out ulong rescuedSteamId);

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
            NotifyRescuerToRTB(player, rescuedPlayer);

            Plugin.Log.LogInfo($"Waiting for {rescuerName}'s aircraft to complete a successful sortie before unblocking {rescuedName}'s airframe selection.");

            // Event listener bound to the rescuer's aircraft that detects successful sorties.
            void OnSortieSuccessful(float bonus)
            {
                rescuerAircraft.onSortieSuccessful -= OnSortieSuccessful;
                rescuerAircraft.onDisableUnit -= OnRescuerAircraftDisabled;
                RescueState.AwaitingSortie.Remove(rescuedPlayer);

                /* At this point, rescuedPlayer might be null because the player disconnected and then
                   reconnected while being rescued. So, we now grab their new 'Player' object reference
                   in ConnectedPlayers structure that's associated with their Steam ID so we can reassociate
                   their new 'Player' object throughout the rest of the mod.
                */
                Player currentPlayer = rescuedPlayer;
                if (grabbedRescuedSteamId && RescueState.ConnectedPlayers.TryGetValue(rescuedSteamId, out Player livePlayer))
                {
                    currentPlayer = livePlayer;
                }

                if (currentPlayer != null)
                {
                    Unblock(currentPlayer, rescuedName);
                }
                else if (grabbedRescuedSteamId)
                {
                    /* The player is currently disconnected, but go ahead and remove them from the cooldown, now that
                       they've been rescued while disconnected.
                    */
                    RescueState.CooldownRemaining.Remove(rescuedSteamId);
                    RescueState.EjectedCooldownRemaining.Remove(rescuedSteamId);
                    Plugin.Log.LogInfo($"{rescuedName} is currently disconnected - cancelled their persisted cooldown instead.");
                }

                NotifyPlayerRescued(rescuerName, rescuedName);
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
        private static async Task NotifyRescuerToRTB(Player rescuer, Player rescuedPlayer)
        {
            if (rescuer == null || rescuer.Owner == null) return;

            ChatManager chatManager = NetworkSceneSingleton<ChatManager>.i;
            if (chatManager == null) return;

            string rescuerName = rescuer.GetDisplayName(PlayerNameContext.Other);
            string rescuedName = rescuedPlayer.GetDisplayName(PlayerNameContext.Other);

            string message = $"<color=#FF9822FF>Ok, you need to RTB, land, come to a stop, and exit your aircraft to successfully</color><color=#FFFFFFFF> rescue {rescuedName}</color>.";
            chatManager.RpcTargetServerMessage(rescuer.Owner, message, true);

            string savedMessage = $"<color=#FF9822FF>You've been picked up by</color><color=#21FF68FF> {rescuerName}!</color><color=#FF9822FF> They must now land at a controlled airfield, come to a full stop, and exit their aircraft to rescue you!</color>";
            chatManager.RpcTargetServerMessage(rescuedPlayer.Owner, savedMessage, true);

            await Task.Delay(15000);

            string rescueServerMessage = $"<color=#21FF68FF>{rescuedName}</color><color=#FF9822FF> has been picked up by <color=#21FF68FF>{rescuerName}</color>! <color=#FF9822FF>Go protect them so they can RTB and respawn!</color>";
            MissionMessages.ShowMessage(rescueServerMessage, true, null, true);
        }

        //Tell whole server that a player was rescued.
        private static void NotifyPlayerRescued(string rescuerName, string rescuedName)
        {
            //if (player == null) return;

            string msg = $"<color=#21FF68FF>{rescuedName}</color><color=#FF9822FF> has been </color><color=#21FF68FF>rescued</color><color=#FF9822FF> by</color><color=#FFFFFFFF> {rescuerName}!</color>";
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

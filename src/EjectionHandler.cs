using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;
using System;

namespace OneLife
{
    /* Detecting ejections was weird and I still don't think I have it quite right, but this is working for now.
       Uses the OnStartServer() function because this function still runs the PilotDismounted.Setup() function handling
       all of the pilot ejection physics. It also seems to be the only function that runs immediately upon ejection
       and is also a Mirage function, so it's basically guaranteed to be more stable. Plus, it's less likely to get renamed
       in a future game update. So using this function will help prevent the mod from breaking in a future game update.
       It'll probably still break, though, TBH lol.
    */
    [HarmonyPatch(typeof(PilotDismounted), "OnStartServer")]

    /* This handler needs to check for multiple types of ejections:
     * While flying (wether you got shot down or not is irrelevant)
     * While stationary on the ground in or near a friendly airbase
     * While doing a sortie
       
       They all have their own detection logic.
    */
    public static class EjectionHandler
    {
        
        private const float StationarySpeedThreshold = 15.0f;

        public static void Postfix(PilotDismounted __instance)
        {

            //If this code isn't running on the server-side, then don't run.
            if (!ServerDetection.IsServer) return;

            Player player = __instance.Networkplayer;
            if (player == null)
            {
                /* This removes the AI pilot(s) from your plane when you eject. Had an issue where a player ejected (with their AI co-pilot),
                   and somebody rescued their AI pilot and not them by accident lol.
                   Considering this mod REQUIRES you to rescue players to get them back (unless their cooldown times out),
                   and since there is no visual difference between AI pilots and player pilots, I just decided to derezz
                   the AI pilot when they all eject from the aircraft.
                */ 
                UnityEngine.Object.Destroy(__instance.gameObject); //DIE YOU FUCKING AI PILOT!!
                return;
            }

            /* Check if player landed under safe conditions (basically same as a sortie)
             * Plane is on the ground
             * Plane isn't moving (or at least less than the threshold defined above)
             * Player ejected in or near a friendly airbase
               In this instance, we don't want to punish them. This is so that players who spawned and haven't taken off yet
               can eject to change their loadout if they need or forgot to before they spawned.
            */
            if (IsSafeLandedDismount(__instance, player))
            {
                Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} dismounted under safe conditions. YIPPEE!!");
                return;
            }

            /* Otherwise, they ejected in midair for whatever reason:
             * To save themselves after getting shot
             * To spite the person they rescued
             * For shits and giggles
            */
            RescueState.Blocked.Add(player);
            RescueState.StillFalling.Add(player);

            DateTime timeoutUntil = DateTime.UtcNow.AddMinutes(Plugin.DeathCooldownMinutes);
            RescueState.DeathCooldown[player] = timeoutUntil;

            Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} ejected. Blocked from selecting a new airframe until rescued, or for up to {Plugin.DeathCooldownMinutes} minute(s) if no one gets to them first.");
        }

        //Checks if player is still on the ground, not moving, within or near a friendly airbase.
        private static bool IsSafeLandedDismount(PilotDismounted pilotDismounted, Player player)
        {
            if (player.HQ == null) return false;

            if (!UnitRegistry.TryGetUnit<Aircraft>(pilotDismounted.parentUnit, out Aircraft aircraft) || aircraft == null)
            {
                return false; // can't confirm the aircraft - default to blocking
            }

            if (!aircraft.IsLanded()) return false;
            if (aircraft.speed > StationarySpeedThreshold) return false;
            if (!player.HQ.AnyNearAirbase(aircraft.transform.position, out _)) return false;

            return true;
        }
    }

    /* Need to do this instead of directly patching the CheckLanded() function because its
       data members are private... So this little workaround works because the CheckLanded()
       function fires off CheckForCapture() immediately after a pilot lands after ejecting and parachuting down.
       I hope this will continue to work well lol.
    */
    [HarmonyPatch(typeof(PilotDismounted), "CheckForCapture")]
    public static class LandingDetectionPatch
    {
        public static void Prefix(PilotDismounted __instance)
        {
            if (!ServerDetection.IsServer) return;

            Player player = __instance.Networkplayer;
            if (player == null) return;

            RescueState.StillFalling.Remove(player);
        }
    }

    /* OnDestroy() runs every time a unit gets destroyed and despawned for any reason:
     * Captured by enemy
     * Rescued by friendly AI
     * Killed after ejecting
     * Rescued by a friendly airbase
     * Maybe more I'm not aware of
       
       This is basically being used to detect and implement player-rescuing-player mechanics.
    */
    [HarmonyPatch(typeof(Unit), "OnDestroy")]
    public static class EjectionBlockEndPatch
    {
        public static void Postfix(Unit __instance)
        {
            if (!ServerDetection.IsServer) return;
            if (!(__instance is PilotDismounted pilotDismounted)) return; //Make sure object is a pilot

            Player player = pilotDismounted.Networkplayer;
            if (player == null) return; //No AI pilots here

            if (RescueState.AwaitingSortie.Contains(player))
            {
                // An actual player-rescuing-player stuff and the rescued pilot is not waiting 
                // for their rescuer to do a successful sortie.
                return;
            }

            if (RescueState.Blocked.Remove(player))
            {
                /* This needs to be cleared here otherwise a player who is rescued by friendly airbase
                   would get put on cooldown, and we don't want that.
                */
                RescueState.DeathCooldown.Remove(player);
                RescueState.StillFalling.Remove(player);

                Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} walked back to airbase. Not blocking");
            }
        }
    }

    /* Basically prevents players from respawning if they have a cooldown
       from dying or from waiting for rescue. 
    */
    [HarmonyPatch(typeof(Spawner), "AllowedToSpawn")]
    public static class SpawnBlockPatch
    {
        public static bool Prefix(Player __3, ref bool __result)
        {
            if (!ServerDetection.IsServer) return true;
            if (__3 == null) return true;

            if (RescueState.Blocked.Contains(__3))
            {
                // Check if they are ejected and still waiting to be rescued.
                if (RescueState.DeathCooldown.TryGetValue(__3, out DateTime timeoutUntil) &&
                    DateTime.UtcNow < timeoutUntil)
                {
                    NotifyStillWaiting(__3, timeoutUntil, stillEjected: true);
                    __result = false;
                    return false;
                }

                //Otherwise, let them spawn YIPPEE
                RescueState.Blocked.Remove(__3);
                RescueState.AwaitingSortie.Remove(__3);
                RescueState.DeathCooldown.Remove(__3);
                RescueState.StillFalling.Remove(__3);

                Plugin.Log.LogInfo($"{__3.GetDisplayName(PlayerNameContext.Other)}'s eject timeout expired before anyone rescued them. Unblocking...");

                return true;
            }

            //Check if they're on a death cooldown.
            if (RescueState.DeathCooldown.TryGetValue(__3, out DateTime cooldownUntil))
            {
                if (DateTime.UtcNow < cooldownUntil)
                {
                    NotifyStillWaiting(__3, cooldownUntil, stillEjected: false);
                    __result = false;
                    return false;
                }

                // Cooldown expired.
                RescueState.DeathCooldown.Remove(__3);
            }

            return true;
        }

        private static void NotifyStillWaiting(Player player, DateTime until, bool stillEjected)
        {
            if (player.Owner == null) return;

            //TODO: I should make a more unified chat manager class. I'll do that later.
            ChatManager chatManager = NetworkSceneSingleton<ChatManager>.i;
            if (chatManager == null) return;

            double minutesLeft = (until - DateTime.UtcNow).TotalMinutes;
            string message = stillEjected ? $"<color=#FF9822FF>You're still waiting to be rescued! You'll be able to respawn in {minutesLeft:0} minute(s) if no one rescues you first.</color>" : $"<color=#FF9822FF>You're still on cooldown! {minutesLeft:0} minute(s) remaining before you can respawn.</color>";

            chatManager.RpcTargetServerMessage(player.Owner, message, true);
        }
    }
}

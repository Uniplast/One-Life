using HarmonyLib;
using NuclearOption.Networking;
using System;

namespace OneLife
{
    /* Need to check if a pilot died in the cockpit in a sorta strange way, unless I find a better way later.
       This runs every time a pilot gets damaged while in the cockpit for any reason. So, when a pilot does get damaged while flying,
       I check to see if it was a lethal hit, and then put the player on death cooldown if it was.
       This basically checks for a player death BEFORE they had a chance to eject from the plane.
    */
    [HarmonyPatch(typeof(Pilot), "ApplyDamage")]
    public static class InCockpitDeathPatch
    {
        /* This is a bit strange. Needing the prefix here allows the postfix function
           to know that they were already dead if they kept getting shot after they died.
           This ALSO prevents dublicate firings of the function when/if thier AI copilot(s)
           take damage and/or die after the player dies. There's some strange crap that
           happens if they keep getting shot after they die, and when the game registers
           their copilot(s) died after the player died, so I can prevent that fuckery
           from happening by also using the prefix.
        */
        public static void Prefix(Pilot __instance, out bool __state)
        {
            __state = __instance.dead;
        }

        public static void Postfix(Pilot __instance, bool __state)
        {
            if (!ServerDetection.IsServer) return;

            if (__state) return; // Already dead before this call. This was not the lethal hit.
            if (!__instance.dead) return; // This hit wasn't lethal
            if (__instance.aircraft == null) return;

            /* Needing to grab all pilots in the damaged player's aircraft and use that to figure out
               WHICH pilot died (player vs AI copilot). We don't want the AI copilot's death to cause
               the player to go onto cooldown.
            */
            Player player = __instance.aircraft.Player;

            Pilot[] pilots = __instance.aircraft.pilots;
            Pilot primarySeat = (pilots?.Length > 0) ? pilots[0] : null;

            if (primarySeat != __instance) return; // AI copilot in a player-controlled aircraft.

            if (player == null) return; // Stupid AI pilot, so who cares if they got shot??

            if (RescueState.DeathCooldown.ContainsKey(player)) return;

            RescueState.Blocked.Remove(player);
            RescueState.AwaitingSortie.Remove(player);

            DateTime cooldownUntil = DateTime.UtcNow.AddMinutes(Plugin.DeathCooldownMinutes);
            RescueState.DeathCooldown[player] = cooldownUntil;

            string msg = $"<color=#FF9822FF>{player.GetDisplayName(PlayerNameContext.Other)} was killed and is on cooldown!</color>";
            MissionMessages.ShowMessage(msg, true, null, true);

            Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} was killed in the cockpit and is blocked from selecting a new airframe for {Plugin.DeathCooldownMinutes} minute(s).");
        }
    }

    // This fires when a pilot dies AFTER ejecting and BEFORE they could be rescued.
    [HarmonyPatch(typeof(PilotDismounted), "KillPilot")]
    public static class KillPatch
    {
        public static void Postfix(PilotDismounted __instance)
        {
            if (!ServerDetection.IsServer) return;

            Player player = __instance.Networkplayer;
            if (player == null) return; // AI pilot so do nothing.

            string msg = $"<color=#FF9822FF>{player.GetDisplayName(PlayerNameContext.Other)} was killed after ejecting and is on cooldown!</color>";
            MissionMessages.ShowMessage(msg, true, null, true);

            RescueState.Blocked.Remove(player);
            RescueState.AwaitingSortie.Remove(player);
            RescueState.StillFalling.Remove(player);

            /* Check to see if the player already has a cooldown so we don't reset it. Players
               were getting their cooldowns reset back to 20 minutes when they died waiting for rescue.
               We only want the players to suffer through a cooldown for a MAXIMUM of what the cooldown
               is set to. Some players were dying after ejecting close to when their cooldown was
               going to expire, only to have their cooldown reset when they died. This check here fixes it.
            */

            if (!RescueState.DeathCooldown.ContainsKey(player))
            {
                DateTime cooldownUntil = DateTime.UtcNow.AddMinutes(Plugin.DeathCooldownMinutes);
                RescueState.DeathCooldown[player] = cooldownUntil;
            }

            Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} was killed while ejected and is blocked from selecting a new airframe for {Plugin.DeathCooldownMinutes} minute(s).");
        }
    }
}

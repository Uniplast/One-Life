using NuclearOption.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OneLife.src
{
    /* Periodically broadcasts remaining cooldown times to all players
     via the game's own faction message system, so players can see
     who's still waiting for their cooldown to expire.
    */
    internal static class BroadcastHandler
    {
        public static void BroadcastIfAnyOnCooldown()
        {
            // Don't run on a client, otherwise clients will get duplicate messages.
            if (!ServerDetection.IsServer) return;

            DateTime now = DateTime.UtcNow;

            // Don't broadcast anything if nobody is on cooldown.
            if (RescueState.DeathCooldown.Count == 0) return;

            //Get a list of all players on cooldown.
            List<string> entries = RescueState.DeathCooldown
                .Where(kvp => !RescueState.StillFalling.Contains(kvp.Key))
                .Select(kvp => $"{kvp.Key.GetDisplayName(PlayerNameContext.Other)} " +
                                $"({(kvp.Value - now).TotalMinutes:0.0} minutes.)")
                .ToList();

            if (entries.Count == 0) return;

            string message = $"<color=#FF9822FF>Respawn cooldowns: " + string.Join(", ", entries);
            message += "</color>";

            // Broadcast message to entire server.
            MissionMessages.ShowMessage(message, true, null, true);
        }
    }

    //Broadcasts message to player's entire faction that they're no longer on a cooldown.
    internal class FinishedCooldownBroadcaster
    {
        public static void BroadcastFinishedCooldowns()
        {
            if (!ServerDetection.IsServer) return;

            DateTime now = DateTime.UtcNow;

            // Checks for all expired cooldowns
            List<Player> expired = RescueState.DeathCooldown
                .Where(kvp => kvp.Value <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            // Removes player from being blocked in case it wasn't done elsewhere
            foreach (Player player in expired)
            {
                bool wasEjected = RescueState.Blocked.Remove(player);
                RescueState.AwaitingSortie.Remove(player);
                RescueState.StillFalling.Remove(player);

                //Broadcasts message
                string msg = wasEjected
                    ? $"<color=#21FF68FF>{player.GetDisplayName(PlayerNameContext.Other)}'s eject timeout expired - " +
                      "they can now select a new airframe!</color>"
                    : $"<color=#21FF68FF>{player.GetDisplayName(PlayerNameContext.Other)} is no longer on cooldown!</color>";

                MissionMessages.ShowMessage(msg, true, null, true);
                RescueState.DeathCooldown.Remove(player);
            }
        }
    }
}

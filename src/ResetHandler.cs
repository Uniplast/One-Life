using HarmonyLib;
using NuclearOption.Networking;

namespace OneLife
{
    /* I was under the mistaken impression that the game would reinitialize the mod
       when the mission ends and/or when a new mission begins. Apparently NOT!! So,
       I had to create this little extra handler to do the resetting for me when a
       new mission starts. Otherwise, players that had cooldowns in the previous mission
       would still have a cooldown in the next mission, too, and that's not good, obviously.
       So, upon the start of ANY mission, I'm just resetting all the data structures that
       are listed in RescueState.
    */
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "ServerMissionStart")]
    public static class ResetHandler
    {
        public static void Postfix()
        {
            if (!ServerDetection.IsServer) return;

            int blockedCount = RescueState.Blocked.Count;
            int cooldownCount = RescueState.DeathCooldown.Count;

            RescueState.Blocked.Clear();
            RescueState.AwaitingSortie.Clear();
            RescueState.DeathCooldown.Clear();
            RescueState.CooldownRemaining.Clear();
            RescueState.EjectedCooldownRemaining.Clear();
            RescueState.StillFalling.Clear();

            /* Most likely not needed because the ConnectionHandler always grabs a fresh 'Player' object
               and associates it with the player's Steam ID and also removes the player from the dictionary
               when they disconnect. Theoretically it should never need to be cleared because it "refreshes"
               itself whenever a player connects. I added it anyways because I'm a perfectionist and just want
               to make sure there are no surprises or loose ends.
            */
            RescueState.ConnectedPlayers.Clear();

            Plugin.Log.LogInfo($"New mission started. RescueState reset. {blockedCount} blocs and {cooldownCount} cooldowns cleared.");
        }
    }
}

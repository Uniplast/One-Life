using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;
using NuclearOption.Networking.Authentication;
using System;

namespace OneLife.src
{
    /* Player objects get created in the "SpawnCharacter" function. This is used to restore cooldown
       of players who disconnected with a cooldown and then reconnected.
    */
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "SpawnCharacter")]
    public static class ConnectionHandler
    {
        public static void Postfix(Mirage.INetworkPlayer __0)
        {
            if (!ServerDetection.IsServer) return;
            if (__0 == null) return;
            if (!PlayerHelper.TryGetPlayer<Player>(__0, out Player player) || player == null) return;

            if (!PlayerIdentityHelper.TryGetSteamId(player, out ulong steamId)) return;
            
            if (!RescueState.CooldownRemaining.TryGetValue(steamId, out TimeSpan remaining)) return;

            RescueState.CooldownRemaining.Remove(steamId);
            bool wasEjected = RescueState.EjectedCooldownRemaining.Remove(steamId);

            if (remaining <= TimeSpan.Zero) return; // already expired while disconnected - nothing to restore

            DateTime until = DateTime.UtcNow + remaining;

            if (wasEjected)
            {
                // Readd them to blocked if their cooldown wasn't expired when they disconnected.
                RescueState.Blocked.Add(player);
            }

            RescueState.DeathCooldown[player] = until;

            Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} reconnected with {remaining.TotalMinutes:0.0} minute(s) remaining on their cooldown.");
        }
    }

    // Used to get unique steam ID of players, so tracking cooldowns is much more reliable than just
    // using a player-defined username.
    internal static class PlayerIdentityHelper
    {
        internal static bool TryGetSteamId(Player player, out ulong steamId)
        {
            steamId = 0;

            if (player == null || player.Owner == null) return false;

            NetworkAuthenticatorNuclearOption.AuthData authData = PlayerHelper.GetAuthData(player.Owner);
            if (authData == null) return false;

            steamId = authData.SteamID.m_SteamID;
            return steamId != 0;
        }
    }

    /* Used for telling players what this mod does when they connect to the server
       Need to use RpcTargetServerMessage because the other function that sends messages
       directly to individual players apparently removes any kind of text formatting information
       from the message, so I can't make this message orange without using this method.
    */
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "SpawnCharacter")]
    public static class ConnectMessageBroadcast
    {
        // TODO: replace with the actual welcome/explainer text.
        private const string WelcomeMessage = $"<color=#FF9822FF>This is One Life Only! Your pilot needs to survive for you to take off in a new aircraft. If you eject, you can be rescued by helis or a brave cricket pilot with no sense of self-preservation.\n• First, they have to land right next to you and pick you up.\n• Next, they need to return to base with you safely to drop you off (successful sortie).\n• And then you should be able to spawn again.</color>";

        public static void Postfix(Mirage.INetworkPlayer __0)
        {
            if (!ServerDetection.IsServer) return;
            if (__0 == null) return;
            if (!PlayerHelper.TryGetPlayer<Player>(__0, out Player player) || player == null) return;
            if (player.Owner == null) return;

            ChatManager chatManager = NetworkSceneSingleton<ChatManager>.i;
            if (chatManager == null) return;

            chatManager.RpcTargetServerMessage(player.Owner, WelcomeMessage, true);
        }
    }

    // Checks if player has any cooldowns when they disconnect, and puts those cooldowns
    // into a separate persistent cooldown data structure to be restored if they reconnect.
    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "OnServerDisconnect")]
    public static class PlayerDisconnectedPatch
    {
        public static void Prefix(Mirage.INetworkPlayer __0)
        {
            if (!ServerDetection.IsServer) return;
            if (__0 == null) return;
            if (!PlayerHelper.TryGetPlayer<Player>(__0, out Player player) || player == null) return;

            if (RescueState.DeathCooldown.TryGetValue(player, out DateTime until))
            {
                TimeSpan remaining = until - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero && PlayerIdentityHelper.TryGetSteamId(player, out ulong steamId))
                {
                    bool wasEjected = RescueState.Blocked.Contains(player);

                    RescueState.CooldownRemaining[steamId] = remaining;
                    if (wasEjected)
                    {
                        RescueState.EjectedCooldownRemaining.Add(steamId);
                    }
                    else
                    {
                        RescueState.EjectedCooldownRemaining.Remove(steamId);
                    }

                    Plugin.Log.LogInfo($"{player.GetDisplayName(PlayerNameContext.Other)} disconnected with {remaining.TotalMinutes:0.0} minute(s) remaining on their cooldown. Information saved for when they reconnect.");
                }
            }

            //Remove disconnected player from being blocked from spawn just to keep all the data organized.
            //All of the relevant blockings/cooldowns will be restored if they reconnect later.
            RescueState.Blocked.Remove(player);
            RescueState.AwaitingSortie.Remove(player);
            RescueState.DeathCooldown.Remove(player);
            RescueState.StillFalling.Remove(player);
        }
    }
}

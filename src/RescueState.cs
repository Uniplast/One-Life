using System;
using System.Collections.Generic;
using NuclearOption.Networking;

namespace OneLife
{
    /*RescueState keeps track of all the players who are blocked from spawning for all the different reasone:
      *Waiting for rescuer to RTB and successful sortie
      *Waiting for cooldown to expire
      *Ejecting and waiting for rescue
      *Dying
      *Also leaving the server while on cooldown
    */
    internal static class RescueState
    {
        /* Players blocked from spawning */
        internal static readonly HashSet<Player> Blocked = new HashSet<Player>();

        /* Players waiting for their rescuer to do a sortie */
        internal static readonly HashSet<Player> AwaitingSortie = new HashSet<Player>();

        /* Players who are on cooldown after dying or after ejecting and waiting for rescue
           It used to be that this only kept track of player cooldown for players who died, but
           we decided to also make a cooldown for players ejecting and waiting to be rescued,
           so the name of this variable still says "Death" instead of something more generic.
        */
        internal static readonly Dictionary<Player, DateTime> DeathCooldown = new Dictionary<Player, DateTime>();

        /* Keeps track of ALL cooldowns. Needed for now until I figure out a better way to do this.
           When players on cooldown disconnect, they get moved to this dictionary and it stores the
           amount of time they have left on their cooldown when they disconnected, so their cooldown
           can be restored if they reconnect. No disconnect-reconnect scumming here. :)
        */
        internal static readonly Dictionary<ulong, TimeSpan> CooldownRemaining = new Dictionary<ulong, TimeSpan>();

        /* Also using this until I figure out a better way to do this. This keeps track of which players
           are on a cooldown specifically for ejections instead of death cooldowns. Only the ejection cooldown
           needs to put a player in the Blocked dictionary when they reconnect, since that's the only cooldown
           that can end early from a rescue.
        */
        internal static readonly HashSet<ulong> EjectedCooldownRemaining = new HashSet<ulong>();

        /* Players that ejected and are still falling to the ground. Basically only used for the broadcasthandler
           so it doesn't broadcast cooldown messages and include a player who is still falling to the ground and
           who's fate is still undetermined. Only when they land on the ground will it be more clear what their
           fate entails. In this case, it just prevents the broadcaster from saying they're on cooldown in case
           they land near enough to a friendly airbase to get rescued immediately from them. We don't want a cooldown
           if that happens, so broadcasting that they're on cooldown while they're still falling can be really misleading.
        */
        internal static readonly HashSet<Player> StillFalling = new HashSet<Player>();

        /* Maps a 'Player' object to their Steam ID. Used for keeping track of each player's
           'Player' object when they connect or disconnect, so the cooldown system can still function
            correctly when players disconnect or reconnects.
        */
        internal static readonly Dictionary<ulong, Player> ConnectedPlayers = new Dictionary<ulong, Player>();
    }
}

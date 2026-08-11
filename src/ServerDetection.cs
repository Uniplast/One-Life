using NuclearOption.Networking;

namespace OneLife.src
{
    /* Used for detecting if this mod instance is running server-side or client-side. The vast majority
       of the code we want running only server-side or else we get a lot of strange behaviour. Took a while
       to find out how the game was differentiating this.
    */
    internal static class ServerDetection
    {
        internal static bool IsServer
        {
            get
            {
                NetworkManagerNuclearOption manager = NetworkManagerNuclearOption.i;
                return manager != null && manager.Server != null && manager.Server.Active;
            }
        }
    }
}
using HarmonyLib;
using NuclearOption.Networking;

namespace OneLife
{
    /* Detects when a player gets captured or rescued by an AI.
       Apparently, the game doesn't use different functions for that. It runs the same "Capture" function,
       and uses other information like the capturing unit's and captured unit's faction to determine
       if it was a capture or a rescue.
    */ 
    [HarmonyPatch(typeof(PilotDismounted), "Capture")]
    public static class CaptureHandler
    {
        public static bool Prefix(PilotDismounted __instance, Unit capturingUnit)
        {
            //Needs to not run on the client, so returns if this instance isn't a server.
            if (!ServerDetection.IsServer) return true;

            if (capturingUnit == null) return true;

            //Enemy capture attempt
            if (capturingUnit.NetworkHQ != __instance.NetworkHQ)
            { 
                Plugin.Log.LogInfo("Blocked an enemy capture attempt - pilot remains in the field.");
                return false; //Return to block the capture.
            }

            //Friendly AI rescue attempt. This does not apply to friendly airbases. They can still rescue you.
            Aircraft rescuerAircraft = capturingUnit as Aircraft;
            if (rescuerAircraft == null || rescuerAircraft.Player == null)
            {
                Plugin.Log.LogInfo("Blocked a friendly AI rescue attempt.");
                return false;
            }

            //Otherwise, this is a rescue by ONLY a friendly player.
            return true;
        }
    }
}

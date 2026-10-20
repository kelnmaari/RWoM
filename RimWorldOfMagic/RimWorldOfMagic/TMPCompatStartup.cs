using System;
using RimWorld;
using Verse;

namespace TorannMagic
{
    /// <summary>
    /// Initializes the Multiplayer compatibility layer after ALL mod classes have
    /// been constructed — by that time the Multiplayer fork's API is initialized
    /// and MP.RegisterSyncMethod targets the real implementation, not the Dummy.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class TMPCompatStartup
    {
        static TMPCompatStartup()
        {
            if (TMPCompat.MultiplayerActive())
            {
                // Never let a compatibility failure break the mod constructor chain.
                try
                {
                    TMPCompat.Setup();
                }
                catch (Exception ex)
                {
                    Log.Error("[TMPCompat] setup failed: " + ex);
                }
            }
        }
    }
}

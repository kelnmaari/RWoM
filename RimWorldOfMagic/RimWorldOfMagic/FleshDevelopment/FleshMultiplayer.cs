using Multiplayer.API;
using Verse;

namespace TorannMagic.FleshDevelopment
{
    public static class FleshMultiplayer
    {
        // Resolve the owner inside the synchronized execution: serializing both pawns
        // would make Multiplayer reject owners and undead on different maps.
        public static void StopFeast(Pawn undead)
        {
            FleshActions.StopFeast(FleshRepository.Owner(undead), undead);
        }

        public static void Register()
        {
            MP.RegisterSyncMethod(typeof(FleshActions), nameof(FleshActions.Upgrade));
            MP.RegisterSyncMethod(typeof(FleshActions), nameof(FleshActions.DebugLevelUp)).SetDebugOnly();
            MP.RegisterSyncMethod(typeof(FleshActions), nameof(FleshActions.TryActivate));
            MP.RegisterSyncMethod(typeof(FleshActions), nameof(FleshActions.TryDevour));
            MP.RegisterSyncMethod(typeof(JobDriver_FleshCast), nameof(JobDriver_FleshCast.TryStart));
            MP.RegisterSyncMethod(typeof(FleshMultiplayer), nameof(StopFeast));
            MP.RegisterSyncMethod(typeof(FleshActions), nameof(FleshActions.StopAll));
        }
    }
}

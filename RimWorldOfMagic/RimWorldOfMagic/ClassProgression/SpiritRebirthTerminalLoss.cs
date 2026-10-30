using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using RimWorld.Planet;
using Verse;

namespace TorannMagic.ClassProgression
{
    internal enum SpiritTerminalLossResult { Rejected, Unavailable, Completed }

    // Only an explicitly authorized terminal map/caravan-loss caller may invoke
    // this entry. Ordinary damage, Destroy and recoverable collapse never do.
    // A future caller must durably authorize a partial native failure: this
    // live-owner admission deliberately cannot retry a detached/destroyed pawn.
    internal static class SpiritRebirthTerminalLoss
    {
        private static readonly MethodInfo discard = typeof(WorldPawns).GetMethod("DiscardPawn",
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Pawn), typeof(bool) }, null);

        internal static SpiritTerminalLossResult TryDiscard(Pawn original, ThingOwner expectedOwner)
        {
            if (original == null || original.Dead || original.Destroyed || original.Discarded || original.Spawned
                || !SpiritRebirthState.TryGet(original, out _)
                || expectedOwner?.Owner == null || !ReferenceEquals(original.holdingOwner, expectedOwner)
                || !ReferenceEquals(expectedOwner.Owner.GetDirectlyHeldThings(), expectedOwner)
                || !expectedOwner.Contains(original)) return SpiritTerminalLossResult.Rejected;
            var world = Find.World == null ? null : Find.WorldPawns;
            if (world == null || discard == null || discard.ReturnType != typeof(void)) return SpiritTerminalLossResult.Unavailable;
            if (world.Contains(original) || world.IsBeingDiscarded(original)) return SpiritTerminalLossResult.Rejected;
            try
            {
                // The native scope suppresses Pawn.Destroy's PassToWorld path
                // and removes itself in finally, including callback exceptions.
                discard.Invoke(world, new object[] { original, true });
                if (!original.Destroyed || !original.Discarded || expectedOwner.Contains(original)
                    || original.holdingOwner != null || world.Contains(original) || world.IsBeingDiscarded(original))
                    throw new InvalidOperationException("Native terminal discard did not complete its ownership cleanup.");
                return SpiritTerminalLossResult.Completed;
            }
            catch (Exception error)
            {
                Exception cause = error is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : error;
                Compatibility.TM_Log.Warning("SpiritTerminalLoss", "Native terminal disposal failed for " + original.ThingID
                    + "; Destroyed=" + original.Destroyed + "; Discarded=" + original.Discarded
                    + "; currentOwner=" + (original.holdingOwner?.Owner?.GetType().FullName ?? "none")
                    + ". Explicit caller recovery is required. " + cause);
                ExceptionDispatchInfo.Capture(cause).Throw();
                throw; // unreachable, keeps older compilers' flow analysis explicit
            }
        }
    }
}

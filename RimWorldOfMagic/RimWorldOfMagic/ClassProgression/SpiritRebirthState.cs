using System;
using Verse;

namespace TorannMagic.ClassProgression
{
    public enum SpiritRebirthForm { Ethereal, Possessed, Dormant }

    // Identity belongs to the original pawn. This record never owns another copy
    // of the pawn or its progression; the native possession container owns custody.
    public sealed class SpiritRebirthState : IExposable
    {
        public const int CurrentVersion = 1;
        private int version;
        private SpiritRebirthForm form = SpiritRebirthForm.Dormant;
        private Pawn original;
        private Pawn host;

        public int Version => version;
        public SpiritRebirthForm Form => form;
        public Pawn Host => host;

        public SpiritRebirthState() { } // Scribe only; an empty record grants nothing.
        internal SpiritRebirthState(Pawn original)
        {
            this.original = original;
            version = CurrentVersion;
            form = SpiritRebirthForm.Ethereal;
        }

        public static bool TryGet(Pawn pawn, out SpiritRebirthState state)
        {
            state = ClassIdentity.Component(pawn)?.SpiritRebirth;
            if (state != null && state.version == CurrentVersion && ReferenceEquals(state.original, pawn)
                && pawn != null && !pawn.Destroyed && Enum.IsDefined(typeof(SpiritRebirthForm), state.form)) return true;
            state = null;
            return false;
        }

        private static HediffComp_SpiritPossession Possession(Pawn candidate)
            => candidate?.health?.hediffSet?.GetFirstHediffOfDef(TorannMagicDefOf.TM_SpiritPossessionHD)
                ?.TryGetComp<HediffComp_SpiritPossession>();

        private static bool Holds(Pawn candidate, Pawn spirit)
        {
            var possession = Possession(candidate);
            var holder = possession?.GetDirectlyHeldThings();
            return candidate != null && candidate != spirit && !candidate.Dead && !candidate.Destroyed
                && spirit != null && !spirit.Dead && !spirit.Destroyed && !spirit.Spawned
                && ReferenceEquals(possession?.parent?.pawn, candidate)
                && ReferenceEquals(possession?.SpiritPawn, spirit) && holder?.Contains(spirit) == true
                && ReferenceEquals(holder.Owner, possession)
                && ReferenceEquals(spirit.holdingOwner, holder);
        }

        public bool HasValidHost(Pawn pawn)
            => TryGet(pawn, out var current) && ReferenceEquals(current, this)
                && form == SpiritRebirthForm.Possessed && Holds(host, pawn);

        public static bool TryGetPossessingSpirit(Pawn candidate, out Pawn spirit)
        {
            spirit = Possession(candidate)?.SpiritPawn;
            if (TryGet(spirit, out var state) && ReferenceEquals(state.host, candidate) && state.HasValidHost(spirit)) return true;
            spirit = null;
            return false;
        }

        // Called only after a future transaction has transferred native custody.
        internal bool TryEnterHost(Pawn pawn, Pawn candidate)
        {
            if (!TryGet(pawn, out var current) || !ReferenceEquals(current, this)
                || form != SpiritRebirthForm.Ethereal || host != null || !Holds(candidate, pawn)) return false;
            host = candidate;
            form = SpiritRebirthForm.Possessed;
            return true;
        }

        // Release must have placed the original pawn safely before changing form.
        internal bool TryLeaveHost(Pawn pawn, Pawn expectedHost)
        {
            if (!TryGet(pawn, out var current) || !ReferenceEquals(current, this)
                || form != SpiritRebirthForm.Possessed || host == null || !ReferenceEquals(host, expectedHost)
                || pawn.Dead || !pawn.Spawned || pawn.holdingOwner != null
                || Possession(expectedHost)?.GetDirectlyHeldThings()?.Contains(pawn) == true) return false;
            host = null;
            form = SpiritRebirthForm.Ethereal;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version", 0);
            Scribe_Values.Look(ref form, "form", SpiritRebirthForm.Dormant);
            Scribe_References.Look(ref original, "originalPawn");
            Scribe_References.Look(ref host, "hostPawn");
        }
    }
}

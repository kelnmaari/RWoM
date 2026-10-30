using System;
using Verse;

namespace TorannMagic.ClassProgression
{
    public enum SpiritRebirthForm { Ethereal, Possessed, Dormant, Stone }

    // Identity and progression remain on the original pawn; native holders own custody.
    public sealed class SpiritRebirthState : IExposable
    {
        public const int CurrentVersion = 2;
        public const int RecoveryDurationTicks = 420000;
        private int version;
        private SpiritRebirthForm form = SpiritRebirthForm.Dormant;
        private Pawn original;
        private Pawn host;
        private Thing_SoulStone stone;
        private bool recoveryRequired;
        private int recoveryTicks;
        private int lastRecoveryTick = -1;

        public int Version => version;
        public SpiritRebirthForm Form => form;
        public Pawn Host => host;
        public Thing_SoulStone Stone => stone;
        public bool RecoveryRequired => recoveryRequired;
        public int RecoveryTicks => recoveryTicks;

        public SpiritRebirthState() { }
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

        private static bool Holds(Pawn candidate, Pawn spirit, bool allowDead = false)
        {
            var possession = Possession(candidate);
            var holder = possession?.GetDirectlyHeldThings();
            return candidate != null && candidate != spirit && (allowDead || !candidate.Dead) && !candidate.Destroyed
                && spirit != null && !spirit.Dead && !spirit.Destroyed && !spirit.Spawned
                && ReferenceEquals(possession?.parent?.pawn, candidate)
                && ReferenceEquals(possession?.SpiritPawn, spirit) && holder?.Contains(spirit) == true
                && ReferenceEquals(holder.Owner, possession)
                && ReferenceEquals(spirit.holdingOwner, holder);
        }

        private bool IsCurrent(Pawn pawn) => TryGet(pawn, out var current) && ReferenceEquals(current, this);
        public bool HasValidHost(Pawn pawn) => IsCurrent(pawn) && form == SpiritRebirthForm.Possessed && Holds(host, pawn);
        public bool HasValidStone(Pawn pawn) => IsCurrent(pawn) && form == SpiritRebirthForm.Stone && StoneHolds(stone, pawn);
        private static bool StoneHolds(Thing_SoulStone candidate, Pawn pawn)
        {
            var holder = candidate?.GetDirectlyHeldThings();
            return candidate != null && !candidate.Destroyed && pawn != null && !pawn.Dead && !pawn.Destroyed && !pawn.Spawned
                && ReferenceEquals(candidate.ContainedSpirit, pawn) && holder?.Contains(pawn) == true
                && ReferenceEquals(holder.Owner, candidate) && ReferenceEquals(pawn.holdingOwner, holder);
        }
        public bool CanVoluntarilyLeaveHost(Pawn pawn)
            => HasValidHost(pawn) && (!recoveryRequired || recoveryTicks >= RecoveryDurationTicks);

        public static bool TryGetPossessingSpirit(Pawn candidate, out Pawn spirit)
        {
            spirit = Possession(candidate)?.SpiritPawn;
            if (TryGet(spirit, out var state) && ReferenceEquals(state.host, candidate) && state.HasValidHost(spirit)) return true;
            spirit = null;
            return false;
        }

        // Future host integration must call once each game tick. Unobserved gaps
        // (including off-map/offline intervals) never count as time in a living host.
        internal bool TickRecovery(Pawn pawn, Pawn expectedHost, int tick)
        {
            if (!HasValidHost(pawn) || !ReferenceEquals(host, expectedHost))
            {
                lastRecoveryTick = -1;
                return false;
            }
            if (!recoveryRequired || tick < 0 || tick <= lastRecoveryTick) return false;
            bool advance = lastRecoveryTick >= 0 && tick - lastRecoveryTick == 1 && recoveryTicks < RecoveryDurationTicks;
            lastRecoveryTick = tick;
            if (advance) recoveryTicks++;
            return advance;
        }

        // Forced collapse only: false means energy depletion, true means host death.
        // Callers must validate the depletion event; this is never voluntary exit.
        internal bool CanEnterStone(Pawn pawn, Pawn expectedHost, bool hostDied)
        {
            if (!IsCurrent(pawn) || pawn.Dead) return false;
            if (form == SpiritRebirthForm.Ethereal)
                return !hostDied && expectedHost == null && host == null && pawn.Spawned && pawn.holdingOwner == null;
            return form == SpiritRebirthForm.Possessed && ReferenceEquals(host, expectedHost) && host != null
                && host.Dead == hostDied && Holds(host, pawn, hostDied);
        }

        // Commit follows the holder's validated transfer. The original can only
        // be in one native container; saved references alone confer no custody.
        internal bool TryEnterStone(Pawn pawn, Thing_SoulStone destination, Pawn expectedHost, bool hostDied)
        {
            if (!IsCurrent(pawn) || !StoneHolds(destination, pawn)) return false;
            if (form == SpiritRebirthForm.Ethereal)
            {
                if (hostDied || expectedHost != null || host != null) return false;
            }
            else if (form != SpiritRebirthForm.Possessed || host == null || !ReferenceEquals(host, expectedHost)
                || host.Destroyed || host.Dead != hostDied
                || !ReferenceEquals(Possession(host)?.parent?.pawn, host)
                || Possession(host)?.GetDirectlyHeldThings()?.Contains(pawn) != false) return false;
            host = null;
            stone = destination;
            form = SpiritRebirthForm.Stone;
            recoveryRequired = true;
            recoveryTicks = 0;
            lastRecoveryTick = -1;
            return true;
        }

        internal bool TryEnterHost(Pawn pawn, Pawn candidate)
        {
            if (!IsCurrent(pawn) || host != null || !Holds(candidate, pawn)) return false;
            if (form == SpiritRebirthForm.Stone)
            {
                if (stone == null || stone.Destroyed || stone.GetDirectlyHeldThings().Count != 0) return false;
                recoveryRequired = true;
            }
            else if (form != SpiritRebirthForm.Ethereal || stone != null) return false;
            stone = null;
            host = candidate;
            form = SpiritRebirthForm.Possessed;
            lastRecoveryTick = -1;
            return true;
        }

        internal bool TryLeaveHost(Pawn pawn, Pawn expectedHost)
        {
            if (!IsCurrent(pawn) || form != SpiritRebirthForm.Possessed || host == null || !ReferenceEquals(host, expectedHost)
                || host.Dead || host.Destroyed || (recoveryRequired && recoveryTicks < RecoveryDurationTicks)
                || pawn.Dead || !pawn.Spawned || pawn.holdingOwner != null
                || Possession(expectedHost)?.GetDirectlyHeldThings()?.Contains(pawn) == true) return false;
            host = null;
            form = SpiritRebirthForm.Ethereal;
            lastRecoveryTick = -1;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version", 0);
            Scribe_Values.Look(ref form, "form", SpiritRebirthForm.Dormant);
            Scribe_References.Look(ref original, "originalPawn");
            Scribe_References.Look(ref host, "hostPawn");
            Scribe_References.Look(ref stone, "soulStone");
            Scribe_Values.Look(ref recoveryRequired, "recoveryRequired", false);
            Scribe_Values.Look(ref recoveryTicks, "recoveryTicks", 0);
            Scribe_Values.Look(ref lastRecoveryTick, "lastRecoveryTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && version == 1)
            {
                // Foundation saves had no Stone form or recovery debt.
                if (form == SpiritRebirthForm.Stone) form = SpiritRebirthForm.Dormant;
                stone = null;
                recoveryRequired = false;
                recoveryTicks = 0;
                lastRecoveryTick = -1;
                version = CurrentVersion;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && version == CurrentVersion)
            {
                // Repair malformed counters conservatively, without awarding time.
                // A valid ongoing recovery keeps both progress and tick baseline.
                if (recoveryTicks < 0 || recoveryTicks > RecoveryDurationTicks)
                {
                    recoveryRequired = true;
                    recoveryTicks = 0;
                    lastRecoveryTick = -1;
                }
                if (lastRecoveryTick < -1) lastRecoveryTick = -1;
                if (recoveryTicks > 0) recoveryRequired = true;
                if (form == SpiritRebirthForm.Stone)
                {
                    recoveryRequired = true;
                    recoveryTicks = 0;
                    lastRecoveryTick = -1;
                }
            }
        }
    }
}

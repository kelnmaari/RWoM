using System;
using System.Collections.Generic;
using Verse;

namespace TorannMagic.ClassProgression
{
    [Flags]
    public enum Vocation { None = 0, Mage = 1, Warrior = 2, WarriorMage = Mage | Warrior }
    public enum DisciplineDomain { Magic = 1, Might = 2 }
    public enum InitiationStage { Foundation = 1, Adept = 2, Master = 3, Ascended = 4 }

    // Owned by the pawn, independent of class traits and the magic/might skill owners.
    // Missing state means classic progression, including existing addon classes.
    public sealed class ClassProgressionState : IExposable
    {
        public const int CurrentVersion = 1;
        private int version = CurrentVersion;
        private Vocation vocation;
        private List<string> noviceSpells = new List<string>();
        public IReadOnlyList<string> NoviceSpells => noviceSpells.AsReadOnly();
        private List<DisciplineProgress> disciplines = new List<DisciplineProgress>();
        private int revision;
        public int Revision => revision;
        public int Version => version;
        public Vocation Vocation => vocation;
        public IReadOnlyList<DisciplineProgress> Disciplines => disciplines.AsReadOnly();

        public ClassProgressionState() { } // Scribe only: no implicit enrollment.
        public ClassProgressionState(Vocation vocation)
        {
            if (vocation == Vocation.None || (vocation & ~Vocation.WarriorMage) != 0)
                throw new ArgumentOutOfRangeException(nameof(vocation));
            this.vocation = vocation;
        }

        public DisciplineProgress Find(string classId)
            => disciplines.Find(p => p != null && string.Equals(p.ClassId, classId, StringComparison.Ordinal));

        // Storage operation for a future verified initiation transaction. It neither
        // consumes books nor grants abilities, traits, XP, points or hediffs.
        public bool TryAddFoundation(string classId, DisciplineDomain domain)
        {
            if (version != CurrentVersion || string.IsNullOrWhiteSpace(classId)
                || classId != classId.Trim() || Find(classId) != null) return false;
            Vocation required = domain == DisciplineDomain.Magic ? Vocation.Mage
                : domain == DisciplineDomain.Might ? Vocation.Warrior : Vocation.None;
            if (required == Vocation.None || (vocation & required) == 0) return false;
            disciplines.Add(new DisciplineProgress(classId, domain));
            revision++;
            return true;
        }

        public bool TryAdvanceAfterTrial(string classId, InitiationStage expected, string trialId)
        {
            if (version != CurrentVersion || string.IsNullOrWhiteSpace(trialId)) return false;
            var path = Find(classId);
            if (path == null || !path.TryAdvanceAfterTrial(expected, trialId)) return false;
            revision++;
            return true;
        }

        public bool TryChooseNoviceSpell(string abilityId)
        {
            if (version != CurrentVersion || (vocation & Vocation.Mage) == 0 || noviceSpells.Count >= 2
                || string.IsNullOrWhiteSpace(abilityId) || abilityId != abilityId.Trim() || noviceSpells.Contains(abilityId)) return false;
            noviceSpells.Add(abilityId);
            revision++;
            return true;
        }

        internal bool ActivateLichForm()
        {
            if (version != CurrentVersion || Find("rwom.necromancer")?.ActivateLichForm() != true) return false;
            revision++;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref noviceSpells, "noviceSpells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && noviceSpells == null) noviceSpells = new List<string>();
            Scribe_Values.Look(ref version, "version", CurrentVersion);
            Scribe_Values.Look(ref vocation, "vocation", Vocation.None);
            Scribe_Collections.Look(ref disciplines, "disciplines", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && disciplines == null)
                disciplines = new List<DisciplineProgress>();
            // Unknown profile IDs survive without a Def cross-reference or data loss.
            // Loading never initiates a class, completes a ritual or issues a reward.
        }
    }

    public sealed class DisciplineProgress : IExposable
    {
        private string classId;
        private DisciplineDomain domain;
        private InitiationStage stage = InitiationStage.Foundation;
        private List<string> completedTrials = new List<string>();
        private ServantControlProgress servantControl;
        private bool lichFormActive;
        private bool golemancerBrandsGranted;
        public bool GolemancerBrandsGranted => golemancerBrandsGranted;
        internal void MarkGolemancerBrandsGranted() { if (classId == "rwom.golemancer" && domain == DisciplineDomain.Magic && stage >= InitiationStage.Adept) golemancerBrandsGranted = true; }
        private float fireboltDamage;
        private SummonerTrialProgress summonerTrials;
        public SummonerTrialProgress SummonerTrials => summonerTrials ?? (summonerTrials = new SummonerTrialProgress());
        private ArcanistTrialProgress arcanistTrials;
        public ArcanistTrialProgress ArcanistTrials => arcanistTrials ?? (arcanistTrials = new ArcanistTrialProgress());
        private LightningTrialProgress lightningTrials;
        public LightningTrialProgress LightningTrials => lightningTrials ?? (lightningTrials = new LightningTrialProgress());
        private ShamanTrialProgress shamanTrials;
        public ShamanTrialProgress ShamanTrials => shamanTrials ?? (shamanTrials = new ShamanTrialProgress());
        private BrightmageTrialProgress brightmageTrials;
        public BrightmageTrialProgress BrightmageTrials => brightmageTrials ?? (brightmageTrials = new BrightmageTrialProgress());
        private BloodMageTrialProgress bloodMageTrials;
        public BloodMageTrialProgress BloodMageTrials => bloodMageTrials ?? (bloodMageTrials = new BloodMageTrialProgress());
        private IceTrialProgress iceTrials;
        public IceTrialProgress IceTrials => iceTrials ?? (iceTrials = new IceTrialProgress());
        private List<FireclawCastWitness> fireclawWitnesses = new List<FireclawCastWitness>();
        public IReadOnlyList<FireclawCastWitness> FireclawWitnesses => fireclawWitnesses.AsReadOnly();
        internal void RecordFireclawVictim(int castId, int victimId)
        {
            if (classId != "rwom.fire" || domain != DisciplineDomain.Magic || stage != InitiationStage.Adept
                || castId <= 0 || victimId <= 0 || completedTrials.Contains(FireclawTrial.TrialId)) return;
            var witness = fireclawWitnesses.Find(w => w.castId == castId);
            if (witness != null)
            {
                if (witness.firstVictimId == victimId) return;
                RecordCompletedTrial(FireclawTrial.TrialId);
                fireclawWitnesses.Clear();
                return;
            }
            if (fireclawWitnesses.Count >= FireclawTrial.WitnessLimit) fireclawWitnesses.RemoveAt(0);
            fireclawWitnesses.Add(new FireclawCastWitness { castId = castId, firstVictimId = victimId });
        }
        public float FireboltDamage => fireboltDamage;
        internal void RecordFireboltDamage(float amount)
        {
            if (classId != "rwom.fire" || stage != InitiationStage.Foundation || domain != DisciplineDomain.Magic
                || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0) return;
            fireboltDamage = System.Math.Min(100f, fireboltDamage + amount);
            if (fireboltDamage >= 100f) RecordCompletedTrial(FireboltDamageTrial.TrialId);
        }
        public bool LichFormActive => lichFormActive;
        internal bool ActivateLichForm()
        {
            if (lichFormActive || classId != "rwom.necromancer" || domain != DisciplineDomain.Magic
                || stage != InitiationStage.Ascended) return false;
            lichFormActive = true;
            return true;
        }
        public ServantControlProgress ServantControl => servantControl;
        public ServantControlProgress EnsureServantControl() => servantControl ?? (servantControl = new ServantControlProgress());
        public string ClassId => classId;
        public DisciplineDomain Domain => domain;
        public InitiationStage Stage => stage;
        public IReadOnlyList<string> CompletedTrials => completedTrials.AsReadOnly();
        public DisciplineProgress() { }
        internal DisciplineProgress(string classId, DisciplineDomain domain)
        { this.classId = classId; this.domain = domain; }

        // Completion is local to one direction: an Ice trial cannot finish Fire.
        // Repeated notifications are idempotent; rewards belong to ritual transactions.
        public bool RecordCompletedTrial(string trialId)
        {
            if (string.IsNullOrWhiteSpace(trialId) || trialId != trialId.Trim()
                || completedTrials.Contains(trialId)) return false;
            completedTrials.Add(trialId);
            return true;
        }

        internal bool TryAdvanceAfterTrial(InitiationStage expected, string trialId)
        {
            if (stage != expected || expected < InitiationStage.Foundation || expected >= InitiationStage.Ascended
                || !completedTrials.Contains(trialId)) return false;
            stage = (InitiationStage)((int)stage + 1);
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref classId, "classId");
            Scribe_Values.Look(ref domain, "domain");
            Scribe_Values.Look(ref stage, "stage", InitiationStage.Foundation);
            Scribe_Collections.Look(ref completedTrials, "completedTrials", LookMode.Value);
            Scribe_Deep.Look(ref servantControl, "servantControl");
            Scribe_Values.Look(ref lichFormActive, "lichFormActive", false);
            Scribe_Values.Look(ref golemancerBrandsGranted, "golemancerBrandsGranted", false);
            Scribe_Values.Look(ref fireboltDamage, "fireboltDamage", 0f);
            Scribe_Deep.Look(ref iceTrials, "iceTrials");
            Scribe_Deep.Look(ref lightningTrials, "lightningTrials");
            Scribe_Deep.Look(ref bloodMageTrials, "bloodMageTrials");
            Scribe_Deep.Look(ref brightmageTrials, "brightmageTrials");
            Scribe_Deep.Look(ref shamanTrials, "shamanTrials");
            Scribe_Deep.Look(ref arcanistTrials, "arcanistTrials");
            Scribe_Deep.Look(ref summonerTrials, "summonerTrials");
            Scribe_Collections.Look(ref fireclawWitnesses, "fireclawWitnesses", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && (float.IsNaN(fireboltDamage) || float.IsInfinity(fireboltDamage) || fireboltDamage < 0)) fireboltDamage = 0;
            if (Scribe.mode == LoadSaveMode.PostLoadInit) fireboltDamage = System.Math.Min(100f, fireboltDamage);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && completedTrials == null)
                completedTrials = new List<string>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (fireclawWitnesses == null) fireclawWitnesses = new List<FireclawCastWitness>();
                var seen = new HashSet<int>();
                fireclawWitnesses.RemoveAll(w => w == null || w.castId <= 0 || w.firstVictimId <= 0 || !seen.Add(w.castId));
                if (fireclawWitnesses.Count > FireclawTrial.WitnessLimit)
                    fireclawWitnesses.RemoveRange(0, fireclawWitnesses.Count - FireclawTrial.WitnessLimit);
                if (completedTrials.Contains(FireclawTrial.TrialId)) fireclawWitnesses.Clear();
            }
        }
    }
}

using System.Linq;
using RimWorld;
using TorannMagic.ClassProgression;
using UnityEngine;
using Verse;

namespace TorannMagic
{
    public partial class Window_TMGrimoire
    {
        private string InitiationRequirements()
        {
            if (!ClassIdentity.TryGetState(pawn, out var state)) return "TM_GrimoireClassicRequirements".Translate();
            string text = ClassIdentity.Stages(state);
            var profile = ClassIdentity.Profile("rwom.priest");
            if (state.Disciplines.Count == 0)
                foreach (var available in ClassInitiation.FoundationProfiles)
                    text += "\n" + "TM_ProgressionFoundationBook".Translate(ClassIdentity.DisciplineLabel(available.profileId), available.foundationBook.LabelCap);
            var path = state.Find("rwom.priest");
            if (path?.Stage == InitiationStage.Foundation && profile?.adeptBook != null)
            {
                text += "\n\n" + "TM_ProgressionPriestHealingTrial".Translate();
                text += "\n" + (path.CompletedTrials.Contains(profile.adeptTrialId) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                text += "\n" + "TM_ProgressionNextBook".Translate(profile.adeptBook.LabelCap);
            }
            if (path?.Stage == InitiationStage.Adept && profile?.masterBook != null)
            {
                text += "\n\n" + "TM_ProgressionPriestPurificationTrial".Translate();
                text += "\n" + (path.CompletedTrials.Contains(profile.masterTrialId) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                text += "\n" + "TM_ProgressionNextBook".Translate(profile.masterBook.LabelCap);
            }
            if (path?.Stage == InitiationStage.Master)
                text += "\n\n" + "TM_ProgressionAscensionRequirements".Translate();
            var necromancer = state.Find("rwom.necromancer");
            var necromancerProfile = ClassIdentity.Profile("rwom.necromancer");
            if (necromancer?.Stage == InitiationStage.Foundation && necromancerProfile?.adeptBook != null)
            {
                text += "\n\n" + "TM_ProgressionNecromancerControlTrial".Translate();
                text += "\n" + "TM_ProgressionServantTime".Translate(((necromancer.ServantControl?.ControlledTicks ?? 0) / 2500f).ToString("0.0"));
                text += "\n" + (necromancer.CompletedTrials.Contains(NecromancerControlTrial.TrialId)
                    ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                text += "\n" + "TM_ProgressionNextBook".Translate(necromancerProfile.adeptBook.LabelCap);
            }
            if (necromancer?.Stage == InitiationStage.Adept && necromancerProfile?.masterBook != null)
            {
                text += "\n\n" + "TM_ProgressionNecromancerDeathMarkTrial".Translate();
                text += "\n" + (necromancer.CompletedTrials.Contains(NecromancerDeathMarkTrial.TrialId)
                    ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                text += "\n" + "TM_ProgressionNextBook".Translate(necromancerProfile.masterBook.LabelCap);
            }
            if (necromancer?.Stage == InitiationStage.Master)
                text += "\n\n" + "TM_ProgressionNecromancerAscensionRequirements".Translate();
            var fire = state.Find("rwom.fire");
            var fireProfile = ClassIdentity.Profile("rwom.fire");
            if (fire?.Stage == InitiationStage.Foundation && fireProfile?.adeptBook != null)
            {
                text += "\n\n" + "TM_ProgressionFireboltTrial".Translate();
                text += "\n" + "TM_ProgressionFireboltDamage".Translate(fire.FireboltDamage.ToString("0.#"));
                text += "\n" + (fire.CompletedTrials.Contains(FireboltDamageTrial.TrialId) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                text += "\n" + "TM_ProgressionNextBook".Translate(fireProfile.adeptBook.LabelCap);
            }
            if (fire?.Stage == InitiationStage.Adept && fireProfile?.masterBook != null)
            {
                text += "\n\n" + "TM_ProgressionFireclawTrial".Translate();
                text += "\n" + (fire.CompletedTrials.Contains(FireclawTrial.TrialId) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                text += "\n" + "TM_ProgressionNextBook".Translate(fireProfile.masterBook.LabelCap);
            }
            if (fire?.Stage == InitiationStage.Master)
                text += "\n\n" + "TM_ProgressionFireAscensionRequirements".Translate();
            var ice = state.Find("rwom.ice");
            var iceProfile = ClassIdentity.Profile("rwom.ice");
            if (ice != null && (ice.Stage == InitiationStage.Foundation || ice.Stage == InitiationStage.Adept))
            {
                bool first = ice.Stage == InitiationStage.Foundation;
                string trial = first ? IceTrials.FoundationTrialId : IceTrials.MasteryTrialId;
                text += "\n\n" + (first ? "TM_ProgressionIceFoundationTrial" : "TM_ProgressionIceMasteryTrial").Translate();
                text += "\n" + "TM_ProgressionIceTrialProgress".Translate(ice.IceTrials.IceboltDamage.ToString("0.#"), ice.IceTrials.SnowballVictims.Count);
                text += "\n" + (ice.CompletedTrials.Contains(trial) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                var book = first ? iceProfile?.adeptBook : iceProfile?.masterBook;
                if (book != null) text += "\n" + "TM_ProgressionNextBook".Translate(book.LabelCap);
                text += "\n" + "TM_ProgressionIceAttunementHint".Translate();
            }
            if (ice?.Stage == InitiationStage.Master)
                text += "\n\n" + "TM_ProgressionIceAscensionRequirements".Translate();
            var lightning = state.Find("rwom.lightning");
            var lightningProfile = ClassIdentity.Profile("rwom.lightning");
            if (lightning != null && (lightning.Stage == InitiationStage.Foundation || lightning.Stage == InitiationStage.Adept))
            {
                bool first = lightning.Stage == InitiationStage.Foundation;
                string trial = first ? LightningTrials.FoundationTrialId : LightningTrials.MasteryTrialId;
                text += "\n\n" + (first ? "TM_ProgressionLightningFoundationTrial" : "TM_ProgressionLightningMasteryTrial").Translate();
                text += "\n" + "TM_ProgressionLightningTrialProgress".Translate(lightning.LightningTrials.BoltDamage.ToString("0.#"), lightning.LightningTrials.CloudVictims.Count);
                text += "\n" + (lightning.CompletedTrials.Contains(trial) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                var book = first ? lightningProfile?.adeptBook : lightningProfile?.masterBook;
                if (book != null) text += "\n" + "TM_ProgressionNextBook".Translate(book.LabelCap);
                text += "\n" + "TM_ProgressionLightningAttunementHint".Translate();
            }
            if (lightning?.Stage == InitiationStage.Master)
                text += "\n\n" + "TM_ProgressionLightningAscensionRequirements".Translate();
            var arcanist = state.Find("rwom.arcanist");
            var arcanistProfile = ClassIdentity.Profile("rwom.arcanist");
            if (arcanist != null && (arcanist.Stage == InitiationStage.Foundation || arcanist.Stage == InitiationStage.Adept))
            {
                bool first = arcanist.Stage == InitiationStage.Foundation;
                string trial = first ? ArcanistTrials.FoundationTrialId : ArcanistTrials.MasteryTrialId;
                text += "\n\n" + (first ? "TM_ProgressionArcanistFoundationTrial" : "TM_ProgressionArcanistMasteryTrial").Translate();
                if (first) text += "\n" + "TM_ProgressionArcanistTrialProgress".Translate(arcanist.ArcanistTrials.MissileDamage.ToString("0.#"));
                text += "\n" + (arcanist.CompletedTrials.Contains(trial) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                var book = first ? arcanistProfile?.adeptBook : arcanistProfile?.masterBook;
                if (book != null) text += "\n" + "TM_ProgressionNextBook".Translate(book.LabelCap);
                text += "\n" + "TM_ProgressionArcanistAttunementHint".Translate();
            }
            if (arcanist?.Stage == InitiationStage.Master)
                text += "\n\n" + "TM_ProgressionArcanistAscensionRequirements".Translate();
            foreach (var classId in new[] { "rwom.paladin", "rwom.summoner", "rwom.druid", "rwom.bard", "rwom.warlock", "rwom.succubus", "rwom.geomancer", "rwom.technomancer", "rwom.bloodmage", "rwom.enchanter", "rwom.chronomancer", "rwom.brightmage", "rwom.shaman", "rwom.golemancer", "rwom.empath" })
            {
                var discipline = state.Find(classId);
                var disciplineProfile = ClassIdentity.Profile(classId);
                string prefix = classId == "rwom.empath" ? "TM_ProgressionEmpath" : classId == "rwom.golemancer" ? "TM_ProgressionGolemancer" : classId == "rwom.shaman" ? "TM_ProgressionShaman" : classId == "rwom.brightmage" ? "TM_ProgressionBrightmage" : classId == "rwom.chronomancer" ? "TM_ProgressionChronomancer" : classId == "rwom.enchanter" ? "TM_ProgressionEnchanter" : classId == "rwom.bloodmage" ? "TM_ProgressionBloodMage" : classId == "rwom.technomancer" ? "TM_ProgressionTechnomancer" : classId == "rwom.geomancer" ? "TM_ProgressionGeomancer" : classId == "rwom.warlock" ? "TM_ProgressionWarlock" : classId == "rwom.succubus" ? "TM_ProgressionSuccubus" : classId == "rwom.druid" ? "TM_ProgressionDruid" : classId == "rwom.bard" ? "TM_ProgressionBard" : classId == "rwom.paladin" ? "TM_ProgressionPaladin" : "TM_ProgressionSummoner";
                if (discipline != null && (discipline.Stage == InitiationStage.Foundation || discipline.Stage == InitiationStage.Adept))
                {
                    bool first = discipline.Stage == InitiationStage.Foundation;
                    string trial = classId + (first ? ".foundation" : ".mastery");
                    text += "\n\n" + (prefix + (first ? "FoundationTrial" : "MasteryTrial")).Translate();
                    if (first && classId == "rwom.summoner")
                        text += "\n" + "TM_ProgressionSummonerTrialProgress".Translate(discipline.SummonerTrials.ControlledTicks);
                    if (first && classId == "rwom.brightmage")
                        text += "\n" + "TM_ProgressionBrightmageTrialProgress".Translate(discipline.BrightmageTrials.LanceDamage.ToString("0.#"));
                    if (first && classId == "rwom.shaman")
                        text += "\n" + "TM_ProgressionShamanTrialProgress".Translate(discipline.ShamanTrials.Healing.ToString("0.#"));
                    if (!first && classId == "rwom.bloodmage")
                        text += "\n" + "TM_ProgressionBloodMageTrialProgress".Translate(discipline.BloodMageTrials.IgniteDamage.ToString("0.#"));
                    text += "\n" + (discipline.CompletedTrials.Contains(trial) ? "TM_ProgressionTrialDone" : "TM_ProgressionTrialPending").Translate();
                    var book = first ? disciplineProfile?.adeptBook : disciplineProfile?.masterBook;
                    if (book != null) text += "\n" + "TM_ProgressionNextBook".Translate(book.LabelCap);
                    text += "\n" + (prefix + "AttunementHint").Translate();
                }
                if (discipline?.Stage == InitiationStage.Master)
                    text += "\n\n" + (prefix + "AscensionRequirements").Translate();
            }
            return text;
        }

        private float InitiationControlsHeight()
        {
            if (!ClassIdentity.TryGetState(pawn, out var state) || (state.Vocation & Vocation.Mage) == 0) return 0f;
            return 68f + DemonBookChoices().Count() * 36f + (state.NoviceSpells.Count < 2 ? NoviceLearning.Options.Count() * 36f : 0f)
                + (ClassTomeQuestOffer.Candidate(pawn) != null || ClassTomeQuestOffer.Existing(pawn) != null ? 40f : 0f);
        }

        private System.Collections.Generic.IEnumerable<System.Tuple<Thing, ClassDisciplineDef>> DemonBookChoices()
        {
            if (pawn.Map == null) yield break;
            foreach (var book in pawn.Map.listerThings.AllThings.Where(t => t.def?.defName == "Torn_BookOfDemons" && !t.IsForbidden(pawn))
                .GroupBy(t => t.def).Select(g => g.First()))
                foreach (var profile in ClassInitiation.FoundationCandidates(pawn, book))
                    if (profile.profileId == "rwom.warlock" || profile.profileId == "rwom.succubus")
                        yield return System.Tuple.Create(book, profile);
        }

        private void DrawInitiationControls(float width, float y)
        {
            if (!ClassIdentity.TryGetState(pawn, out var state) || (state.Vocation & Vocation.Mage) == 0) return;
            Text.Font = GameFont.Small;
            foreach (var choice in DemonBookChoices())
            {
                if (Widgets.ButtonText(new Rect(0f, y, width, 32f), ClassInitiationMenu.Label(choice.Item2, choice.Item1))
                    && ClassInitiationMenu.Begin(pawn, choice.Item1, choice.Item2.profileId)) Close();
                y += 36f;
            }
            var request = ClassTomeQuestOffer.Existing(pawn);
            if (ClassTomeQuestOffer.Candidate(pawn) != null || request != null)
            {
                var rect = new Rect(0f, y, width, 32f);
                string reason = request == null ? ClassTomeQuestOffer.UnavailableReason(pawn) : null;
                TooltipHandler.TipRegion(rect, (reason ?? "TM_TomeQuestRequestDesc").Translate());
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && reason == null;
                try
                {
                    if (Widgets.ButtonText(rect, (request == null ? "TM_TomeQuestRequest" : "TM_TomeQuestView").Translate()))
                    {
                        var quest = request?.quest ?? ClassTomeQuestOffer.Request(pawn);
                        if (quest != null) { ClassTomeQuestOffer.Open(quest); Close(); }
                        else Messages.Message("TM_TomeQuestUnavailable".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                }
                finally { GUI.enabled = previousEnabled; }
                y += 40f;
            }
            Widgets.Label(new Rect(0f, y, width, 48f), "TM_ProgressionNoviceChoices".Translate(state.NoviceSpells.Count, 2));
            y += 52f;
            if (state.NoviceSpells.Count >= 2) return;
            foreach (var ability in NoviceLearning.Options)
            {
                if (state.NoviceSpells.Contains(ability.defName) || NoviceLearning.AlreadyKnown(pawn, ability)) continue;
                TooltipHandler.TipRegion(new Rect(0f, y, width, 32f), ability.description);
                if (Widgets.ButtonText(new Rect(0f, y, width, 32f), ability.LabelCap)
                    && NoviceLearning.TryLearn(pawn, ability.defName)) filtersBuilt = false;
                y += 36f;
            }
        }
    }
}

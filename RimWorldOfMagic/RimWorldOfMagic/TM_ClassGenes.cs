using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace TorannMagic
{
    /// <summary>
    /// Biotech gene mirrors of the class gift traits: a classed pawn carries the
    /// active gene (mana / stamina), a merely gifted pawn carries the latent gene
    /// (hidden mage / sleeping warrior), which the class-gain paths consume.
    /// Genes are endogenes so vanilla reproduction can pass them to children;
    /// the inheritance chances are re-rolled per family in ApplyInheritance.
    /// Everything here is inert without Biotech (no gene defs are loaded).
    /// </summary>
    public static class TM_ClassGenes
    {
        private static GeneDef manaGene;
        private static GeneDef staminaGene;
        private static GeneDef hiddenMageGene;
        private static GeneDef sleepingWarriorGene;
        private static bool geneLookupDone;

        // Enchantment bloodline genes: markers paired 1:1 with their background
        // trait (Arcane Conduit, Mana Well, ...). The trait carries all effects;
        // the gene makes the gift inheritable and is granted with the trait both ways.
        private static GeneDef arcaneConduitGene;
        private static GeneDef manaWellGene;
        private static GeneDef boundlessGene;
        private static GeneDef faeBloodGene;
        private static GeneDef giantsBloodGene;
        private static GeneDef enlightenedGene;
        private static GeneDef[] mageEnchantGenes;
        private static GeneDef[] mightEnchantGenes;
        private static GeneDef[] allEnchantGenes;

        private static bool GenesLoaded()
        {
            if (!ModsConfig.BiotechActive)
            {
                return false;
            }
            if (!geneLookupDone)
            {
                geneLookupDone = true;
                manaGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_ManaGene");
                staminaGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_StaminaGene");
                hiddenMageGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_HiddenMageGene");
                sleepingWarriorGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_SleepingWarriorGene");
                arcaneConduitGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_ArcaneConduitGene");
                manaWellGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_ManaWellGene");
                boundlessGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_BoundlessGene");
                faeBloodGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_FaeBloodGene");
                giantsBloodGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_GiantsBloodGene");
                enlightenedGene = DefDatabase<GeneDef>.GetNamedSilentFail("TM_EnlightenedGene");
                mageEnchantGenes = new[] { arcaneConduitGene, manaWellGene, boundlessGene, faeBloodGene, enlightenedGene };
                mightEnchantGenes = new[] { giantsBloodGene };
                allEnchantGenes = mageEnchantGenes.Concat(mightEnchantGenes).ToArray();
            }
            return manaGene != null && staminaGene != null && hiddenMageGene != null && sleepingWarriorGene != null
                && allEnchantGenes.All((GeneDef g) => g != null);
        }

        /// <summary>
        /// Idempotent: derives the desired gene set from the pawn's class and gift
        /// traits and adds/removes genes to match. Safe to call from any class
        /// change point and repeatedly (trait gain, comp init, game load).
        /// </summary>
        public static void SyncClassGenes(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null || pawn.story == null || pawn.story.traits == null || !GenesLoaded())
            {
                return;
            }
            bool wanderer = pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Wanderer) || pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Wayfarer);
            bool magicClass = wanderer || TM_Calc.IsMagicUser(pawn);
            bool mightClass = wanderer || TM_Calc.IsMightUser(pawn);
            bool magicGift = pawn.story.traits.HasTrait(TorannMagicDefOf.TM_Gifted);
            bool mightGift = pawn.story.traits.HasTrait(TorannMagicDefOf.PhysicalProdigy);
            ApplyGene(pawn, manaGene, magicClass);
            ApplyGene(pawn, staminaGene, mightClass);
            ApplyGene(pawn, hiddenMageGene, magicGift && !magicClass);
            ApplyGene(pawn, sleepingWarriorGene, mightGift && !mightClass);
            SyncEnchantGenes(pawn);
        }

        /// <summary>
        /// Keeps each enchantment gene paired with its background trait in both
        /// directions: a trait carrier gains the gene (making the gift inheritable),
        /// a gene carrier gains the trait. The trait always supplies the actual
        /// bonuses, so the paired gene never doubles them.
        /// </summary>
        private static void SyncEnchantGenes(Pawn pawn)
        {
            TryPairEnchant(pawn, TorannMagicDefOf.TM_ArcaneConduitTD, arcaneConduitGene);
            TryPairEnchant(pawn, TorannMagicDefOf.TM_ManaWellTD, manaWellGene);
            TryPairEnchant(pawn, TorannMagicDefOf.TM_BoundlessTD, boundlessGene);
            TryPairEnchant(pawn, TorannMagicDefOf.TM_FaeBloodTD, faeBloodGene);
            TryPairEnchant(pawn, TorannMagicDefOf.TM_GiantsBloodTD, giantsBloodGene);
            TryPairEnchant(pawn, TorannMagicDefOf.TM_EnlightenedTD, enlightenedGene);
        }

        private static void TryPairEnchant(Pawn pawn, TraitDef traitDef, GeneDef geneDef)
        {
            if (traitDef == null || geneDef == null)
            {
                return;
            }
            bool hasTrait = pawn.story.traits.HasTrait(traitDef);
            Gene gene = pawn.genes.GetGene(geneDef);
            if (hasTrait && gene == null)
            {
                pawn.genes.AddGene(geneDef, xenogene: false);
            }
            else if (!hasTrait && gene != null)
            {
                pawn.story.traits.GainTrait(new Trait(traitDef, 0, false));
            }
        }

        private static void ApplyGene(Pawn pawn, GeneDef geneDef, bool wanted)
        {
            Gene gene = pawn.genes.GetGene(geneDef);
            if (wanted && gene == null)
            {
                pawn.genes.AddGene(geneDef, xenogene: false);
            }
            else if (!wanted && gene != null)
            {
                pawn.genes.RemoveGene(gene);
            }
        }

        private static int FamilyParentCount(Pawn father, Pawn mother, GeneDef latent, GeneDef active)
        {
            int count = 0;
            if (father?.genes != null && (father.genes.HasActiveGene(latent) || father.genes.HasActiveGene(active)))
            {
                count++;
            }
            if (mother?.genes != null && (mother.genes.HasActiveGene(latent) || mother.genes.HasActiveGene(active)))
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// The two mage genes (hidden mage, mana) count as one inheritable family,
        /// and so do the two fighter genes. One carrier parent gives the child the
        /// latent gene with 10% chance, two carrier parents with 50% chance. The
        /// latent gene is always the one passed on: the active genes only appear
        /// together with a class. Vanilla rolls are discarded for these four defs.
        /// </summary>
        public static void ApplyInheritance(Pawn father, Pawn mother, ref List<GeneDef> childGenes)
        {
            if (childGenes == null || !GenesLoaded())
            {
                return;
            }
            childGenes.RemoveAll((GeneDef g) => g == manaGene || g == staminaGene || g == hiddenMageGene || g == sleepingWarriorGene);
            int mageParents = FamilyParentCount(father, mother, hiddenMageGene, manaGene);
            int mightParents = FamilyParentCount(father, mother, sleepingWarriorGene, staminaGene);
            if (mageParents > 0 && Rand.Chance(mageParents > 1 ? 0.5f : 0.1f))
            {
                childGenes.Add(hiddenMageGene);
            }
            if (mightParents > 0 && Rand.Chance(mightParents > 1 ? 0.5f : 0.1f))
            {
                childGenes.Add(sleepingWarriorGene);
            }
            // enchantment genes themselves are plain inheritable endogenes and
            // pass on through the vanilla rolls above untouched
        }

        /// <summary>
        /// Birth gift: a child born with a dormant gift may carry one of its
        /// bloodline enchantment genes (20%). The rolled gene is a non-heritable
        /// xenogene, and it is skipped entirely when the child already inherited
        /// an enchantment gene of the same family — the inheritable one stays.
        /// Called after the newborn exists (its inherited genes are on it).
        /// </summary>
        public static void RollBirthEnchantGenes(Pawn baby)
        {
            if (baby == null || baby.genes == null || !GenesLoaded())
            {
                return;
            }
            if (baby.genes.HasActiveGene(hiddenMageGene)
                && !mageEnchantGenes.Any((GeneDef g) => baby.genes.GetGene(g) != null)
                && Rand.Chance(0.2f))
            {
                baby.genes.AddGene(mageEnchantGenes.RandomElement(), xenogene: true);
            }
            if (baby.genes.HasActiveGene(sleepingWarriorGene)
                && baby.genes.GetGene(giantsBloodGene) == null
                && Rand.Chance(0.2f))
            {
                baby.genes.AddGene(giantsBloodGene, xenogene: true);
            }
        }
    }
}

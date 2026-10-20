using System;
using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using Verse;
using RimWorld;

namespace TorannMagic.TMDefs
{
    /// <summary>
    /// Autocast defaults for every learned power that LoadLegacyClassAutocast does
    /// not already cover. With a TM_Autocast assigned, the per-pawn autocast toggle
    /// works for all spells (player side) and combat spells become available to AI
    /// casters (AIUsable). Utility spells are intentionally AIUsable=false: they are
    /// defined so players can toggle them, but NPCs never auto-cast map travel,
    /// weather control, transforms or base-building powers.
    /// </summary>
    public static class TM_AutocastDefaults
    {
        private static readonly Dictionary<string, TM_Autocast> cache = new Dictionary<string, TM_Autocast>(512);

        // Heal/regen/cure: cast on nearby injured allies (and self), drafted or not.
        private static readonly HashSet<string> HealAllies = new HashSet<string>
        {
            "TM_AdvancedHeal", "TM_Heal", "TM_HealingCircle", "TM_Regenerate", "TM_SpellMending",
            "TM_CureDisease", "TM_RegrowLimb", "TM_RayofHope", "TM_P_RayofHope", "TM_Purify",
            "TM_FirstAid", "TM_Herbalist", "TM_SoothingBalm", "TM_InnerHealing", "TM_Elixir"
        };

        // Combat self-buffs: enable whenever combat starts.
        private static readonly HashSet<string> SelfBuff = new HashSet<string>
        {
            "TM_Shield", "TM_ManaShield", "TM_Blur", "TM_HeatShield", "TM_Stoneskin", "TM_Overdrive",
            "TM_BlankMind", "TM_BattleHymn", "TM_BloodShield", "TM_BloodGift", "TM_EnchantedAura",
            "TM_EnchantedBody", "TM_Invisibility", "TM_Refraction", "TM_TechnoShield", "TM_TechnoWeapon",
            "TM_TechnoBit", "TM_ChargeBattery", "TM_NanoStimulant", "TM_ChaosTradition", "TM_AMP",
            "TM_Prediction", "TM_BardTraining", "TM_BowTraining", "TM_BladeArt", "TM_BladeFocus",
            "TM_FightersFocus", "TM_Fortitude", "TM_ThickSkin", "TM_CQC", "TM_BurningFury",
            "TM_CommanderAura", "TM_HoldTheLine", "TM_StayAlert", "TM_Sprint", "TM_MindOverBody",
            "TM_PsionicAugmentation", "TM_Shroud", "TM_SniperFocus", "TM_Legion", "TM_TigerStrike",
            "TM_VeilOfShadows", "TM_SpiritOfLight", "TM_TimeMark", "TM_Totems", "TM_Whirlwind",
            "TM_SeismicSlash", "TM_TempestStrike", "TM_SigilSurge", "TM_ValiantCharge", "TM_PhaseStrike",
            "TM_PsionicDash", "TM_Sprint"
        };

        // Conjurations: summon the fight to help, then engage.
        private static readonly HashSet<string> SummonAlly = new HashSet<string>
        {
            "TM_Summon", "TM_SummonDemon", "TM_SummonElemental", "TM_SummonExplosive", "TM_SummonMinion",
            "TM_SummonPoppi", "TM_SummonPylon", "TM_SpiritWolves", "TM_Golemancy", "TM_Sentinel",
            "TM_TechnoTurret", "TM_EarthSprites", "TM_LivingWall", "TM_GuardianSpirit"
        };

        // Splash damage: worth it when several enemies cluster up.
        private static readonly HashSet<string> DamageAoE = new HashSet<string>
        {
            "TM_LightningStorm", "TM_Sunfire", "TM_LightBurst", "TM_SmokeCloud", "TM_SpiritStorm",
            "TM_PsionicStorm", "TM_PsionicBlast", "TM_ArrowStorm", "TM_WaveOfFear", "TM_ConsumeCorpse",
            "TM_CorpseExplosion"
        };

        // Map travel, weather, transforms, base powers: player toggles only, never AI.
        private static readonly HashSet<string> UtilityNoAI = new HashSet<string>
        {
            "TM_Flight", "TM_Teleport", "TM_Blink", "TM_Recall", "TM_LightSkip", "TM_LightSkipMass",
            "TM_LightSkipGlobal", "TM_Polymorph", "TM_Shapeshift", "TM_ShapeshiftDW", "TM_LichForm",
            "TM_Meteor", "TM_OrbitalStrike", "TM_Rainmaker", "TM_FertileLands", "TM_DryGround",
            "TM_WetGround", "TM_Extinguish", "TM_MageLight", "TM_Sunlight", "TM_Heater", "TM_Cooler",
            "TM_PowerNode", "TM_LightningTrap", "TM_PoisonTrap", "TM_RuneCarving", "TM_EnchantWeapon",
            "TM_EnchanterStone", "TM_Resurrection", "TM_Transmutate", "TM_ShadowWalk", "TM_Entertain",
            "TM_TeachMagic", "TM_TeachMight", "TM_WandererCraft", "TM_WayfarerCraft", "TM_Soothe",
            "TM_SootheAnimal", "TM_TransferMana", "TM_ChronostaticField", "TM_Mimic", "TM_Possess",
            "TM_Disguise", "TM_Transpose", "TM_MoveOut", "TM_StrongBack", "TM_FieldTraining",
            "TM_RangerTraining", "TM_ShadowSlayer", "TM_ProvisionerAura", "TM_TaskMasterAura",
            "TM_Meditate", "TM_BriarPatch", "TM_FoldReality", "TM_AnimalFriend", "TM_BestowMight",
            "TM_Inspire", "TM_Dominate"
        };

        public static TM_Autocast For(TMAbilityDef def, bool mightUser)
        {
            if (def == null)
            {
                return null;
            }
            string baseName = BaseName(def.defName);
            string key = baseName + (mightUser ? "|M" : "|G");
            TM_Autocast ac;
            if (cache.TryGetValue(key, out ac))
            {
                return ac;
            }
            ac = Build(def, baseName, mightUser);
            cache.Add(key, ac);
            return ac;
        }

        // Powers are classified by their base def: level variants share the tree.
        private static string BaseName(string defName)
        {
            if (defName.EndsWith("_III")) return defName.Substring(0, defName.Length - 4);
            if (defName.EndsWith("_II")) return defName.Substring(0, defName.Length - 3);
            if (defName.EndsWith("_I")) return defName.Substring(0, defName.Length - 2);
            return defName;
        }

        private static TM_Autocast Build(TMAbilityDef def, string baseName, bool mightUser)
        {
            TM_Autocast ac = new TM_Autocast
            {
                mightUser = mightUser,
                magicUser = !mightUser,
                targetType = "Pawn",
                includeSelf = false
            };

            if (HealAllies.Contains(baseName))
            {
                ac.type = AutocastType.OnNearby;
                ac.drafted = true;
                ac.undrafted = true;
                ac.requiresLoS = true;
                ac.AIUsable = true;
                ac.includeSelf = true;
                ac.targetFriendly = true;
                ac.targetNeutral = false;
                ac.targetEnemy = false;
                ac.targetNoFaction = false;
                ac.minRange = 0f;
                ac.maxRange = 30f;
                ac.advancedConditionDefs = new List<string> { "TM_BelowMaxHealth" };
            }
            else if (SummonAlly.Contains(baseName))
            {
                ac.type = AutocastType.OnSelf;
                ac.drafted = true;
                ac.undrafted = false;
                ac.AIUsable = true;
            }
            else if (SelfBuff.Contains(baseName))
            {
                ac.type = AutocastType.OnSelf;
                ac.drafted = true;
                ac.undrafted = false;
                ac.AIUsable = true;
            }
            else if (DamageAoE.Contains(baseName))
            {
                ac.type = AutocastType.OnNearby;
                ac.drafted = true;
                ac.undrafted = false;
                ac.requiresLoS = true;
                ac.AIUsable = true;
                ac.targetEnemy = true;
                ac.targetNeutral = false;
                ac.targetFriendly = false;
                ac.targetNoFaction = false;
                ac.minRange = 4f;
                ac.maxRange = 40f;
                ac.advancedConditionDefs = new List<string> { "TM_3EnemiesWithin6Cells" };
            }
            else if (UtilityNoAI.Contains(baseName))
            {
                ac.type = AutocastType.OnSelf;
                ac.drafted = true;
                ac.undrafted = true;
                ac.AIUsable = false;
            }
            else
            {
                // isViolent lives on AbilityUser.VerbProperties_Ability, not the base
                VerbProperties_Ability verb = def.MainVerb as VerbProperties_Ability;
                if (verb != null && verb.isViolent)
                {
                    // Uncatalogued combat spell: single-target enemy offense.
                    ac.type = AutocastType.OnTarget;
                    ac.drafted = true;
                    ac.undrafted = false;
                    ac.requiresLoS = true;
                    ac.AIUsable = true;
                    ac.targetEnemy = true;
                    ac.targetNeutral = false;
                    ac.targetFriendly = false;
                    ac.targetNoFaction = false;
                }
                else
                {
                    // Uncatalogued non-violent power: player-only self cast.
                    ac.type = AutocastType.OnSelf;
                    ac.drafted = true;
                    ac.undrafted = true;
                    ac.AIUsable = false;
                }
            }
            return ac;
        }
    }
}

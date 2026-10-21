using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;

namespace TorannMagic
{
    /// <summary>
    /// Inspect-pane tab variant of the Grimoire button for might class holders —
    /// the fighter side of the "grimoireOnAbilityBar = false" setting; opens the
    /// shared Grimoire window on click.
    /// </summary>
    public class ITab_Pawn_Might : ITab
    {
        public override bool IsVisible
        {
            get
            {
                if (ModOptions.Settings.Instance.grimoireOnAbilityBar)
                {
                    return false;
                }
                Pawn pawn = base.SelPawn;
                if (pawn == null || !pawn.IsColonist)
                {
                    return false;
                }
                CompAbilityUserMight comp = pawn.GetCompAbilityUserMight();
                return comp != null && comp.IsMightUser;
            }
        }

        public ITab_Pawn_Might()
        {
            this.size = new Vector2(360f, 130f);
            this.labelKey = "TM_Grimoire";
        }

        public override void OnOpen()
        {
            base.OnOpen();
            OpenGrimoire();
        }

        protected override void FillTab()
        {
            Rect buttonRect = new Rect(12f, 12f, this.size.x - 24f, 32f);
            if (Widgets.ButtonText(buttonRect, "TM_GrimoireOpen".Translate()))
            {
                OpenGrimoire();
            }
            Rect hintRect = new Rect(16f, 52f, this.size.x - 32f, this.size.y - 64f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(hintRect, "TM_GrimoireTabHint".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void OpenGrimoire()
        {
            Pawn pawn = base.SelPawn;
            if (pawn == null)
            {
                return;
            }
            Window_TMGrimoire open = Find.WindowStack.Windows.OfType<Window_TMGrimoire>().FirstOrDefault();
            if (open != null)
            {
                open.Close();
            }
            Find.WindowStack.Add(new Window_TMGrimoire(pawn));
        }
    }
}

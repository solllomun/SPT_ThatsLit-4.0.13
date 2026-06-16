using System.Reflection;
using UnityEngine;
using EFT;
using System.Linq;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;

namespace ThatsLit.Patches.Vision
{
    public class BlindFirePatch : ModulePatch
    {
        // The aiming type and its BotOwner-holding field are obfuscated / auto-named (was "botOwner_0")
        // and drift between EFT builds, which is what broke the old "___botOwner_0" Harmony field injection.
        // Resolve both structurally: the unique EFT type exposing the aiming properties, and its single
        // BotOwner-typed field. Cached at patch setup and read by name-independent reflection in the postfix.
        private static FieldInfo _botOwnerField;

        protected override MethodBase GetTargetMethod()
        {
            var aimingType = PatchConstants.EftTypes.First(t => t.GetProperty("LastSpreadCount") != null
                                                             && t.GetProperty("LastAimTime")     != null
                                                             && t.GetProperty("HardAim")         != null);
            _botOwnerField = AccessTools.GetDeclaredFields(aimingType).Single(f => f.FieldType == typeof(BotOwner));
            return aimingType.GetMethod("get_EndTargetPoint");
        }

        [PatchPostfix]
        public static void Postfix (object __instance, ref Vector3 __result)
        {
            if (!ThatsLitPlugin.ForceBlindFireScatter.Value) return;
            ThatsLitPlugin.swBlindFireScatter.MaybeResume();
            var botOwner = (BotOwner)_botOwnerField.GetValue(__instance);
            if (botOwner.GetPlayer == null
             ||(botOwner.GetPlayer.HandsController as Player.FirearmController)?.Blindfire != true)
            {
                ThatsLitPlugin.swBlindFireScatter.Stop();
                return;
            }
            float dis = Vector3.Distance(__result, botOwner.GetPlayer.Position);

            __result += UnityEngine.Random.insideUnitSphere * 5 * Mathf.InverseLerp(15f, 200f, dis);
            ThatsLitPlugin.swBlindFireScatter.Stop();
        }
    }
}
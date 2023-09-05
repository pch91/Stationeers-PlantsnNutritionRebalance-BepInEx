﻿using Assets.Scripts.Objects.Entities;
using Assets.Scripts.UI;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Serialization;
using Assets.Scripts.Objects;
using Assets.Scripts.Atmospherics;
using System.Reflection;
using System;
using System.Collections.Generic;
using Object = System.Object;

namespace PlantsnNutritionRebalance.Scripts
{
    // Make plants transpirate some of the water they drink:
    [HarmonyPatch(typeof(Plant))]
    public class PlantPatch
    {
        [HarmonyPatch("TakePlantDrink")]
        [UsedImplicitly]
        [HarmonyPostfix]
        public static void TakePlantDrinkPatch(Plant __instance, ref float __result)
        {
            if (ConfigFile.PlantWaterTranspirationPercentage == 0)
                return;
            else
            {
                GasMixture gasMixture = GasMixtureHelper.Create();
                gasMixture.Add(new Mole(Chemistry.GasType.Water, (__result / 100) * ConfigFile.PlantWaterTranspirationPercentage, 0f));
                gasMixture.AddEnergy(__instance.ParentTray.WaterAtmosphere.Temperature * gasMixture.HeatCapacity);
                __instance.BreathingAtmosphere.Add(gasMixture);
            }
        }
    }

    // Patch the atmosphere fog:
    [HarmonyPatch(typeof(AtmosphericFog))]
    public class AtmosphericFogPatch
    {
        [HarmonyPatch("get_IsValid")]
        [UsedImplicitly]
        [HarmonyPrefix]
        public static bool PatchAtmosphericFog(AtmosphericFog __instance, ref bool __result)
        {
            if (ConfigFile.AtmosphereFogThreshold == 0f)
                return true; //user don't want to change the Fog Threshold, so keep the Vanilla method
            else
            {
                // Get the private Atmosphere property using reflection
                var atmosphereProp = typeof(AtmosphericFog).GetProperty("Atmosphere", BindingFlags.NonPublic | BindingFlags.Instance);
                // Get the value of the Atmosphere property for this instance of AtmosphericFog
                var atmosphere = (Atmosphere)atmosphereProp.GetValue(__instance);
                // Change the AtmosphereFog Moles threshold:
                __result = atmosphere != null && atmosphere.GasMixture.TotalMolesLiquids > ConfigFile.AtmosphereFogThreshold && atmosphere.Mode == AtmosphereHelper.AtmosphereMode.World;
                return false; // then skip the vanilla method
            }
        }
    }

    // Adjusts the water consumption of plants:
    [HarmonyPatch(typeof(PlantLifeRequirements))]
    public class PlantLifeRequirementsPatch
    {
        [HarmonyPatch("get_WaterPerTick")]
        [UsedImplicitly]
        [HarmonyPostfix]
        public static void WaterPerTickPatch(ref float __result)
        {
            __result *= ConfigFile.PlantWaterConsumptionMultiplier;
            if (__result > ConfigFile.PlantWaterConsumptionLimit)
                __result = ConfigFile.PlantWaterConsumptionLimit;
        }
    }

    [HarmonyPatch(typeof(Human))]
    public static class HumanPatches
    {
        // Adjusts the Human hydration based on the world difficulty and the warning/critical alerts
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        [UsedImplicitly]
        static public void HydrationAndWarningsPatch(Human __instance, ref float ___MaxHydrationStorage, ref float ____hydrationLossPerTick)
        {
            ___MaxHydrationStorage = ConfigFile.MaxHydrationStorage;
            switch (WorldManager.CurrentWorldSetting.DifficultySetting.HydrationRate)
            {
                case 1.5f: //stationeers difficulty, full water will last 100 game minutes (2 and a half days)
                    ____hydrationLossPerTick = 0.0019446f;
                    break;
                case 1f: //normal difficulty, full water should last ~160 game minutes (4 days in sun/orbit 2)
                    ____hydrationLossPerTick = 0.001798755f;
                    break;
                case 0.5f: //easy difficulty, full water should last ~220 game minutes
                    ____hydrationLossPerTick = 0.0017258325f;
                    break;
                case 0f: //user disabled water consumption directly on world config
                    ____hydrationLossPerTick = 0f;
                    break;
                default: // if it's none of the above, will try to calculate hydrationLossPerTick based on DifficultySetting.HydrationRate:
                    float a = Mathf.InverseLerp(0.0001f, 3f, WorldManager.CurrentWorldSetting.DifficultySetting.HydrationRate);
                    ____hydrationLossPerTick = Mathf.Lerp(0.001507065f, 0.002382135f, a);
                    break;
            }
            // Nutrition/hydration warnings:
            __instance.WarningNutrition = (ConfigFile.MaxNutritionStorage / 100) * ConfigFile.WarningNutrition;
            __instance.CriticalNutrition = (ConfigFile.MaxNutritionStorage / 100) * ConfigFile.CriticalNutrition;
            __instance.WarningHydration = (ConfigFile.MaxHydrationStorage / 100) * ConfigFile.WarningHydration;
            __instance.CriticalHydration = (ConfigFile.MaxHydrationStorage / 100) * ConfigFile.CriticalHydration;
        }


        [HarmonyPatch("get_MaxNutritionStorage")]
        [HarmonyPostfix]
        [UsedImplicitly]
        static public void MaxNutritionPatch(ref float __result)
        {
            // Adjusts the max food of the character:
            __result = ConfigFile.MaxNutritionStorage;
        }

        // Adjusts the HungerRate based on the world difficulty and changes the damage system for Starvation
        static float LastNutritionLossPerTick = 0.083333f;
        [HarmonyPatch("LifeNutrition")]
        [HarmonyPrefix]
        [UsedImplicitly]
        public static bool MetabolismRatePatch(Human __instance)
        {
            float NutritionLossPerTick;
            switch (WorldManager.CurrentWorldSetting.DifficultySetting.HungerRate)
            {
                case 1.5f: //stationeers difficulty, character food will last 8 game days
                    NutritionLossPerTick = 0.104167f;
                    break;
                case 1f: //normal difficulty, will last 10 game days
                    NutritionLossPerTick = 0.083333f;
                    break;
                case 0.5f: //easy difficulty, will last 12 game days
                    NutritionLossPerTick = 0.069444f;
                    break;
                case 0f: //user disabled food consumption directly on world config
                    NutritionLossPerTick = 0f;
                    break;
                default: // if it's none of the above, will try to calculate NutritionLossPerTick based on DifficultySetting.HungerRate, examples:
                         // Difficulty in 3 should give 0.208334f, will last 4 game days 
                         // Difficulty in 2.25 should give 0.1562505f, will last 6 game days
                         // Difficulty in 0.001 should give 0.055555f, will last 14 days
                    float hungerdifficulty = Mathf.InverseLerp(0f, 3f, WorldManager.CurrentWorldSetting.DifficultySetting.HungerRate);
                    NutritionLossPerTick = Mathf.Lerp(0.055555f, 0.208334f, hungerdifficulty);
                    break;
            }
            LastNutritionLossPerTick = NutritionLossPerTick;

            // Complete rewrite of base method Human.LifeNutrition
            float num = ConfigFile.NutritionLossMultiplier * NutritionLossPerTick * (__instance.OrganBrain.IsOnline ? 1f : WorldManager.CurrentWorldSetting.DifficultySetting.LifeFunctionLoggedOut);
            __instance.Nutrition -= num;
            // Entity.LifeNutrition
            if (__instance.IsArtificial)
            {
                return false;
            }
            if (__instance.Nutrition <= 0f) // increase the damage when nutrition reaches 0
            {
                __instance.DamageState.Damage(ChangeDamageType.Increment, 0.2f, DamageUpdateType.Starvation);
                return false;
            }
            if (__instance.DamageState.Starvation > 0f && __instance.Nutrition >= 400f) // Only heals the character when nutrition is over 400 (after "Nutrition Critical" warning)
            {
                __instance.DamageState.Damage(ChangeDamageType.Decrement, 0.1f, DamageUpdateType.Starvation);
            }
            return false;
        }

        // Calculates the Food and Nutrition to give to the player based on the dayspast in the save, so we don't reward a character who dies with 100% stats
        [HarmonyPatch("OnLifeCreated")]
        [HarmonyPostfix]
        [UsedImplicitly]
        private static void RespawnPatch(Human __instance, ref float ___MaxHydrationStorage, ref float ____hydrationLossPerTick, ref bool isRespawn)
        {
            if (ConfigFile.EnableRespawnPenaltyLogic)
            {
                // First, get how long in days the max nutrition should last with the current configuration parameters
                int NormalizedMaxHungerDays = Mathf.RoundToInt(ConfigFile.MaxNutritionStorage / LastNutritionLossPerTick / 2 / 60 / (20 * Settings.CurrentData.SunOrbitPeriod));
                // Then, calculate a nutrition slice for each day
                float NutritionSlicePerDay = ConfigFile.MaxNutritionStorage / NormalizedMaxHungerDays;
                // Get the normalized days
                float DaysPastNorm = WorldManager.DaysPast * Settings.CurrentData.SunOrbitPeriod;

                // If the character is a new player joining and not a old character who died, and the configfile is set to modify the amount of food to give to the new player
                // apply the desired amount of food for the new character
                if (!isRespawn && ConfigFile.CustomNewPlayerRespawn)
                {
                    __instance.Nutrition = ConfigFile.CustomNewPlayerRespawnNutrition;
                    ModLog.Debug("RespawnPatch: Nutrition given because CustomNewPlayerRespawn is true and a new player joined ---> " + __instance.Nutrition);
                }
                // If it's a respawn and the DaysPastNorm is lower than the NormalizedMaxHungerDays, that means we should calculate the amount of food to give to the respawning character
                else if (DaysPastNorm < NormalizedMaxHungerDays)
                {
                    __instance.Nutrition = NutritionSlicePerDay * (NormalizedMaxHungerDays - DaysPastNorm);
                    ModLog.Info("RespawnPatch: Nutrition given for an player who died and are respawning ---> " + __instance.Nutrition);
                }
                //if DaysPastNorm is equal or bigger than NormalizedMaxHungerDays, that means we should give a minimal amount of food, just enough for the character to go eat something
                else
                {
                    __instance.Nutrition = ConfigFile.MaxNutritionStorage / 100;
                }
                // Now do the same logic, but for Hydration:
                int NormalizedMaxHydrationDays = Mathf.RoundToInt(___MaxHydrationStorage / (____hydrationLossPerTick + ____hydrationLossPerTick * 0.7f)  / 2 / 60 / (20 * Settings.CurrentData.SunOrbitPeriod));
                float HydrationSlicePerDay = ConfigFile.MaxHydrationStorage / NormalizedMaxHydrationDays;
                float HydrationToGive;
                if (!isRespawn && ConfigFile.CustomNewPlayerRespawn)
                {
                    HydrationToGive = ConfigFile.CustomNewPlayerRespawnHydration;
                    ModLog.Debug("RespawnPatch: Hydration given because CustomNewPlayerRespawn is true and a new player joined ---> " + __instance.Nutrition);
                }
                else if (DaysPastNorm < NormalizedMaxHydrationDays)
                {
                    HydrationToGive = HydrationSlicePerDay * (NormalizedMaxHydrationDays - DaysPastNorm);
                    ModLog.Info("RespawnPatch: Hydration given because a player who died are respawning ---> " + HydrationToGive);
                }
                //if DaysPastNorm is equal or bigger than NormalizedMaxHydrationDays, that means we should give a minimal amount of water, just enough for the character to go drink something
                else
                {
                    __instance.Nutrition = ConfigFile.MaxNutritionStorage / 100;
                }
            }
        }
    }




    public static class FoodsValuesPatch
    {
        public static float getFood(String __instance)
        {

            ModLog.Debug("FoodsValuesPatch: getFood---> " + "f" + __instance.Trim().Replace(" ", ""));
            if (typeof(PlantsnNutritionRebalancePlugin).GetField("f" + __instance.Trim().Replace(" ", "")) != null)
            {
                return float.Parse(((Dictionary<String, Object>)typeof(PlantsnNutritionRebalancePlugin).GetField("f" + __instance.Trim().Replace(" ", "")).GetValue(PlantsnNutritionRebalancePlugin.Instance))["NUTV"].ToString());
            }
            return -1f;
        }

        public static float getFoodEatSpeed(String __instance)
        {
            ModLog.Debug("FoodsValuesPatch: getFood---> " + "f" + __instance.Trim().Replace(" ", ""));
            if (typeof(PlantsnNutritionRebalancePlugin).GetField("f" + __instance.Trim().Replace(" ", "")) != null)
            {
                return float.Parse(((Dictionary<String, Object>)typeof(PlantsnNutritionRebalancePlugin).GetField("f" + __instance.Trim().Replace(" ", "")).GetValue(PlantsnNutritionRebalancePlugin.Instance))["SEAT"].ToString());
            }
            return -1f;
        }
    }


    // Update food Nutrition Values
    [HarmonyPatch(typeof(Food))]
    [HarmonyPatch("OnUseSecondary")]
    public class FoodNutritionPatch
    {
        [UsedImplicitly]
        [HarmonyPrefix]
        private static void PatchFoodNutrition(Food __instance)
        {
            try
            {
                float tes = FoodsValuesPatch.getFoodEatSpeed(__instance.DisplayName);
                float tf = FoodsValuesPatch.getFood(__instance.DisplayName);
                if (tes >= 0f && bool.Parse(ConfigFile.fConfigsFood["FCE"].ToString()))
                {
                    ModLog.Debug("FoodsValuesPatch: PatchFoodNutrition ---> EatSpeed : " + tes);
                    __instance.EatSpeed = tes;
                }
                if (tf >= 0f && bool.Parse(ConfigFile.fConfigsFood["FCE"].ToString()))
                {
                    ModLog.Debug("FoodsValuesPatch: PatchFoodNutrition ---> NutritionValue : " + tf);
                    __instance.NutritionValue = tf;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(ex);
            }
        }
    }


    [HarmonyPatch(typeof(StackableFood))]
    [HarmonyPatch("OnUseSecondary")]
    public class StackableFoodNutritionPatch
    {
        [UsedImplicitly]
        [HarmonyPrefix]
        private static void PatchStackableNutrition(StackableFood __instance)
        {
            try
            {
                float tes = FoodsValuesPatch.getFoodEatSpeed(__instance.DisplayName);
                float tf = FoodsValuesPatch.getFood(__instance.DisplayName);
                if (tes >= 0f && bool.Parse(ConfigFile.fConfigsFood["FCE"].ToString()))
                {
                    ModLog.Debug("FoodsValuesPatch: PatchStackableNutrition ---> EatSpeed : " + tes);
                    __instance.EatSpeed = tes;
                }
                if (tf >= 0f && bool.Parse(ConfigFile.fConfigsFood["FCE"].ToString()))
                {
                    ModLog.Debug("FoodsValuesPatch: PatchStackableNutrition ---> NutritionValue : " + tf);
                    __instance.NutritionValue = tf;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(ex);
            }
        }
    }

    [HarmonyPatch(typeof(Plant))]
    [HarmonyPatch("OnUseSecondary")]
    public class PlantNutritionPatch
    {
        [UsedImplicitly]
        [HarmonyPrefix]
        private static void PatchPlantNutrition(Plant __instance)
        {
            try
            {
                float tf = FoodsValuesPatch.getFood(__instance.DisplayName);
                if (tf >= 0f && bool.Parse(ConfigFile.fConfigsFood["PCE"].ToString()))
                {
                    ModLog.Debug("FoodsValuesPatch: PatchPlantNutrition ---> NutritionValue : " + tf);
                    __instance.NutritionValue = tf;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(ex);
            }
        }
    }

    // Update food Nutrition Values
    [HarmonyPatch(typeof(Stationpedia))]
    [HarmonyPatch("AddNutrition")]
    public class StationpediaPatch
    {
        [UsedImplicitly]
        [HarmonyPrefix]
        private static bool AddNutrition(ref Thing prefab, ref StationpediaPage page)
        {
            Food food = prefab as Food;
            if (food != null && bool.Parse(ConfigFile.fConfigsFood["FCE"].ToString()))
            {
                food.NutritionValue = FoodsValuesPatch.getFood(food.DisplayName);
                ModLog.Debug("FoodsValuesPatch: AddNutrition ---> " + prefab.DisplayName + " nut value: " + food.NutritionValue);
            }
            StackableFood stackableFood = prefab as StackableFood;
            if (stackableFood != null && bool.Parse(ConfigFile.fConfigsFood["FCE"].ToString()))
            {
                stackableFood.NutritionValue = FoodsValuesPatch.getFood(stackableFood.DisplayName);
                ModLog.Debug("FoodsValuesPatch: AddNutrition ---> " + prefab.DisplayName + " nut value: " + stackableFood.NutritionValue);
            }

            Plant Plantfood = prefab as Plant;
            if (Plantfood != null && bool.Parse(ConfigFile.fConfigsFood["PCE"].ToString()))
            {
                Plantfood.NutritionValue = FoodsValuesPatch.getFood(Plantfood.DisplayName);
                ModLog.Debug("FoodsValuesPatch: AddNutrition ---> " + prefab.DisplayName + " nut value: " + Plantfood.NutritionValue);
            }
            return true;
        }
    }



    // Changes fertilized Eggs requisites to hatch:
    [HarmonyPatch(typeof(FertilizedEgg))]
    [HarmonyPatch("OnAtmosphericTick")]
    public class FertilizedEggPatch
    {
        [UsedImplicitly]
        [HarmonyPrefix]
        public static bool PatchFertilizedEgg(FertilizedEgg __instance)
        {
            if (__instance.HasAtmosphere == true && __instance.WorldAtmosphere.PressureGasses >= 50f && __instance.WorldAtmosphere.PressureGasses <= 120.5f && __instance.WorldAtmosphere.Temperature >= 309.15f && __instance.WorldAtmosphere.Temperature < 311.65f)
            {
                if (__instance.ParentSlot != null && __instance.ParentSlot.Occupant)
                {
                    //Good enviroment, but in Slot so don't hatch
                    return false;
                }
                //Good enviroment, Hatching.
                __instance.HatchTime -= 0.5f;
                //If it's near hatching (1 day left), then randomly move the egg to simulate the chick trying to pip the shell.
                if (__instance.HatchTime < 2400)
                {
                    if (UnityEngine.Random.Range(0f, 10f) > 9)
                        __instance.RigidBody.AddForce(new Vector3(UnityEngine.Random.Range(-10f, 10f), UnityEngine.Random.Range(-10f, 10f), UnityEngine.Random.Range(-10f, 10f)));
                }
                if (__instance.HatchTime <= 0f)
                {
                    OnServer.Interact(__instance.InteractOnOff, 1, false);
                }
            }
            return false;
        }
    }

    // Changes the hatch time of Fertilized Eggs:
    [HarmonyPatch(typeof(FertilizedEgg))]
    [HarmonyPatch("Awake")]
    public class FertilizedEggAwakePatch
    {
        [UsedImplicitly]
        [HarmonyPostfix]
        private static void PatchFertilizedEggAwake(FertilizedEgg __instance)
        {
            __instance.HatchTime = 16800f; //Should hatch in 7 days
        }
    }

    // Changes the time it takes to spoil eggs:
    [HarmonyPatch(typeof(Item))]
    [HarmonyPatch("Awake")]
    public class EggSpoilPatch
    {
        [UsedImplicitly]
        [HarmonyPostfix]
        private static void PatchEggsSpoil(Item __instance)
        {

            if (__instance.DisplayName == "Egg" || __instance.DisplayName == "Fertilized Egg")
            {
                __instance.DecayRate = 0.000091f; //should spoil in ~12 game days without refrigeration
            }

        }
    }

    [HarmonyPatch(typeof(DynamicThing))]
    [HarmonyPatch("GetPassiveTooltip")]
    public class FertilizedeggSTooltipPatch
    {
        [UsedImplicitly]
        [HarmonyPostfix]
        private static PassiveTooltip PatchFertilizedEggTooltip(PassiveTooltip __result, DynamicThing __instance)
        {
            if (__instance is FertilizedEgg fertilizedEgg)
            {
                __result.Extended = getTooltipText(fertilizedEgg);
            }
            return __result;
        }

        private static string getTooltipText(FertilizedEgg fertilizedEgg)
        {
            string text = "";
            if (fertilizedEgg.HasAtmosphere == true && fertilizedEgg.WorldAtmosphere.PressureGasses >= 50f && fertilizedEgg.WorldAtmosphere.PressureGasses < 120.5f && fertilizedEgg.WorldAtmosphere.Temperature >= 309.15f && fertilizedEgg.WorldAtmosphere.Temperature < 311.65f)
            {
                if (fertilizedEgg.ParentSlot != null && fertilizedEgg.ParentSlot.Occupant)
                {
                    //Good enviroment, but inside a Slot.
                    return text;
                }
                if (fertilizedEgg.HatchTime >= 2400f)
                    text = string.Format("The Egg <color=green>is hatching</color>\n");
                else
                    text = string.Format("The Egg <color=green>is almost hatching </color>\nThe chick <color=green>is trying to pip the shell</color>");
            }
            else
            {
                text = string.Format("The Egg <color=red>is not hatching</color>\n");
                if (fertilizedEgg.HasAtmosphere == false)
                {
                    text += string.Format("The Egg <color=red>needs an atmosphere</color>\n");
                }
                else
                {
                    if (fertilizedEgg.WorldAtmosphere.PressureGasses < 50f)
                        text += string.Format("The Egg <color=red>is in a low pressure environment</color> (P < 50kpa)\n");
                    else if (fertilizedEgg.WorldAtmosphere.PressureGasses >= 120.5f)
                        text += string.Format("The Egg <color=red>is in a high pressure environment</color> (P > 120kpa)\n");
                    if (fertilizedEgg.WorldAtmosphere.Temperature < 309.15f)
                        text += string.Format("The Egg temperature <color=red>is too cold</color> (T < 36°C)\n");
                    else if (fertilizedEgg.WorldAtmosphere.Temperature >= 311.65f)
                        text += string.Format("The Egg temperature <color=red>is too hot</color> (T > 38°C)\n");
                }
            }
            return text;
        }
    }
}
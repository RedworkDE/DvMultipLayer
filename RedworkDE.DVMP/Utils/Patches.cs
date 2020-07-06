using System;
using System.IO;
using DV;
using HarmonyLib;
using UnityEngine;

namespace RedworkDE.DVMP.Utils
{
	/// <summary>
	/// Various minor fixes / conveniences
	/// </summary>
    public class Patches : AutoLoad<Patches>
    {
	    private static readonly FileStream? _fileStream;

		// don't force full screen
	    [HarmonyPatch(typeof(MenuScreenResolutionOptions), nameof(MenuScreenResolutionOptions.UpdateResolution))]
	    [HarmonyPrefix]
	    public static bool MenuScreenResolutionOptions_UpdateResolution_Patch(MenuScreenResolutionOptions __instance, ref int ___screenResolutionIndex, Vector2Int[] ___supportedResolutions)
	    {
		    ___screenResolutionIndex = Mathf.Clamp(___screenResolutionIndex, 0, ___supportedResolutions.Length - 1);
		    Vector2Int supportedResolution = ___supportedResolutions[___screenResolutionIndex];
		    __instance.resolutionButtonValue.text = $"{supportedResolution.x} x {supportedResolution.y}";
		    Screen.SetResolution(supportedResolution.x, supportedResolution.y, Screen.fullScreenMode, 60);
		    return false;
	    }


		// no pause
		[HarmonyPatch(typeof(CanvasSpawner), nameof(CanvasSpawner.OnApplicationFocus))]
		[HarmonyPrefix]
	    public static bool CanvasSpawner_OnApplicationFocus_Patch()
	    {
		    return false;
	    }

	    [HarmonyPatch(typeof(CanvasSpawner), nameof(CanvasSpawner.Open), typeof(MenuScreen), typeof(bool))]
	    [HarmonyPrefix]
	    public static void CanvasSpawner_Open_Patch(ref bool pauseGame)
	    {
		    pauseGame = false;
	    }

	    [HarmonyPatch(typeof(AppUtil), nameof(AppUtil.PauseGame))]
	    [HarmonyPrefix]
	    public static bool AppUtil_PauseGame_Patch()
	    {
		    return false;
	    }

		// enable loco spawning and spawn mode
		[HarmonyPatch(typeof(CommsRadioCarSpawner), nameof(CommsRadioCarSpawner.UpdateCarTypesToSpawn))]
		[HarmonyPrefix]
	    public static void CommsRadioCarSpawner_UpdateCarTypesToSpawn_Patch(ref bool allowLocoSpawning)
	    {
		    allowLocoSpawning = true;
	    }

		[HarmonyPatch(typeof(CommsRadioController), nameof(CommsRadioController.UpdateSpawnModesAvailability))]
		[HarmonyPostfix]
	    public static void CommsRadioController_UpdateSpawnModesAvailability_Patch(CommsRadioController __instance)
	    {
			__instance.disabledModeIndices.Clear();
	    }

	    static Patches()
		{
			// dev console commands
			Environment.SetEnvironmentVariable("DERAIL_VALLEY_DEV", "1", EnvironmentVariableTarget.Process);


			// instance number for obs
			for (int i = 1; i < 1000; i++)
		    {
			    try
			    {
				    Directory.CreateDirectory("Instances");
				    _fileStream = new FileStream("Instances/" + i, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
				    DV.Config.BUILD_DESTINATION += " #" + i;
					return;
			    }
			    catch (IOException)
			    {

			    }
		    }
	    }

	}

}
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RedworkDE.DVMP.Utils
{
	/// <summary>
	/// Loads harmony patches, this allows for patches to directly appear in classes without requiring a dedicated wrapper class
	/// </summary>
	public class HarmonyLoader : AutoLoad<HarmonyLoader>
	{
		static HarmonyLoader()
		{
			// load directly embedded patches

			var harmony = new Harmony("harmony-loader-" + Guid.NewGuid().ToString("N"));

			var assembly = Assembly.GetExecutingAssembly();

			foreach (var type in assembly.GetTypes())
			{
				foreach (var method in type.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
				{
					var harmonyMethods = HarmonyMethodExtensions.GetFromMethod(method);
					if (harmonyMethods is null || !harmonyMethods.Any()) continue;
					var merged = new HarmonyMethod() {methodType = MethodType.Normal}.Merge(HarmonyMethod.Merge(harmonyMethods));
					
					Logger.LogDebug($"found method: {method} / {merged}");
					
					var proc = new PatchProcessor(harmony);

					proc.AddOriginal(PatchProcessor.GetOriginalMethod(merged) /*(MethodBase) Info.OfMethod<PatchProcessor>("GetOriginalMethod").Invoke(null, new[] {merged})*/);

					if (method.GetCustomAttributes<HarmonyTranspiler>().Any())
						proc.AddTranspiler(method);
					if (method.GetCustomAttributes<HarmonyPrefix>().Any())
						proc.AddPrefix(method);
					if (method.GetCustomAttributes<HarmonyPostfix>().Any())
						proc.AddPostfix(method);
					if (method.GetCustomAttributes<HarmonyFinalizer>().Any())
						proc.AddFinalizer(method);

					proc.Patch();
				}
			}

			// load the normal style of patches
			Harmony.CreateAndPatchAll(typeof(AutoLoadManager).Assembly);
		}
	}
}
#if BepInEx
using BepInEx.Logging;
using BepInEx;
#endif
#if UMM

#endif
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;


/// <summary>
/// Main Entry point of the mod, ensure that is type is a MonoBehaviour and add it to an GameObject
/// </summary>
#if BepInEx
[BepInPlugin("59DC751D-53B6-4AE1-B469-5FC507BF24FB", "RedworkDE.DVMP", "0.1")]
#endif
public class AutoLoadManager
#if BepInEx
	: BaseUnityPlugin
#else
	: MonoBehaviour<AutoLoadManager>
#endif
{
#if UMM
    public static bool Load(UnityModManager.ModEntry mod)
    {
        new GameObject("__AutoLoadManager").AddComponent<AutoLoadManager>();

        return true;
    }
#endif

	void Awake()
	{
		foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
		{
			try
			{
				if (type.BaseType is null) continue;

				if (type.BaseType.IsGenericType && type.BaseType.GetGenericTypeDefinition() == typeof(AutoLoad<>))
				{
					Logger.LogDebug($"Loading {type}");
					RuntimeHelpers.RunClassConstructor(type.TypeHandle);
				}

				if (type.BaseType.IsGenericType && type.BaseType.GetGenericTypeDefinition() == typeof(AutoLoadMonoBehaviour<>))
				{
					Logger.LogDebug($"Loading {type}");
					RuntimeHelpers.RunClassConstructor(type.TypeHandle);
				}

				if (type.BaseType.IsGenericType && type.BaseType.GetGenericTypeDefinition() == typeof(AutoCreateMonoBehaviour<>))
				{
					Logger.LogDebug($"Loading {type}");
					RuntimeHelpers.RunClassConstructor(type.TypeHandle);
					gameObject.AddComponent(type);
				}
			}
			catch (Exception ex)
			{
				Logger.LogDebug($"Error checking {type}: {ex}");
			}
		}
	}
}

/// <summary>
/// MonoBehaviour that has a logger for type <typeparamref name="T"/>
/// </summary>
public class MonoBehaviour<T> : MonoBehaviour where T : MonoBehaviour<T>
{
	protected static readonly ManualLogSource Logger = Logging<T>.Logger;
}

/// <summary>
/// Class that has a logger for type <typeparamref name="T"/>
/// </summary>
public class HasLogger<T> where T : HasLogger<T>
{
	protected static readonly ManualLogSource Logger = Logging<T>.Logger;
}

/// <summary>
/// MonoBehaviour whose static Constructor is is ran automatically
/// </summary>
public class AutoLoadMonoBehaviour<T> : MonoBehaviour<T> where T : AutoLoadMonoBehaviour<T>
{
}

/// <summary>
/// MonoBehaviour that is automatically added to some GameObject
/// </summary>
public class AutoCreateMonoBehaviour<T> : MonoBehaviour<T> where T : AutoCreateMonoBehaviour<T>
{
}

/// <summary>
/// Class whose static Constructor is is ran automatically
/// </summary>
public class AutoLoad<T> : HasLogger<T> where T : AutoLoad<T>
{
}

#if UMM
public class ManualLogSource
{
	public void LogError(string str) { }
	public void LogWarning(string str) { }
	public void LogInfo(string str) { }
	public void LogDebug(string str) { }
}
#endif

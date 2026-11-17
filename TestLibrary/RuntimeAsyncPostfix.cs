#if NET11_0_OR_GREATER
using HarmonyLib;
using System.Threading.Tasks;

namespace TestLibrary;

[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoOtherAsync))]
public static class RuntimeAsyncVoidPostfix
{
	public static async Task Postfix()
	{
		AsyncTargets.Events.Add("postfix:start");
		await Task.Yield();
		AsyncTargets.Events.Add("postfix:end");
	}
}
#endif

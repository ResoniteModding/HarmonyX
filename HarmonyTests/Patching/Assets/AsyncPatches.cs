#if NET452_OR_GREATER || NETCOREAPP
using System.Threading.Tasks;
using HarmonyLib;
using TestLibrary;

namespace HarmonyLibTests.Assets
{
	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoAsync))]
	public static class AsyncTaskResultPatch
	{
		public static async Task Postfix(Task __result)
		{
			AsyncTargets.Events.Add("postfix:start");
			await __result;
			AsyncTargets.Events.Add("postfix:end");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncValueResultPatch
	{
		public static void Postfix(int __result)
		{
			AsyncTargets.Events.Add($"postfix:result:{__result}");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncTaskOfTReturnPatch
	{
		public static async Task<int> Postfix(int __result)
		{
			AsyncTargets.Events.Add($"postfix:start:{__result}");
			await Task.Yield();
			AsyncTargets.Events.Add("postfix:end");
			return __result + 5;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncIntReturnPatch
	{
		public static int Postfix(int __result) => __result + 5;
	}

#if NET11_0_OR_GREATER
	[HarmonyPatch(typeof(InFlightTargets), nameof(InFlightTargets.RunAsync))]
	public static class InFlightPatch
	{
		public static void Postfix(int __result) => InFlightTargets.Events.Add($"postfix:{__result}");
	}
#endif

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.RefSumAsync))]
	public static class AsyncRefValueResultPatch
	{
		public static void Postfix(ref int __result)
		{
			AsyncTargets.Events.Add($"postfix:before:{__result}");
			__result += 1;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncTaskOfTResultPatch
	{
		public static async Task Postfix(Task<int> __result)
		{
			AsyncTargets.Events.Add($"postfix:result:{await __result}");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.RefSumAsync))]
	public static class AsyncRefTaskOfTResultPatch
	{
		public static void Postfix(ref Task<int> __result)
		{
			AsyncTargets.Events.Add($"postfix:result:{__result.Result}");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoRefAsync))]
	public static class AsyncRefTaskResultPatch
	{
		public static void Postfix(ref Task __result)
		{
			AsyncTargets.Events.Add($"postfix:completed:{__result.IsCompleted}");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync), MethodType.Async)]
	public static class AsyncMethodTypePatch
	{
		public static void Postfix() { }
	}

#if NETCOREAPP
	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumValueAsync))]
	public static class AsyncValueTaskOfTReturnPatch
	{
		public static async ValueTask<int> Postfix(int __result)
		{
			AsyncTargets.Events.Add($"postfix:start:{__result}");
			await Task.Yield();
			AsyncTargets.Events.Add("postfix:end");
			return __result + 5;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumValueAsync))]
	public static class AsyncValueTaskOfTResultPatch
	{
		public static async Task Postfix(ValueTask<int> __result)
		{
			AsyncTargets.Events.Add($"postfix:result:{await __result}");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.RefSumValueAsync))]
	public static class AsyncRefValueTaskOfTResultPatch
	{
		public static void Postfix(ref ValueTask<int> __result)
		{
			AsyncTargets.Events.Add($"postfix:result:{__result.Result}");
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoValueAsync))]
	public static class AsyncValueTaskResultPatch
	{
		public static void Postfix(ValueTask __result)
		{
			AsyncTargets.Events.Add($"postfix:completed:{__result.IsCompleted}");
		}
	}
#endif
}
#endif

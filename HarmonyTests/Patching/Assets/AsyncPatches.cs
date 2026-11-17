#if NET452_OR_GREATER || NETCOREAPP
using System;
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

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoAsync))]
	public static class AsyncBoolPrefixPatch
	{
		public static async Task<bool> Prefix()
		{
			AsyncTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncTargets.Events.Add("prefix:end");
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoAsync))]
	public static class AsyncBoolPrefixContinuePatch
	{
		public static async Task<bool> Prefix()
		{
			AsyncTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncTargets.Events.Add("prefix:end");
			return true;
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoValueAsync))]
	public static class AsyncValueTaskBoolPrefixPatch
	{
		public static async ValueTask<bool> Prefix()
		{
			AsyncPrefixTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncPrefixTargets.Events.Add("prefix:end");
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoAsync))]
	public static class AsyncVoidPrefixPatch
	{
		public static async Task Prefix()
		{
			AsyncTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncTargets.Events.Add("prefix:end");
		}
	}

	public static class AsyncIntPrefix
	{
		public static async Task<int> Prefix()
		{
			await Task.Yield();
			return 1;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncRefTaskPrefix
	{
		public static bool Prefix(ref Task<int> __result)
		{
			AsyncTargets.Events.Add("prefix:ref");
			__result = Task.FromResult(100);
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncRefTaskRunPrefix
	{
		public static bool Prefix(ref Task<int> __result)
		{
			__result = Task.FromException<int>(new Exception("the assigned task must not be awaited"));
			return true;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumValueAsync))]
	public static class AsyncRefValueTaskPrefix
	{
		public static bool Prefix(ref ValueTask<int> __result)
		{
			__result = new ValueTask<int>(200);
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoAsync))]
	public static class AsyncRefVoidTaskPrefix
	{
		public static bool Prefix(ref Task __result)
		{
			AsyncTargets.Events.Add("prefix:ref");
			__result = Task.CompletedTask;
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.TryCheckAsync))]
	public static class AsyncRefBoolTaskPrefix
	{
		public static bool Prefix(ref Task<bool> __result)
		{
			AsyncPrefixTargets.Events.Add("prefix:ref");
			__result = Task.FromResult(true);
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.TryCheckAsync))]
	public static class AsyncBoolControlPrefix
	{
		public static async Task<bool> Prefix()
		{
			AsyncPrefixTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncPrefixTargets.Events.Add("prefix:end");
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.BoxAsync))]
	public static class AsyncRefBoxedTaskPrefix
	{
		public static bool Prefix(ref Task<int> __result)
		{
			AsyncPrefixTargets.Events.Add("prefix:ref");
			__result = Task.FromResult(7);
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.FormatAsync))]
	public static class AsyncRefBoxedInterfaceTaskPrefix
	{
		public static bool Prefix(ref Task<int> __result)
		{
			AsyncPrefixTargets.Events.Add("prefix:ref");
			__result = Task.FromResult(9);
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.TextAsync))]
	public static class AsyncRefTextTaskPrefix
	{
		public static bool Prefix(ref Task<string> __result)
		{
			AsyncPrefixTargets.Events.Add("prefix:ref");
			__result = Task.FromResult("prefix");
			return false;
		}
	}

	public static class AsyncRefTaskStringPrefix
	{
		public static bool Prefix(ref Task<string> __result)
		{
			__result = Task.FromResult("wrong");
			return false;
		}
	}

	public static class AsyncRefTaskPostfix
	{
		public static void Postfix(ref Task<int> __result) { }
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoRejectAsync))]
	public static class AsyncInvalidResultPrefixPatch
	{
		public static async Task<int> Prefix()
		{
			await Task.Yield();
			return 1;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncRefValueTaskOnTaskPrefix
	{
		public static bool Prefix(ref ValueTask<int> __result)
		{
			__result = new ValueTask<int>(200);
			return false;
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncRefTaskUnassignedPrefix
	{
		public static bool Prefix(ref Task<int> __result) => true;
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncSkipPrefix
	{
		public static bool Prefix() => false;
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncAwaitableResultPostfixPatch
	{
		public static void Postfix(ref Task<int> __result) { }
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.DoValueAsync))]
	public static class AsyncRefVoidValueTaskPrefix
	{
		public static bool Prefix(ref ValueTask __result)
		{
			AsyncTargets.Events.Add("prefix:valuetask");
			__result = ValueTask.CompletedTask;
			return false;
		}
	}


	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncCombinedPrefixSkipPatch
	{
		public static async Task<(bool, int)> Prefix()
		{
			AsyncTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncTargets.Events.Add("prefix:end");
			return (false, 100);
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync))]
	public static class AsyncCombinedPrefixRunPatch
	{
		public static async Task<(bool, int)> Prefix()
		{
			AsyncTargets.Events.Add("prefix:start");
			await Task.Yield();
			AsyncTargets.Events.Add("prefix:end");
			return (true, 0);
		}
	}

	[HarmonyPatch(typeof(AsyncTargets), nameof(AsyncTargets.SumValueAsync))]
	public static class AsyncValueTaskCombinedPrefixPatch
	{
		public static async ValueTask<(bool, int)> Prefix()
		{
			await Task.Yield();
			return (false, 200);
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.TryCheckAsync))]
	public static class AsyncCombinedBoolPrefixPatch
	{
		public static async Task<(bool, bool)> Prefix()
		{
			await Task.Yield();
			return (false, true);
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.BoxAsync))]
	public static class AsyncCombinedBoxedPrefixPatch
	{
		public static async Task<(bool, int)> Prefix()
		{
			await Task.Yield();
			return (false, 7);
		}
	}

	public static class AsyncCombinedStringPrefix
	{
		public static async Task<(bool, string)> Prefix()
		{
			await Task.Yield();
			return (false, "wrong");
		}
	}

	[HarmonyPatch(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoRejectAsync))]
	public static class AsyncCombinedVoidPrefixPatch
	{
		public static async Task<(bool, int)> Prefix()
		{
			await Task.Yield();
			return (false, 1);
		}
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

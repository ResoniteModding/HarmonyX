#if NET452_OR_GREATER || NETCOREAPP
using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using TestLibrary;

namespace HarmonyLibTests.Patching
{
	[TestFixture, NonParallelizable]
	public class AsyncPatching : TestLogger
	{
		static async Task<int> ClassicSumAsync(int a, int b)
		{
			await Task.Yield();
			return a + b;
		}

		static async Task<int> ClassicSumPostfix(Task<int> __result)
		{
			return await __result;
		}

		static void ReplaceWith42(ref Task<int> __result) => __result = Task.FromResult(42);

		static void AssertEvents(params string[] expected)
		{
			CollectionAssert.AreEqual(expected, AsyncTargets.Events);
		}

		[Test]
		public void Test_ClassicAsyncPostfix_ChainsResult()
		{
			var instance = new Harmony("test.async.classicchain");
			_ = instance.Patch(
				AccessTools.Method(typeof(AsyncPatching), nameof(ClassicSumAsync), [typeof(int), typeof(int)]),
				postfix: AccessTools.Method(typeof(AsyncPatching), nameof(ClassicSumPostfix)));

			var result = ClassicSumAsync(2, 3).GetAwaiter().GetResult();

			Assert.AreEqual(5, result);
		}

#if NET11_0_OR_GREATER
		[TestCase(typeof(AsyncTaskResultPatch), nameof(AsyncTargets.DoAsync))]
		[TestCase(typeof(AsyncTaskOfTResultPatch), nameof(AsyncTargets.SumAsync))]
		[TestCase(typeof(AsyncRefTaskOfTResultPatch), nameof(AsyncTargets.RefSumAsync))]
		[TestCase(typeof(AsyncRefTaskResultPatch), nameof(AsyncTargets.DoRefAsync))]
		[TestCase(typeof(AsyncValueTaskOfTResultPatch), nameof(AsyncTargets.SumValueAsync))]
		[TestCase(typeof(AsyncRefValueTaskOfTResultPatch), nameof(AsyncTargets.RefSumValueAsync))]
		[TestCase(typeof(AsyncValueTaskResultPatch), nameof(AsyncTargets.DoValueAsync))]
		public void Test_AwaitableResult_IsRejected(Type patchType, string targetName)
		{
			var target = AccessTools.Method(typeof(AsyncTargets), targetName);
			var postfix = AccessTools.Method(patchType, "Postfix");

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.reject.awaitable").Patch(target, postfix: new HarmonyMethod(postfix)));

			StringAssert.Contains("Runtime-async methods do not materialize their", exception.Message);

			var registered = Harmony.GetPatchInfo(target)?.Postfixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, postfix);
		}

		[Test]
		public void Test_AsyncValueResult_ReceivesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.valueresult");
			try
			{
				instance.PatchAll(typeof(AsyncValueResultPatch));

				Assert.AreEqual(5, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:result:5");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefValueResult_CanChangeValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvalueresult");
			try
			{
				instance.PatchAll(typeof(AsyncRefValueResultPatch));

				Assert.AreEqual(6, AsyncTargets.RefSumAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:before:5");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncTaskOfTReturn_ReplacesValueAfterOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.taskoftreturn");
			try
			{
				instance.PatchAll(typeof(AsyncTaskOfTReturnPatch));

				Assert.AreEqual(10, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
				AssertEvents("original:start", "original:end", "postfix:start:5", "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_SyncIntReturn_ReplacesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.intreturn");
			try
			{
				instance.PatchAll(typeof(AsyncIntReturnPatch));

				Assert.AreEqual(10, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_PatchWhileSuspended_ResumesOriginalVersion()
		{
			InFlightTargets.Reset();
			var gate = InFlightTargets.Gate;
			var first = InFlightTargets.RunAsync(1);
			Assert.AreEqual("start", InFlightTargets.Events[0]);

			var instance = new Harmony("test.async.inflight");
			try
			{
				instance.PatchAll(typeof(InFlightPatch));

				gate.SetResult(10);
				Assert.AreEqual(11, first.GetAwaiter().GetResult());
				CollectionAssert.DoesNotContain(InFlightTargets.Events, "postfix:11");

				InFlightTargets.Reset();
				var second = InFlightTargets.RunAsync(2);
				InFlightTargets.Gate.SetResult(10);
				Assert.AreEqual(12, second.GetAwaiter().GetResult());
				CollectionAssert.Contains(InFlightTargets.Events, "postfix:12");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncValueTaskOfTReturn_ReplacesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.valuetaskoftreturn");
			try
			{
				instance.PatchAll(typeof(AsyncValueTaskOfTReturnPatch));

				Assert.AreEqual(10, AsyncTargets.SumValueAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_MethodTypeAsync_OnRuntimeAsync_IsRejected()
		{
			var instance = new Harmony("test.async.methodtype.reject");

			var exception = Assert.Throws<HarmonyException>(() => instance.PatchAll(typeof(AsyncMethodTypePatch)));

			Assert.IsInstanceOf<NotSupportedException>(exception.InnerException);
			StringAssert.Contains("no state machine", exception.InnerException.Message);
		}

		[Test]
		public void Test_AsyncVoidPostfix_RunsAfterOriginalCompletes()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.voidpostfix");
			try
			{
				instance.PatchAll(typeof(RuntimeAsyncVoidPostfix));

				AsyncTargets.DoOtherAsync().GetAwaiter().GetResult();

				AssertEvents("original:start", "original:end", "postfix:start", "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		// MonoMod detours the runtime-async variant too (MonoMod PR 346, addresses issue 344), so the patch
		// must be visible to a caller whose async variant is compiled only after the patch is installed
		[Test]
		public void Test_OrdinaryTaskMethod_SeenByRuntimeAsyncCaller()
		{
			var original = AccessTools.Method(typeof(OrdinaryTaskTarget), nameof(OrdinaryTaskTarget.Ordinary));
			var nativeCaller = AccessTools.Method(typeof(OrdinaryTaskTarget), nameof(OrdinaryTaskTarget.NativeCaller));
			Assert.False(AccessTools.IsRuntimeAsync(original));
			Assert.True(AccessTools.IsRuntimeAsync(nativeCaller));

			var instance = new Harmony("test.async.ordinary");
			try
			{
				instance.Patch(original, postfix: new HarmonyMethod(AccessTools.Method(typeof(AsyncPatching), nameof(ReplaceWith42))));

				Assert.AreEqual(42, OrdinaryTaskTarget.Ordinary(5).GetAwaiter().GetResult());
				Assert.AreEqual(42, OrdinaryTaskTarget.NativeCaller(5).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}

			Assert.AreEqual(6, OrdinaryTaskTarget.Ordinary(5).GetAwaiter().GetResult());
			Assert.AreEqual(6, OrdinaryTaskTarget.NativeCaller(5).GetAwaiter().GetResult());
		}

		[Test]
		public void Test_OrdinaryVirtualMethod_SeenByRuntimeAsyncCaller()
		{
			var derive = new OrdinaryVirtualDerived();
			Assert.AreEqual(7, OrdinaryVirtualCaller.NativeVirtualCaller(derive, 5).GetAwaiter().GetResult());

			var instance = new Harmony("test.async.ordinaryvirtual");
			try
			{
				instance.Patch(
					AccessTools.Method(typeof(OrdinaryVirtualDerived), nameof(OrdinaryVirtualDerived.Compute)),
					postfix: new HarmonyMethod(AccessTools.Method(typeof(AsyncPatching), nameof(ReplaceWith42))));

				Assert.AreEqual(42, derive.Compute(5).GetAwaiter().GetResult());
				Assert.AreEqual(42, OrdinaryVirtualCaller.NativeVirtualCaller(derive, 5).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}

			Assert.AreEqual(7, derive.Compute(5).GetAwaiter().GetResult());
			Assert.AreEqual(7, OrdinaryVirtualCaller.NativeVirtualCaller(derive, 5).GetAwaiter().GetResult());
		}

		[Test]
		public void Test_RuntimeAsyncReflection()
		{
			var method = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));

			Assert.True(AccessTools.IsRuntimeAsync(method));
			Assert.AreEqual(typeof(Task<int>), AccessTools.GetReturnedType(method));
			// Runtime-async methods have no state machine, so MethodType.Async cannot resolve them. Patch the method itself.
			Assert.IsNull(AccessTools.AsyncMoveNext(method));
		}
#endif
	}
}
#endif

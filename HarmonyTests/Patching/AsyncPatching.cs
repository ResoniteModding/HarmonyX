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

		static async Task<int> ClassicRejectAsync(int a)
		{
			await Task.Yield();
			return a;
		}

		static void ReplaceWith42(ref Task<int> __result) => __result = Task.FromResult(42);

		static void AssertEvents(params string[] expected)
		{
			CollectionAssert.AreEqual(expected, AsyncTargets.Events);
		}

		static InvalidHarmonyPatchArgumentException AssertRejected(Action action)
		{
			var exception = Assert.Throws<HarmonyException>(() => action());
			for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
				if (inner is InvalidHarmonyPatchArgumentException invalid)
					return invalid;
			Assert.Fail($"Expected {nameof(InvalidHarmonyPatchArgumentException)} in {exception}");
			return null;
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
		[TestCase(typeof(AsyncRefTaskOfTResultPatch), nameof(AsyncTargets.RefSumAsync))]
		[TestCase(typeof(AsyncRefTaskResultPatch), nameof(AsyncTargets.DoRefAsync))]
		[TestCase(typeof(AsyncRefValueTaskOfTResultPatch), nameof(AsyncTargets.RefSumValueAsync))]
		public void Test_AwaitableRefResult_IsRejected(Type patchType, string targetName)
		{
			var target = AccessTools.Method(typeof(AsyncTargets), targetName);
			var postfix = AccessTools.Method(patchType, "Postfix");

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.reject.awaitableref").Patch(target, postfix: new HarmonyMethod(postfix)));

			StringAssert.Contains("do not materialize", exception.Message);

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
		public void Test_GenericSyncMethod_DummyDetour()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.EchoSync)).MakeGenericMethod(typeof(int));
			Assert.False(AccessTools.IsRuntimeAsync(target));
			var instance = new Harmony("test.sync.generic.dummy");
			try
			{
				instance.Patch(target);

				Assert.AreEqual(7, target.Invoke(null, new object[] { 7 }));
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		[Ignore("MonoMod 25.4.0-prerelease.3 still rejects runtime-async generic methods in Core110Runtime.TryGetRuntimeAsyncInfo (MonoMod#298)")]
		public void Test_GenericAsyncMethod_DummyDetour()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.EchoAsync)).MakeGenericMethod(typeof(int));
			Assert.True(AccessTools.IsRuntimeAsync(target));
			var instance = new Harmony("test.async.generic.dummy");
			try
			{
				instance.Patch(target);

				Assert.AreEqual(7, ((Task<int>)target.Invoke(null, new object[] { 7 })).GetAwaiter().GetResult());
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
		public void Test_MethodTypeAsync_OnRuntimeAsync_IsRejected()
		{
			var instance = new Harmony("test.async.methodtype.reject");

			var exception = Assert.Throws<HarmonyException>(() => instance.PatchAll(typeof(AsyncMethodTypePatch)));

			Assert.IsInstanceOf<NotSupportedException>(exception.InnerException);
			StringAssert.Contains("no state machine", exception.InnerException.Message);
		}

		[Test]
		public void Test_AsyncInvalidResultPrefix_OnVoidPatchAll_RejectedBeforeRegistration()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoRejectAsync));
			Assert.True(AccessTools.IsRuntimeAsync(target));
			var prefix = AccessTools.Method(typeof(AsyncInvalidResultPrefixPatch),
				nameof(AsyncInvalidResultPrefixPatch.Prefix));

			var exception = AssertRejected(() =>
				new Harmony("test.async.invalidresult.patchall").PatchAll(typeof(AsyncInvalidResultPrefixPatch)));

			StringAssert.Contains("only `bool` or `void`", exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, prefix);
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_ReplacesResultAndSkipsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.reftask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefTaskPrefix));

				var result = AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult();

				Assert.AreEqual(100, result);
				CollectionAssert.DoesNotContain(AsyncTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_TrueRunsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.reftask.run");
			try
			{
				instance.PatchAll(typeof(AsyncRefTaskRunPrefix));

				var result = AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult();

				Assert.AreEqual(5, result);
				AssertEvents("original:start", "original:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefValueTaskPrefix_ReplacesResult()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvaluetask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefValueTaskPrefix));

				Assert.AreEqual(200, AsyncTargets.SumValueAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefVoidTaskPrefix_AwaitsAndSkipsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvoidtask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefVoidTaskPrefix));

				AsyncTargets.DoAsync().GetAwaiter().GetResult();

				AssertEvents("prefix:ref");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_ConflictingAwaitablePrefixResults_RejectedBeforeRegistration()
		{
			AsyncTargets.Reset();
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));
			var first = AccessTools.Method(typeof(AsyncRefTaskPrefix), nameof(AsyncRefTaskPrefix.Prefix));
			var second = AccessTools.Method(typeof(AsyncRefValueTaskOnTaskPrefix), nameof(AsyncRefValueTaskOnTaskPrefix.Prefix));
			var instance = new Harmony("test.async.conflict");
			try
			{
				instance.Patch(target, prefix: new HarmonyMethod(first));

				var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
					instance.Patch(target, prefix: new HarmonyMethod(second)));

				StringAssert.Contains("__result", exception.Message);
				var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
				CollectionAssert.DoesNotContain(registered, second);
				Assert.AreEqual(100, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_UnassignedAwaitablePrefix_DefaultsResultWhenSkipped()
		{
			AsyncTargets.Reset();
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));
			var instance = new Harmony("test.async.unassigned");
			try
			{
				instance.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(AsyncRefTaskUnassignedPrefix), nameof(AsyncRefTaskUnassignedPrefix.Prefix))));
				instance.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(AsyncSkipPrefix), nameof(AsyncSkipPrefix.Prefix))));

				Assert.AreEqual(0, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefVoidValueTaskPrefix_AwaitsAndSkipsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvoidvaluetask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefVoidValueTaskPrefix));

				AsyncTargets.DoValueAsync().GetAwaiter().GetResult();

				AssertEvents("prefix:valuetask");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AwaitableResultPostfix_OnPatchAll_RejectedBeforeRegistration()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));
			var postfix = AccessTools.Method(typeof(AsyncAwaitableResultPostfixPatch), nameof(AsyncAwaitableResultPostfixPatch.Postfix));

			var exception = AssertRejected(() =>
				new Harmony("test.async.awaitablepostfix.patchall").PatchAll(typeof(AsyncAwaitableResultPostfixPatch)));

			StringAssert.Contains("do not materialize", exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Postfixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, postfix);
		}

		[Test]
		public void Test_AsyncRefBoolTaskPrefix_ReplacesBoolResult()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.refbool.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefBoolTaskPrefix));

				Assert.IsTrue(AsyncPrefixTargets.TryCheckAsync(false).GetAwaiter().GetResult());
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_BoxesResultForReferenceReturnType()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.refboxed");
			try
			{
				instance.PatchAll(typeof(AsyncRefBoxedTaskPrefix));

				var result = AsyncPrefixTargets.BoxAsync().GetAwaiter().GetResult();

				Assert.IsInstanceOf<int>(result);
				Assert.AreEqual(7, result);
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_BoxesResultForInterfaceReturnType()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.refboxedinterface");
			try
			{
				instance.PatchAll(typeof(AsyncRefBoxedInterfaceTaskPrefix));

				var result = AsyncPrefixTargets.FormatAsync().GetAwaiter().GetResult();

				Assert.IsInstanceOf<int>(result);
				Assert.AreEqual(9, result);
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_KeepsReferenceResult()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.reftext");
			try
			{
				instance.PatchAll(typeof(AsyncRefTextTaskPrefix));

				var result = AsyncPrefixTargets.TextAsync("original").GetAwaiter().GetResult();

				Assert.AreEqual("prefix", result);
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_WrongElement_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.SumRejectAsync));
			var prefix = AccessTools.Method(typeof(AsyncRefTaskStringPrefix), nameof(AsyncRefTaskStringPrefix.Prefix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.refstring.reject").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains(typeof(int).FullName, exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, prefix);
		}

		[Test]
		public void Test_AsyncRefTaskPostfix_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));
			var postfix = AccessTools.Method(typeof(AsyncRefTaskPostfix), nameof(AsyncRefTaskPostfix.Postfix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.refpostfix.reject").Patch(target, postfix: new HarmonyMethod(postfix)));

			StringAssert.Contains("do not materialize", exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Postfixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, postfix);
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
